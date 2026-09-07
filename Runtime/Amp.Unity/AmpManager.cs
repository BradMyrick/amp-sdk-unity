// AMP Unity SDK — the one MonoBehaviour you need.
//
// Drag AmpManager into your scene, set the server URL (defaults to the
// AMP production matchmaker), wire the UnityEvents in the Inspector, and
// call the Amp* methods from your code or UI buttons. Every event fires
// on the MAIN THREAD.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amp.Sdk;
using UnityEngine;

namespace Amp.Unity
{
    /// <summary>
    /// AMP client host + main-thread event pump. One per scene is enough.
    /// </summary>
    public class AmpManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Matchmaker base URL. Defaults to the AMP production server.")]
        public string ServerUrl = AmpClient.DefaultServer;

        [Tooltip("Chain id + contract for EIP-712 ladder signatures.")]
        public long ChainId = AmpCrypto.DefaultChainId;
        public string ContractAddress = AmpCrypto.DefaultContract;

        [Header("Events (main thread)")]
        public AmpPlayerEvent OnLogin = new AmpPlayerEvent();
        public AmpErrorEvent OnError = new AmpErrorEvent();
        public AmpMatchFoundEvent OnMatchFound = new AmpMatchFoundEvent();
        public AmpStringEvent OnMatchResult = new AmpStringEvent();
        public AmpQueueStatusEvent OnQueueStatus = new AmpQueueStatusEvent();
        public AmpMultiLobbyEvent OnMultiLobbyFormed = new AmpMultiLobbyEvent();
        public AmpStringEvent OnMultiResult = new AmpStringEvent();
        public AmpStringEvent OnMultiCancelled = new AmpStringEvent();

        /// <summary>The live client (null until Login succeeds).</summary>
        public AmpClient Client { get; private set; }

        private IAmpSigner _signer;
        private AmpEventStream _stream;
        private readonly ConcurrentQueue<Action> _mainThreadPump = new ConcurrentQueue<Action>();

        private static AmpManager _instance;

