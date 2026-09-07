// AMP Unity SDK — signer interfaces.
//
// Two paths, matching every AMP SDK:
//   1. Self-custody: implement IAmpSigner and bridge to your wallet plugin.
//   2. Dev & dedicated servers: AmpPrivateKeySigner (BouncyCastle, RFC 6979).

using System;
using System.Threading.Tasks;

namespace Amp.Sdk
{
    /// <summary>Abstract signer — implement this to connect a real wallet.</summary>
    public interface IAmpSigner
    {
        /// <summary>Checksummed address (0x…) this signer controls.</summary>
        string GetAddress();

        /// <summary>Sign an EIP-191 personal message → 65-byte r‖s‖v hex, v ∈ {27,28}.</summary>
        Task<string> SignPersonalSign(string message);

        /// <summary>Sign an EIP-712 digest → 65-byte r‖s‖v hex.</summary>
        Task<string> SignLadderDigest(byte[] digest);
    }
}
