// AMP Unity SDK — private-key signer on BouncyCastle secp256k1.
//
// Deterministic RFC 6979 nonces, EIP-2 low-s normalization, recovery id
// found by public-key matching — the same algorithm as the C++/OpenSSL
// and Unreal signers, cross-verified against `cast wallet verify`.
//
// FOR DEVELOPMENT AND DEDICATED SERVERS ONLY. Never ship a player's
// private key in a client build.

using System;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Amp.Sdk
{
    public sealed class AmpPrivateKeySigner : IAmpSigner
    {
        private static readonly X9ECParameters Curve = SecNamedCurves.GetByName("secp256k1");
        private static readonly ECDomainParameters Domain =
            new ECDomainParameters(Curve.Curve, Curve.G, Curve.N, Curve.H);

        private readonly BigInteger _private;
        private readonly string _address;

        public AmpPrivateKeySigner(string privateKeyHex)
        {
            var hex = privateKeyHex ?? "";
            if (hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X'))
                hex = hex.Substring(2);
            if (hex.Length != 64)
                throw new ArgumentException("Private key must be 64 hex characters");

            _private = new BigInteger(hex, 16);
            if (_private.SignValue <= 0 || _private.CompareTo(Domain.N) >= 0)
                throw new ArgumentException("Private key out of secp256k1 range");

            _address = DeriveAddress();
        }

        public string GetAddress() => _address;

        public Task<string> SignPersonalSign(string message) =>
            Task.FromResult(SignDigest(AmpCrypto.Eip191Digest(message)));

        public Task<string> SignLadderDigest(byte[] digest) =>
            Task.FromResult(SignDigest(digest));

        // ── internals ─────────────────────────────────────────────

        private string SignDigest(byte[] digest)
        {
            if (digest == null || digest.Length != 32)
                throw new ArgumentException("Digest must be 32 bytes");

            // Deterministic RFC 6979 nonce
            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
            signer.Init(true, new ECPrivateKeyParameters(_private, Domain));
            var twoHalf = signer.GenerateSignature(digest);
            var r = twoHalf[0];
            var s = twoHalf[1];

            // EIP-2 low-s normalization
            var halfN = Domain.N.ShiftRight(1);
            if (s.CompareTo(halfN) > 0)
                s = Domain.N.Subtract(s);

            var recId = FindRecoveryId(digest, r, s);
            if (recId < 0)
                throw new InvalidOperationException("Failed to compute recovery id");

            var sig = new byte[65];
            CopyPadded(r, sig, 0);
            CopyPadded(s, sig, 32);
            sig[64] = (byte)(27 + recId);
            return AmpCrypto.BytesToHex(sig);
        }

        /// <summary>Try each recovery id; return the one matching our public key.</summary>
        private int FindRecoveryId(byte[] digest, BigInteger r, BigInteger s)
        {
            var e = new BigInteger(1, digest);
            for (var recId = 0; recId < 2; recId++)
            {
                var candidate = RecoverPublicKey(r, s, e, recId);
                if (candidate != null && candidate.Equals(PublicPoint()))
                    return recId;
            }
            return -1;
        }

        private static ECPoint RecoverPublicKey(BigInteger r, BigInteger s, BigInteger e, int recId)
        {
            var n = Domain.N;
            if (r.CompareTo(BigInteger.One) < 0 || r.CompareTo(n) >= 0) return null;

            // R = decompress(x = r, parity = recId & 1)
            var xBytes = r.ToByteArrayUnsigned();
            var compressed = new byte[xBytes.Length + 1];
            compressed[0] = (byte)(0x02 | (recId & 1));
            Array.Copy(xBytes, 0, compressed, 1, xBytes.Length);
            ECPoint R;
            try
            {
                R = Curve.Curve.DecodePoint(compressed).Normalize();
            }
            catch (Exception)
            {
                return null;
            }

            // Q = r^-1 (s·R − e·G)
            var rInv = r.ModInverse(n);
            var q = R.Multiply(s).Subtract(Domain.G.Multiply(e)).Multiply(rInv).Normalize();
            return q;
        }

        private ECPoint PublicPoint() => Domain.G.Multiply(_private).Normalize();

        private string DeriveAddress()
        {
            var q = PublicPoint().GetEncoded(false); // 0x04 ‖ X ‖ Y
            var body = new byte[q.Length - 1];
            Array.Copy(q, 1, body, 0, body.Length);
            var hash = AmpCrypto.Keccak(body);
            var addr = new byte[20];
            Array.Copy(hash, 12, addr, 0, 20);
            return AmpCrypto.BytesToHex(addr);
        }

        private static void CopyPadded(BigInteger value, byte[] target, int offset)
        {
            var bytes = value.ToByteArrayUnsigned();
            Array.Copy(bytes, 0, target, offset + (32 - bytes.Length), bytes.Length);
        }
    }
}
