// AMP Unity SDK — Blueprint-equivalent types (plain C#, netstandard2.1-safe).

using System;
using System.Collections.Generic;

namespace Amp.Sdk
{
    /// <summary>Result of a 1v1 match report.</summary>
    public enum AmpMatchResult
    {
        Win,
        Loss,
        Draw
    }

    /// <summary>A player identity.</summary>
    [Serializable]
    public class AmpPlayer
    {
        public string Wallet;
        public string Region;
        public string Language;
    }

    /// <summary>A queue ruleset.</summary>
    [Serializable]
    public class AmpRuleset
    {
        public string Id;
        public string Name;
        public int QueueDepth;
    }

    /// <summary>A registered game with its rulesets.</summary>
    [Serializable]
    public class AmpGameInfo
    {
        public string Id;
        public string Name;
        public List<AmpRuleset> Rulesets = new List<AmpRuleset>();
    }

    /// <summary>Live queue status while waiting.</summary>
    [Serializable]
    public class AmpQueueStatus
    {
        public bool Queued;
        public int Depth;
        public long WaitedMs;
        public double SkillWindow;
    }

    /// <summary>Pushed when the matchmaker assigns you an opponent.</summary>
    [Serializable]
    public class AmpMatchFound
    {
        public string MatchId;
        public string GameId;
        public string RulesetId;
        public bool Bot;
        public string OpponentWallet;
        public double OpponentRating;
        public double YourRating;
        public string ExpiresAt;
    }

    /// <summary>Result payload after a settled match.</summary>
    [Serializable]
    public class AmpMatchResultInfo
    {
        public string MatchId;
        public string Outcome;
        public bool Won;
        public double RatingBefore;
        public double RatingAfter;
    }

    /// <summary>Result of MultiCommit — keep the Salt for the reveal phase.</summary>
    [Serializable]
    public class AmpMultiCommit
    {
        public bool Committed;
        public int CommittedCount;
        public bool Ready;
        /// <summary>Keep this — MultiReveal needs it.</summary>
        public string Salt;
    }

    /// <summary>Party creation result.</summary>
    [Serializable]
    public class AmpPartyCreated
    {
        public string PartyId;
        public string InviteCode;
        public string Leader;
    }

    /// <summary>A multiplayer lobby assignment (N-player).</summary>
    [Serializable]
    public class AmpMultiLobbyFormed
    {
        public string MatchId;
        public int LobbySize;
        public long StakeWei;
        public long SessionNonce;
    }

    /// <summary>Error detail for failure callbacks.</summary>
    [Serializable]
    public class AmpError
    {
        public string Code = "unknown";
        public string Message = "";
        public int HttpStatus;

        public override string ToString() =>
            string.IsNullOrEmpty(Message) ? Code : $"{Code}: {Message}";
    }
}
