// AMP Unity SDK — Quick Start sample.
//
// The complete ranked loop in one MonoBehaviour:
//   Login → Join Queue → Wait For Match → [your game] → Report Match.
//
// Import via Package Manager → Samples → Import, attach to any GameObject.

using Amp.Sdk;
using Amp.Unity;
using UnityEngine;

public class AmpQuickStart : MonoBehaviour
{
    [Tooltip("Dev-only test key. Production players use a wallet or custodial signer.")]
    public string DevPrivateKey;

    private AmpManager amp;

    private void Start()
    {
        amp = AmpManager.Instance;

        // 1. Wire events (do this in the Inspector instead if you prefer)
        amp.OnLogin.AddListener(p => Debug.Log($"[AMP] logged in: {p.Wallet}"));
        amp.OnError.AddListener(e => Debug.LogError($"[AMP] error: {e}"));
        amp.OnMatchFound.AddListener(m =>
        {
            Debug.Log($"[AMP] match found: {m.MatchId} (bot={m.Bot})");
            // 2. Load your gameplay scene here. For the sample we just report:
            _ = amp.ReportMatchAsync(m.MatchId, AmpMatchResult.Win);
        });

        // 3. Login (one gasless signature) → queue → wait
        if (!string.IsNullOrEmpty(DevPrivateKey))
        {
            amp.SetDevPrivateKey(DevPrivateKey);
            Run();
        }
        else
        {
            Debug.LogWarning("[AMP] set DevPrivateKey or call SetCustomSigner first");
        }
    }

    private async void Run()
    {
        await amp.LoginAsync();
        await amp.JoinQueueAsync("amp-tactics", "ranked-1v1");
        await amp.WaitForMatchAsync(timeoutSeconds: 30);
        // OnMatchFound fires above when a match lands; fall back to a bot:
        //   await amp.PlayBotAsync();
    }
}
