// AMP Unity SDK tests — cross-SDK golden vectors + signer verification.
// Live integration runs only when AMP_TEST_KEY is set.

using System;
using System.Collections.Generic;
using Amp.Sdk;
using Xunit;

public class CryptoVectors
{
    [Fact]
    public void Keccak_MatchesKnownVectors()
    {
        Assert.Equal(
            "0xc5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470",
            AmpCrypto.KeccakHex(Array.Empty<byte>()));
    }

    [Fact]
    public void CommitHash_MatchesCrossSdkGoldenVector()
    {
        // Identical in TS/C#/C++/Rust/Unity + amp-server.
        Assert.Equal(
            "0x2d5491f1ad0117eea0c302b3cfb07590fef2d3892349e017361afd1bb5e5be10",
            AmpCrypto.ComputeCommitHash(
                "0x95CC495dF579981d3Ffa4a8f77B93A17563E077a",
                1_000_000_000_000_000, "0xdeadbeef").ToLowerInvariant());
    }

    [Fact]
    public void LadderDigest_MatchesCrossSdkGoldenVector()
    {
        // ethers-verified; contract typehash (gameId is bytes32).
        var digest = AmpCrypto.ComputeLadderDigest(
            43113, "0xcabf7b626172fE55d54f03c346563671AbcC77f7",
            "0x" + new string('a', 64),
            new[] {
                "0x95CC495dF579981d3Ffa4a8f77B93A17563E077a",
                "0x79aDcEF0E2bdc030f5906aA80C6B50C3712c0064",
            },
            "0x" + new string('b', 64), 42);
        Assert.Equal(
            "0x7e3467e6d14daf2c2ba195a1147c550a480c30c867c202b7b02e385a8e48123f",
            AmpCrypto.BytesToHex(digest).ToLowerInvariant());
    }

    [Fact]
    public void ExitCertMessage_MatchesServerFormat()
    {
        Assert.Equal(
            "AMP exit certificate\n\nMatch: m-42\nRank: 3\nExit frame: 1200\nState hash: 0xabc\n\n" +
            "This signature is free. It certifies your elimination and unlocks your reporting bond.",
            AmpCrypto.BuildExitCertMessage("m-42", 3, 1200, "0xabc"));
    }

    [Fact]
    public void Salts_AreUniqueAndWellFormed()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var s = AmpCrypto.GenerateSalt();
            Assert.StartsWith("0x", s);
            Assert.Equal(66, s.Length);
            Assert.True(seen.Add(s));
        }
    }
}

public class SignerTests
{
    private const string CanonicalKey =
        "0x0000000000000000000000000000000000000000000000000000000000000001";

    [Fact]
    public void DerivesCanonicalAddress()
    {
        // privkey = 1 → 0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf
        var signer = new AmpPrivateKeySigner(CanonicalKey);
        Assert.Equal(
            "0x7e5f4552091a69125d5dfcb7b8c2659029395bdf",
            signer.GetAddress().ToLowerInvariant());
    }

    [Fact]
    public void RejectsBadKeys()
    {
        Assert.Throws<ArgumentException>(() => new AmpPrivateKeySigner("0x1234"));
    }

    [Fact]
    public void SignatureIsWellFormed()
    {
        var signer = new AmpPrivateKeySigner(CanonicalKey);
        var sig = signer.SignPersonalSign("hello").Result;
        Assert.StartsWith("0x", sig);
        Assert.Equal(132, sig.Length);
        var v = Convert.ToInt32(sig.Substring(130, 2), 16);
        Assert.True(v == 27 || v == 28, $"v was {v}");
    }

    [Fact]
    public void DumpsSignaturesForCastCrossVerification()
    {
        // The CI + dev flow verifies these with `cast wallet verify`:
        //   dotnet test --filter CastDump
        //   head -1  "$TMPDIR/unity-sigs.txt"                # the address
        //   tail -n+2 "$TMPDIR/unity-sigs.txt" | while read sig … cast wallet verify …
        // Uses the OS temp dir so it works on any runner.
        var key = Environment.GetEnvironmentVariable("AMP_TEST_KEY") ?? CanonicalKey;
        var signer = new AmpPrivateKeySigner(key);
        var lines = new List<string> { signer.GetAddress() };
        for (var i = 0; i < 10; i++)
            lines.Add(signer.SignPersonalSign($"stress-{i}").Result);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unity-sigs.txt");
        System.IO.File.WriteAllLines(path, lines);
        Assert.True(System.IO.File.Exists(path));
    }
}

public class LiveIntegration
{
    private static string TestKey()
    {
        var key = Environment.GetEnvironmentVariable("AMP_TEST_KEY");
        if (!string.IsNullOrEmpty(key)) return key;
        try
        {
            // Wallet index 7 — reserved for the Unity SDK's live tests.
            var json = System.IO.File.ReadAllText("/tmp/opencode/e2e-wallets/wallets.json");
            var entries = json.Split(new[] { "{" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var e in entries)
            {
                if (e.Contains("\"index\": 7"))
                {
                    var pos = e.IndexOf("\"key\"");
                    var colon = e.IndexOf(':', pos);
                    var q1 = e.IndexOf('"', colon + 1);
                    var q2 = e.IndexOf('"', q1 + 1);
                    return e.Substring(q1 + 1, q2 - q1 - 1);
                }
            }
            return null;
        }
        catch { return null; }
    }

    [Fact]
    public async void FullLiveFlow()
    {
        var key = TestKey();
        if (string.IsNullOrEmpty(key)) return; // skip silently

        var client = new AmpClient(AmpClient.DefaultServer, new AmpPrivateKeySigner(key));

        // Login — proves the BouncyCastle EIP-191 signature is accepted
        var player = await client.LoginAsync();
        Assert.Equal(new AmpPrivateKeySigner(key).GetAddress().ToLowerInvariant(),
            player.Wallet.ToLowerInvariant());
        Assert.True(client.Authenticated);

        // Public reads
        var games = await client.GetGamesAsync();
        Assert.True(games.Count > 0);

        // Queue lifecycle
        await client.JoinQueueAsync("amp-tactics", "ranked-1v1");
        var status = await client.QueueStatusAsync();
        await client.LeaveQueueAsync();

        // Bot match + signed report
        var matchId = await client.PlayBotAsync();
        Assert.False(string.IsNullOrEmpty(matchId));
        var report = await client.ReportMatchAsync(matchId, AmpMatchResult.Win);
        Assert.Contains("matchId", report);

        // Party lifecycle
        var party = await client.CreatePartyAsync("amp-tactics", "ranked-1v1");
        Assert.False(string.IsNullOrEmpty(party.PartyId));
        await client.DisbandPartyAsync(party.PartyId);

        // Multiplayer commit + reveal (commit-reveal hashes verified server-side)
        var commit = await client.MultiCommitAsync("amp-tactics", 0, 4);
        Assert.True(commit.Committed);
        var reveal = await client.MultiRevealAsync("amp-tactics", "ranked-1v1", commit.Salt);
        Assert.Contains("revealed", reveal);

        // WebSocket hello
        var stream = await client.ConnectEventsAsync();
        var gotHello = false;
        var listen = stream.ListenAsync(json =>
        {
            if (json.Contains("\"hello\"")) gotHello = true;
        });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!gotHello && DateTime.UtcNow < deadline) await System.Threading.Tasks.Task.Delay(200);
        stream.Dispose();
        Assert.True(gotHello);

        client.Logout();
        Assert.False(client.Authenticated);
    }
}