        /// <summary>Scene singleton convenience.</summary>
        public static AmpManager Instance =>
            _instance != null ? _instance : (_instance = FindObjectOfType<AmpManager>());

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            while (_mainThreadPump.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void OnDestroy()
        {
            _stream?.Dispose();
            _stream = null;
        }

        // ── Setup ─────────────────────────────────────────────────

        /// <summary>Dev / dedicated-server path: sign with a raw private key. Never ship player keys in clients.</summary>
        public void SetDevPrivateKey(string privateKeyHex)
        {
            _signer = new AmpPrivateKeySigner(privateKeyHex);
            Debug.LogWarning($"[AMP] Dev private key signer active for {_signer.GetAddress()} — do NOT ship player keys in clients.");
        }

        /// <summary>Production: bridge your wallet plugin (or custodial backend) by implementing IAmpSigner.</summary>
        public void SetCustomSigner(IAmpSigner signer) => _signer = signer;

        // ── The flow ──────────────────────────────────────────────

        /// <summary>Gasless login. One invisible EIP-191 signature; connects the event stream on success.</summary>
        public async Task LoginAsync()
        {
            if (_signer == null)
            {
                FireError("no_signer", "Call SetDevPrivateKey (dev) or SetCustomSigner (production) first");
                return;
            }
            try
            {
                Client = new AmpClient(ServerUrl, _signer)
                {
                    ChainId = ChainId,
                    ContractAddress = ContractAddress,
                };
                var player = await Client.LoginAsync();
                _ = ListenForEvents();
                _mainThreadPump.Enqueue(() => OnLogin.Invoke(player));
            }
            catch (AmpException e)
            {
                FireError(e.Code, e.Message);
            }
            catch (Exception e)
            {
                FireError("network", e.Message);
            }
        }

        /// <summary>Join a ranked queue. OnMatchFound fires when an opponent lands.</summary>
        public async Task JoinQueueAsync(string gameId, string rulesetId)
        {
            await Wrap(() => Client.JoinQueueAsync(gameId, rulesetId));
        }

        /// <summary>One call: queue → wait → OnMatchFound (REST polling).</summary>
        public async Task WaitForMatchAsync(double timeoutSeconds = 30)
        {
            if (Client == null) { FireError("not_authenticated", "Login first"); return; }
            try
            {
                var match = await Client.WaitForMatchAsync(timeoutSeconds);
                _mainThreadPump.Enqueue(() => OnMatchFound.Invoke(match));
            }
            catch (AmpException e)
            {
                FireError(e.Code, e.Message);
            }
        }

        public async Task PlayBotAsync() => await Wrap(async () =>
        {
            var matchId = await Client.PlayBotAsync();
            _mainThreadPump.Enqueue(() => OnMatchFound.Invoke(new AmpMatchFound { MatchId = matchId, Bot = true }));
        });

        /// <summary>Report your 1v1 result — auto-signs EIP-191 (gasless).</summary>
        public Task ReportMatchAsync(string matchId, AmpMatchResult result) =>
            Wrap(() => Client.ReportMatchAsync(matchId, result));

        public Task LeaveQueueAsync() => Wrap(() => Client.LeaveQueueAsync());

        public async Task GetGamesAsync()
        {
            if (Client == null) { FireError("not_authenticated", "Login first"); return; }
            try
            {
                var games = await Client.GetGamesAsync();
                _mainThreadPump.Enqueue(() =>
                {
                    var evt = new AmpGamesEvent();
                    evt.Invoke(games);
                });
            }
            catch (Exception e) { FireFromException(e); }
        }

        public Task CreatePartyAsync(string gameId, string rulesetId) =>
            Wrap(() => Client.CreatePartyAsync(gameId, rulesetId));

        public Task JoinPartyAsync(string inviteCode) => Wrap(() => Client.JoinPartyAsync(inviteCode));

        public Task MultiCommitAsync(string gameId, long stakeWei, int lobbySize) =>
            Wrap(() => Client.MultiCommitAsync(gameId, stakeWei, lobbySize));

        public Task MultiRevealAsync(string gameId, string rulesetId, string salt) =>
            Wrap(() => Client.MultiRevealAsync(gameId, rulesetId, salt));

        public Task SubmitExitCertAsync(string matchId, int rank, long exitFrame, string stateHash) =>
            Wrap(() => Client.SubmitExitCertAsync(matchId, rank, exitFrame, stateHash));

        public Task MultiReportAsync(string matchId, string[] rankedWallets, string transcriptHash, long sessionNonce) =>
            Wrap(() => Client.MultiReportAsync(matchId, rankedWallets, transcriptHash, sessionNonce));

        public Task MultiClaimAsync(string matchId) => Wrap(() => Client.MultiClaimAsync(matchId));

        // ── internals ─────────────────────────────────────────────

        private async Task Wrap(Func<Task> call)
        {
            if (Client == null) { FireError("not_authenticated", "Login first"); return; }
            try
            {
                await call();
            }
            catch (AmpException e)
            {
                FireError(e.Code, e.Message);
            }
            catch (Exception e)
            {
                FireFromException(e);
            }
        }

        private async Task ListenForEvents()
        {
            try
            {
                _stream = await Client.ConnectEventsAsync();
                await _stream.ListenAsync(json =>
                {
                    DispatchEvent(json);
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AMP] event stream ended: {e.Message}");
                _mainThreadPump.Enqueue(() => OnError.Invoke(new AmpError
                {
                    Code = "ws_closed",
                    Message = e.Message,
                }));
            }
        }

        private void DispatchEvent(string json)
        {
            _mainThreadPump.Enqueue(() =>
            {
                if (json.Contains("\"match_found\"") && TryParseMatchFound(json, out var m))
                {
                    OnMatchFound.Invoke(m);
                }
                else if (json.Contains("\"queue_status\""))
                {
                    OnQueueStatus.Invoke(new AmpQueueStatus
                    {
                        Queued = AmpJson.GetBool(json, "queued"),
                        Depth = (int)AmpJson.GetInt(json, "depth"),
                        WaitedMs = AmpJson.GetInt(json, "waitedMs"),
                        SkillWindow = AmpJson.GetDouble(json, "skillWindow"),
                    });
                }
                else if (json.Contains("\"multi_lobby_formed\""))
                {
                    OnMultiLobbyFormed.Invoke(new AmpMultiLobbyFormed
                    {
                        MatchId = AmpJson.GetString(json, "matchId"),
                        LobbySize = (int)AmpJson.GetInt(json, "lobbySize"),
                        StakeWei = AmpJson.GetInt(json, "stakeWei"),
                        SessionNonce = AmpJson.GetInt(json, "sessionNonce"),
                    });
                }
                else if (json.Contains("\"multi_result\""))
                {
                    OnMultiResult.Invoke(json);
                }
                else if (json.Contains("\"multi_cancelled\""))
                {
                    OnMultiCancelled.Invoke(json);
                }
                else if (json.Contains("\"match_result\""))
                {
                    OnMatchResult.Invoke(json);
                }
            });
        }

        private static bool TryParseMatchFound(string json, out AmpMatchFound match)
        {
            match = new AmpMatchFound
            {
                MatchId = AmpJson.GetString(json, "matchId"),
                GameId = AmpJson.GetString(json, "gameId"),
                RulesetId = AmpJson.GetString(json, "rulesetId"),
                Bot = AmpJson.GetBool(json, "bot"),
                OpponentWallet = AmpJson.GetString(json, "wallet"),
                OpponentRating = AmpJson.GetDouble(json, "rating"),
                YourRating = AmpJson.GetDouble(json, "yourRating"),
                ExpiresAt = AmpJson.GetString(json, "expiresAt"),
            };
            return !string.IsNullOrEmpty(match.MatchId);
        }

        private void FireError(string code, string message)
        {
            _mainThreadPump.Enqueue(() => OnError.Invoke(new AmpError
            {
                Code = code,
                Message = message,
            }));
        }

        private void FireFromException(Exception e)
        {
            FireError("network", e.Message);
        }
    }
}
