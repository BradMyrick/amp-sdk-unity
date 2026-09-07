# AMP SDK for Unity — ranked multiplayer with wallets, in 5 minutes

**The absolute easiest way to add ranked matchmaking, skill ratings, and real-money competition to a Unity game. No blockchain knowledge. No gas. No wallet UI to build.**

| What your team writes | What AMP handles |
|---|---|
| "Who won this match?" (one line) | Glicko-2 skill ratings, queue + skill windows, match assignment, result verification, anti-cheat commit-reveal, on-chain escrow + payouts, fiat-friendly custodial rails |

```
LoginAsync() ──> JoinQueueAsync() ──> WaitForMatchAsync() ──> [your game] ──> ReportMatchAsync(win)
   1 call           1 call               1 call                               1 call
```

One invisible gasless signature at login powers the entire skill-verified economy.

## Install (2 minutes)

**Via Package Manager (git URL):** Window → Package Manager → `+` → *Add package from git URL* → `https://github.com/BradMyrick/amp-sdk-unity.git`

**Or from disk:** clone the repo, then *Add package from disk* → `package.json`.

Requires Unity **2021.3+**. The only dependency (BouncyCastle) is vendored — nothing to install. Import the **QuickStart sample** from the package page in Package Manager.

## Your first ranked match (3 minutes)

1. Add an empty GameObject, add the **AmpManager** component.
2. In `AmpQuickStart` (or your own script):

```csharp
var amp = AmpManager.Instance;

amp.OnMatchFound.AddListener(match =>
{
    Debug.Log($"Matched vs {match.OpponentWallet} (rating {match.OpponentRating})");
    _ = amp.ReportMatchAsync(match.MatchId, AmpMatchResult.Win); // when your game ends
});
amp.OnError.AddListener(e => Debug.LogError($"AMP: {e}"));

amp.SetDevPrivateKey(testKey);       // dev/servers. Production: SetCustomSigner(walletPlugin)
await amp.LoginAsync();              // one gasless EIP-191 signature
await amp.JoinQueueAsync("amp-tactics", "ranked-1v1");
await amp.WaitForMatchAsync(30);     // fires OnMatchFound (or OnError: timeout)
```

All `AmpManager` events fire **on the main thread** — bind them straight to UI and gameplay. They're `UnityEvent`s, so you can also wire them entirely in the Inspector.

## API

`AmpClient` (pure C#, Task-based) mirrors every AMP SDK:

| Area | Methods |
|---|---|
| Auth | `LoginAsync()` (EIP-191, gasless) · `Logout()` |
| Games | `GetGamesAsync()` · `MeRawAsync()` · `GetPlayerAsync(wallet)` |
| Queue | `JoinQueueAsync` · `LeaveQueueAsync` · `QueueStatusAsync` · `PlayBotAsync` · `WaitForMatchAsync(timeout)` |
| 1v1 | `GetMatchAsync` · `MatchHistoryAsync` · `ReportMatchAsync(matchId, Win/Loss/Draw)` · `VerifyEscrowAsync` |
| Parties | `CreatePartyAsync` · `JoinPartyAsync(code)` · `LockPartyAsync` · `DisbandPartyAsync` |
| N-player FFA | `MultiCommitAsync` → keep the **Salt** → `MultiRevealAsync` · `SubmitExitCertAsync` (death certs) · `CountersignExitCertAsync` · `MultiReportAsync` (EIP-712) · `MultiClaimAsync` |
| Live events | `ConnectEventsAsync()` → `ListenAsync(json => …)` |

Every signing path (login, reports, ladders, death certs) is automatic and gasless.

## Auth models

1. **Dev & dedicated servers** — `SetDevPrivateKey(key)` / `new AmpPrivateKeySigner(key)`. BouncyCastle secp256k1, RFC 6979 deterministic nonces. **Never ship player keys in clients** (it logs a warning for a reason).
2. **Player wallets** — implement `IAmpSigner` (3 methods) and bridge to your wallet plugin.
3. **Custodial (fiat players)** — same interface; your backend signs. AMP never touches fiat.

## Platform notes

- Desktop + mobile: HTTP + WebSocket fully supported.
- **WebGL**: `HttpClient`/`ClientWebSocket` need a JS bridge (roadmap). The REST client works via UnityWebRequest if you shim it; the event stream is the gap today.
- IL2CPP/AOT safe: no reflection-based JSON — a minimal hand-rolled serializer keeps the package dependency-light and trimming-proof.

## Crypto conformance

Byte-for-byte identical encodings with the TS/C#/C++/Rust SDKs, amp-server, and the AmpUnreal plugin — locked by cross-SDK golden vectors (`0x2d5491f1…` commit hash, `0x7e3467e6…` EIP-712 digest), both pinned in `Tests~`. Signatures cross-verified with `cast wallet verify`.

One battle scar: the EIP-191 length prefix must be the **UTF-8 byte count**, not the C# char count — the login challenge contains em-dashes, and the first live run failed on exactly that. It's fixed and pinned by the live test.

## Tests

```sh
dotnet build Tests~/compilecheck        # netstandard2.1 (Unity API level)
dotnet test Tests~/AmpUnityTests        # unit + vectors; live runs when AMP_TEST_KEY is set
```

## Unreal too?

The same protocol ships as the **AmpUnreal** plugin (Blueprint nodes) in [amp-sdk-cpp](https://github.com/BradMyrick/amp-sdk-cpp), and raw SDKs exist for [TypeScript](https://github.com/BradMyrick/amp-sdk-ts), [C#](https://github.com/BradMyrick/amp-sdk-csharp), and [Rust](https://github.com/BradMyrick/amp-sdk-rust).

## License

Apache-2.0 (BouncyCastle: MIT-equivalent, vendored as a single netstandard2.0 DLL).
