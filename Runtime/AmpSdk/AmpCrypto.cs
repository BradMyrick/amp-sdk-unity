// AMP Unity SDK — crypto helpers on BouncyCastle.
// Same byte-for-byte encodings as the TS/C#/C++/Rust SDKs, amp-server,
// and the AmpUnreal plugin — pinned by cross-SDK golden vectors.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace Amp.Sdk
{
    /// <summary>Pure crypto: keccak, commit hashes, EIP-191/EIP-712 digests, messages.</summary>
    public static class AmpCrypto
    {
        public const long DefaultChainId = 43113;
        public const string DefaultContract =
            "0xcabf7b626172fE55d54f03c346563671AbcC77f7";

        /// <summary>Keccak-256 as 0x-hex.</summary>
        public static string KeccakHex(byte[] input)
        {
            return "0x" + BitConverter.ToString(Keccak(input)).Replace("-", "").ToLowerInvariant();
        }

        public static byte[] Keccak(byte[] input)
        {
            var digest = new KeccakDigest(256);
            digest.BlockUpdate(input, 0, input.Length);
            var output = new byte[32];
            digest.DoFinal(output, 0);
            return output;
        }

        /// <summary>32 random bytes as 0x-hex (commit-reveal salt).</summary>
        public static string GenerateSalt()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return "0x" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>EIP-191 report message: AMP_REPORT:v1:{matchId}:{result}</summary>
        public static string BuildReportMessage(string matchId, string result) =>
            $"AMP_REPORT:v1:{matchId}:{result}";

        /// <summary>EIP-191 exit certificate (death cert) — must match amp-server exactly.</summary>
        public static string BuildExitCertMessage(string matchId, int rank, long exitFrame, string stateHash) =>
            "AMP exit certificate\n\n" +
            $"Match: {matchId}\n" +
            $"Rank: {rank}\n" +
            $"Exit frame: {exitFrame}\n" +
            $"State hash: {stateHash}\n\n" +
            "This signature is free. It certifies your elimination and unlocks your reporting bond.";

        /// <summary>EIP-191 personal-message digest (byte-length prefix).</summary>
        public static byte[] Eip191Digest(string message)
        {
            // The length prefix must be the UTF-8 BYTE count, not the
            // UTF-16 char count (em-dashes in challenges bit us once).
            var prefix = "\x19" + "Ethereum Signed Message:\n" +
                         Encoding.UTF8.GetByteCount(message) + message;
            return Keccak(Encoding.UTF8.GetBytes(prefix));
        }

        /// <summary>
        /// Commit-reveal hash: keccak256(addr20 ‖ stake_u64_be(8) ‖ salt-utf8).
        /// Golden vector (cross-SDK):
        ///   0x95CC…077a, stake 1e15, salt "0xdeadbeef"
        ///   → 0x2d5491f1ad0117eea0c302b3cfb07590fef2d3892349e017361afd1bb5e5be10
        /// </summary>
        public static string ComputeCommitHash(string wallet, long stakeWei, string salt)
        {
            var addr = HexToBytes(wallet);
            var input = new byte[addr.Length + 8 + Encoding.UTF8.GetByteCount(salt)];
            Array.Copy(addr, 0, input, 0, addr.Length);
            // stake as 8-byte big-endian
            var v = (ulong)stakeWei;
            for (var i = 0; i < 8; i++)
                input[addr.Length + 7 - i] = (byte)(v >> (i * 8));
            Encoding.UTF8.GetBytes(salt, 0, salt.Length, input, addr.Length + 8);
            return KeccakHex(input);
        }

        /// <summary>
        /// EIP-712 digest for the MultiplayerLadder report (contract typehash:
        /// gameId is bytes32). Golden digest (ethers-verified):
        ///   0x7e3467e6d14daf2c2ba195a1147c550a480c30c867c202b7b02e385a8e48123f
        /// </summary>
        public static byte[] ComputeLadderDigest(
            long chainId, string contractAddress, string matchId,
            IReadOnlyList<string> rankedWallets, string transcriptHash, long sessionNonce)
        {
            var domainSeparator = Keccak(Concat(
                Keccak(Encoding.UTF8.GetBytes(
                    "EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)")),
                Keccak(Encoding.UTF8.GetBytes("AMPMultiplayer")),
                Keccak(Encoding.UTF8.GetBytes("1")),
                Word32((ulong)chainId),
                PadLeft32(HexToBytes(contractAddress))));

            var typeHash = Keccak(Encoding.UTF8.GetBytes(
                "MultiplayerLadder(bytes32 matchId,bytes32 gameId,address[] rankedPlacements,bytes32 transcriptHash,uint256 sessionNonce)"));

            // gameId: bytes32 of value 1 (matches every AMP SDK's ladder report)
            var gameId = PadRight32(HexToBytes("0x" + new string('0', 63) + "1"));

            var concat = new byte[rankedWallets.Count * 32];
            for (var i = 0; i < rankedWallets.Count; i++)
            {
                var padded = PadLeft32(HexToBytes(rankedWallets[i]));
                Array.Copy(padded, 0, concat, i * 32, 32);
            }

            var structHash = Keccak(Concat(
                typeHash,
                PadRight32(HexToBytes(matchId)),
                gameId,
                Keccak(concat),
                PadRight32(HexToBytes(transcriptHash)),
                Word32((ulong)sessionNonce)));

            return Keccak(Concat(new byte[] { 0x19, 0x01 }, domainSeparator, structHash));
        }

        // ── primitives ─────────────────────────────────────────────

        public static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            if (hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X'))
                hex = hex.Substring(2);
            if (hex.Length % 2 == 1) hex = "0" + hex;
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        public static string BytesToHex(byte[] bytes) =>
            "0x" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        /// <summary>Right-aligned 32-byte big-endian word.</summary>
        public static byte[] Word32(ulong value)
        {
            var word = new byte[32];
            for (var i = 0; i < 8; i++)
                word[31 - i] = (byte)(value >> (i * 8));
            return word;
        }

        public static byte[] PadLeft32(byte[] input)
        {
            var output = new byte[32];
            if (input.Length <= 32)
                Array.Copy(input, 0, output, 32 - input.Length, input.Length);
            return output;
        }

        public static byte[] PadRight32(byte[] input)
        {
            var output = new byte[32];
            if (input.Length <= 32)
                Array.Copy(input, 0, output, 0, input.Length);
            return output;
        }

        public static byte[] Concat(params byte[][] chunks)
        {
            var total = 0;
            foreach (var c in chunks) total += c.Length;
            var result = new byte[total];
            var offset = 0;
            foreach (var c in chunks)
            {
                Array.Copy(c, 0, result, offset, c.Length);
                offset += c.Length;
            }
            return result;
        }
    }
}
