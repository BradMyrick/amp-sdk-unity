// AMP Unity SDK — serializable UnityEvents for Inspector wiring.

using Amp.Sdk;
using UnityEngine.Events;

namespace Amp.Unity
{
    [System.Serializable]
    public class AmpPlayerEvent : UnityEvent<AmpPlayer> { }

    [System.Serializable]
    public class AmpGamesEvent : UnityEvent<System.Collections.Generic.List<AmpGameInfo>> { }

    [System.Serializable]
    public class AmpMatchFoundEvent : UnityEvent<AmpMatchFound> { }

    [System.Serializable]
    public class AmpQueueStatusEvent : UnityEvent<AmpQueueStatus> { }

    [System.Serializable]
    public class AmpPartyCreatedEvent : UnityEvent<AmpPartyCreated> { }

    [System.Serializable]
    public class AmpMultiCommitEvent : UnityEvent<AmpMultiCommit> { }

    [System.Serializable]
    public class AmpMultiLobbyEvent : UnityEvent<AmpMultiLobbyFormed> { }

    [System.Serializable]
    public class AmpStringEvent : UnityEvent<string> { }

    [System.Serializable]
    public class AmpErrorEvent : UnityEvent<AmpError> { }
}
