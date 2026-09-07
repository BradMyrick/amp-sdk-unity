// AMP Unity SDK — the client. Full matchmaker API with auto-signing,
// Task-based async, and typed results for the hot paths.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Amp.Sdk
{
    /// <summary>
    /// The AMP client. Create one per player (or share an anonymous one for
    /// public reads). All calls are async; callback continuations run on the
    /// thread pool — marshal to the main thread via AmpManager.
    /// </summary>
    public class AmpClient
    {
        /// <summary>Production matchmaker.</summary>
        public const string DefaultServer = "https://amp.playwithamp.xyz";

        private readonly HttpClient _http;
        private readonly string _server;
        private readonly IAmpSigner _signer;
        private string _token;
        private string _wallet;

        public string Wallet => _wallet;
        public bool Authenticated => !string.IsNullOrEmpty(_token);
        public long ChainId { get; set; } = AmpCrypto.DefaultChainId;
        public string ContractAddress { get; set; } = AmpCrypto.DefaultContract;

        public AmpClient(string serverUrl = DefaultServer, IAmpSigner signer = null)
        {
            _server = (serverUrl ?? DefaultServer).TrimEnd('/');
            _signer = signer;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("amp-sdk-unity/0.1");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        // ── Auth ──────────────────────────────────────────────────

        /// <summary>Gasless wallet login: challenge → EIP-191 sign → verify.</summary>
        public async Task<AmpPlayer> LoginAsync()
        {
            if (_signer == null)
                throw new InvalidOperationException("No signer configured");

            _wallet = _signer.GetAddress();

            var challengeJson = await PostAsync("/v1/auth/challenge",
                AmpJson.Build(new Dictionary<string, string> { ["wallet"] = _wallet }));
            var challenge = AmpJson.GetString(challengeJson, "challenge");
            if (string.IsNullOrEmpty(challenge))
                throw new AmpException("auth", "Server returned no challenge");

            var signature = await _signer.SignPersonalSign(challenge);

            var verifyJson = await PostAsync("/v1/auth/verify",
                AmpJson.Build(new Dictionary<string, string>
                {
                    ["wallet"] = _wallet,
                    ["signature"] = signature,
                    ["challenge"] = challenge,
                }));
            _token = AmpJson.GetString(verifyJson, "token");
            if (string.IsNullOrEmpty(_token))
                throw new AmpException("auth", "Server returned no token");

            return new AmpPlayer { Wallet = _wallet, Region = "na", Language = "en" };
        }

        public void Logout() => _token = null;

        // ── Games & player ────────────────────────────────────────

        public async Task<AmpPlayer> MeAsync()
        {
            var json = await GetAsync("/v1/me");
            return new AmpPlayer { Wallet = AmpJson.GetString(json, "wallet") ?? _wallet };
        }

        /// <summary>Raw /v1/me JSON (wallet, ratings, liveMatchId).</summary>
        public Task<string> MeRawAsync() => GetAsync("/v1/me");

        public Task<string> GetPlayerAsync(string wallet) =>
            GetAsync("/v1/players/" + wallet);

        public async Task<List<AmpGameInfo>> GetGamesAsync()
        {
            var json = await GetAsync("/v1/games");
            var games = new List<AmpGameInfo>();
            foreach (var raw in AmpJson.GetArrayObjects(json, "games"))
            {
                var game = new AmpGameInfo
                {
                    Id = AmpJson.GetString(raw, "id") ?? "",
                    Name = AmpJson.GetString(raw, "name") ?? "",
                };
                foreach (var ruleRaw in AmpJson.GetArrayObjects(raw, "rulesets"))
                {
                    game.Rulesets.Add(new AmpRuleset
                    {
                        Id = AmpJson.GetString(ruleRaw, "id") ?? "",
                        Name = AmpJson.GetString(ruleRaw, "name") ?? "",
                        QueueDepth = (int)AmpJson.GetInt(ruleRaw, "queueDepth"),
                    });
                }
                games.Add(game);
            }
            return games;
        }

        // ── Queue ─────────────────────────────────────────────────

        public Task<string> JoinQueueAsync(string gameId, string rulesetId) =>
            PostAsync("/v1/queue/join", AmpJson.Build(
                new Dictionary<string, string> { ["gameId"] = gameId, ["rulesetId"] = rulesetId }));

        public Task<string> LeaveQueueAsync() => PostAsync("/v1/queue/leave", "{}");

        public async Task<AmpQueueStatus> QueueStatusAsync()
        {
            var json = await GetAsync("/v1/queue/status");
            return new AmpQueueStatus
            {
                Queued = AmpJson.GetBool(json, "queued"),
                Depth = (int)AmpJson.GetInt(json, "depth"),
                WaitedMs = AmpJson.GetInt(json, "waitedMs"),
                SkillWindow = AmpJson.GetDouble(json, "skillWindow"),
            };
        }

        public async Task<string> PlayBotAsync()
        {
            var json = await PostAsync("/v1/queue/play-bot", "{}");
            return AmpJson.GetString(json, "matchId");
        }

        /// <summary>One call: poll me().liveMatchId every 2 s until a match lands.</summary>
        public async Task<AmpMatchFound> WaitForMatchAsync(
            double timeoutSeconds = 30,
            Func<Task> onPoll = null)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var json = await GetAsync("/v1/me");
                var live = AmpJson.GetString(json, "liveMatchId");
                if (!string.IsNullOrEmpty(live))
                    return new AmpMatchFound { MatchId = live };
                if (onPoll != null) await onPoll();
                await Task.Delay(2000);
            }
            throw new AmpException("timeout", $"No match within {timeoutSeconds:F0}s");
        }

        // ── Matches (1v1) ─────────────────────────────────────────

        public Task<string> GetMatchAsync(string matchId) => GetAsync("/v1/matches/" + matchId);

        public Task<string> MatchHistoryAsync(int limit = 20, int offset = 0) =>
            GetAsync($"/v1/matches/history?limit={limit}&offset={offset}");

        /// <summary>Report your 1v1 result — auto-signs EIP-191 (gasless).</summary>
        public async Task<string> ReportMatchAsync(string matchId, AmpMatchResult result)
        {
            var resultStr = result == AmpMatchResult.Win ? "win"
                          : result == AmpMatchResult.Loss ? "loss" : "draw";
            var signature = _signer != null
                ? await _signer.SignPersonalSign(AmpCrypto.BuildReportMessage(matchId, resultStr))
                : null;

            return await PostAsync($"/v1/matches/{matchId}/report", AmpJson.Build(
                new Dictionary<string, string> { ["result"] = resultStr, ["signature"] = signature }));
        }

        /// <summary>Verify on-chain escrow for a staked 1v1 (participant only).</summary>
        public Task<string> VerifyEscrowAsync(string matchId) =>
            PostAsync($"/v1/matches/{matchId}/escrow/verify", "{}");

        // ── Parties ───────────────────────────────────────────────

        public async Task<AmpPartyCreated> CreatePartyAsync(string gameId, string rulesetId)
        {
            var json = await PostAsync("/v1/parties", AmpJson.Build(
                new Dictionary<string, string> { ["game_id"] = gameId, ["ruleset_id"] = rulesetId }));
            return new AmpPartyCreated
            {
                PartyId = AmpJson.GetString(json, "partyId"),
                InviteCode = AmpJson.GetString(json, "inviteCode"),
                Leader = AmpJson.GetString(json, "leader"),
            };
        }

        public Task<string> JoinPartyAsync(string inviteCode) =>
            PostAsync("/v1/parties/join", AmpJson.Build(
                new Dictionary<string, string> { ["invite_code"] = (inviteCode ?? "").ToUpperInvariant() }));

        public Task<string> GetPartyAsync(string partyId) => GetAsync("/v1/parties/" + partyId);

        public Task<string> LockPartyAsync(string partyId) =>
            PostAsync($"/v1/parties/{partyId}/lock", "{}");

        public Task<string> DisbandPartyAsync(string partyId) =>
            PostAsync($"/v1/parties/{partyId}/disband", "{}");

        // ── Multiplayer (N-player FFA) ────────────────────────────

        /// <summary>Commit into the FFA lobby queue. Salt generated internally — keep the result for the reveal.</summary>
        public async Task<AmpMultiCommit> MultiCommitAsync(string gameId, long stakeWei, int lobbySize)
        {
            if (string.IsNullOrEmpty(_wallet))
                throw new AmpException("not_authenticated", "Login before MultiCommit");

            var salt = AmpCrypto.GenerateSalt();
            var commitHash = AmpCrypto.ComputeCommitHash(_wallet, stakeWei, salt);

            var json = await PostAsync("/v1/multi/commit", AmpJson.Build(
                new Dictionary<string, string> { ["gameId"] = gameId, ["commitHash"] = commitHash },
                new Dictionary<string, long> { ["stakeWei"] = stakeWei, ["lobbySize"] = lobbySize }));

            return new AmpMultiCommit
            {
                Committed = AmpJson.GetBool(json, "committed"),
                CommittedCount = (int)AmpJson.GetInt(json, "committedCount"),
                Ready = AmpJson.GetBool(json, "ready"),
                Salt = salt,
            };
        }

        public Task<string> MultiRevealAsync(string gameId, string rulesetId, string salt) =>
            PostAsync("/v1/multi/reveal", AmpJson.Build(
                new Dictionary<string, string>
                {
                    ["gameId"] = gameId,
                    ["rulesetId"] = rulesetId,
                    ["salt"] = salt,
                }));

        public Task<string> GetMultiMatchAsync(string matchId) => GetAsync("/v1/multi/" + matchId);

        /// <summary>Submit the final ladder (best-first) — auto-signs EIP-712 (gasless).</summary>
        public async Task<string> MultiReportAsync(
            string matchId, IReadOnlyList<string> rankedWallets,
            string transcriptHash, long sessionNonce)
        {
            var signature = _signer != null
                ? await _signer.SignLadderDigest(AmpCrypto.ComputeLadderDigest(
                      ChainId, ContractAddress, matchId, rankedWallets, transcriptHash, sessionNonce))
                : null;

            var sb = new StringBuilder("{\"ranked\":[");
            for (var i = 0; i < rankedWallets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("[\"").Append(rankedWallets[i]).Append("\",").Append(i + 1).Append(']');
            }
            sb.Append("],\"transcriptHash\":\"").Append(transcriptHash)
              .Append("\",\"sessionNonce\":").Append(sessionNonce);
            if (signature != null)
                sb.Append(",\"signature\":\"").Append(signature).Append('"');
            sb.Append('}');

            return await PostAsync($"/v1/multi/{matchId}/report", sb.ToString());
        }

        public Task<string> MultiClaimAsync(string matchId) =>
            PostAsync($"/v1/multi/{matchId}/claim", "{}");

        /// <summary>Submit a death cert on elimination — auto-signs EIP-191.</summary>
        public async Task<string> SubmitExitCertAsync(
            string matchId, int rank, long exitFrame, string stateHash)
        {
            var signature = _signer != null
                ? await _signer.SignPersonalSign(
                      AmpCrypto.BuildExitCertMessage(matchId, rank, exitFrame, stateHash))
                : null;

            return await PostAsync($"/v1/multi/{matchId}/exit", AmpJson.Build(
                new Dictionary<string, string> { ["stateHash"] = stateHash, ["signature"] = signature },
                new Dictionary<string, long> { ["rank"] = rank, ["exitFrame"] = exitFrame }));
        }

        /// <summary>As a survivor, verify an eliminated player's state hash.</summary>
        public Task<string> CountersignExitCertAsync(string matchId, string wallet, string stateHash) =>
            PostAsync($"/v1/multi/{matchId}/exit/{wallet}",
                AmpJson.Build(new Dictionary<string, string> { ["stateHash"] = stateHash }));

        // ── WebSocket events ──────────────────────────────────────

        /// <summary>
        /// Connect the live event stream. onEvent receives the raw JSON
        /// ({"type":"…","data":{…}}) on a background thread — marshal to the
        /// main thread via AmpManager. Desktop & mobile only (WebGL needs a
        /// JS bridge, on the roadmap).
        /// </summary>
        public async Task<AmpEventStream> ConnectEventsAsync()
        {
            if (string.IsNullOrEmpty(_token))
                throw new AmpException("not_authenticated", "Login before ConnectEvents");
            return await AmpEventStream.ConnectAsync(_server, _token);
        }

        // ── internals ─────────────────────────────────────────────

        private async Task<string> GetAsync(string path) =>
            await SendAsync(HttpMethod.Get, path, null);

        private async Task<string> PostAsync(string path, string body) =>
            await SendAsync(HttpMethod.Post, path, body ?? "{}");

        private async Task<string> SendAsync(HttpMethod method, string path, string body)
        {
            using (var request = new HttpRequestMessage(method, _server + path))
            {
                if (!string.IsNullOrEmpty(_token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                if (body != null)
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using (var response = await _http.SendAsync(request))
                {
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        var code = AmpJson.GetString(text, "error") ?? AmpJson.GetString(text, "code") ?? "http_error";
                        var message = AmpJson.GetString(text, "message") ?? $"HTTP {(int)response.StatusCode}";
                        throw new AmpException(code, message) { HttpStatus = (int)response.StatusCode };
                    }
                    return text;
                }
            }
        }


    }

    /// <summary>AMP error with machine-readable code.</summary>
    public class AmpException : Exception
    {
        public string Code;
        public int HttpStatus;

        public AmpException(string code, string message) : base(message) => Code = code;
    }
}
