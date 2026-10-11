/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Utilities;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// Raw ECDH secret agreement for .NET Framework, whose BCL has no
    /// <c>ECDiffieHellman.DeriveRawSecretAgreement</c>.
    /// </summary>
    /// <remarks>
    /// OPC UA Part 6 feeds the raw shared X coordinate into HKDF, but
    /// <see cref="ECDiffieHellman.DeriveKeyMaterial(ECDiffieHellmanPublicKey)"/>
    /// on .NET Framework always hashes it first. The keys stay platform (CNG)
    /// keys; only the agreement itself runs in the managed BouncyCastle
    /// implementation. The extension has the signature of the .NET 8 instance
    /// method, so callers compile unchanged on every target.
    /// </remarks>
    internal static class EcdhRawAgreement
    {
        /// <summary>
        /// Derives the raw ECDH shared secret: the X coordinate of
        /// <c>d * Q</c>, big-endian and padded to the field size, exactly as
        /// <c>ECDiffieHellman.DeriveRawSecretAgreement</c> returns it on .NET 8+.
        /// </summary>
        /// <param name="privateKey">The local key pair.</param>
        /// <param name="otherPartyPublicKey">The peer's public key.</param>
        /// <exception cref="ArgumentNullException">A key is <c>null</c>.</exception>
        /// <exception cref="CryptographicException">
        /// The curve is not supported, the keys are on different curves, a key
        /// is invalid, or the agreement is the point at infinity.
        /// </exception>
        public static byte[] DeriveRawSecretAgreement(
            this ECDiffieHellman privateKey,
            ECDiffieHellmanPublicKey otherPartyPublicKey)
        {
            if (privateKey == null)
            {
                throw new ArgumentNullException(nameof(privateKey));
            }
            if (otherPartyPublicKey == null)
            {
                throw new ArgumentNullException(nameof(otherPartyPublicKey));
            }

            ECParameters local = privateKey.ExportParameters(true);
            try
            {
                ECParameters remote = otherPartyPublicKey.ExportParameters();
                return DeriveRawSecretAgreement(local, remote);
            }
            finally
            {
                if (local.D != null)
                {
                    Array.Clear(local.D, 0, local.D.Length);
                }
            }
        }

        /// <summary>
        /// Derives the raw ECDH shared secret from exported key parameters.
        /// </summary>
        /// <param name="privateKey">The local parameters, with <see cref="ECParameters.D"/>.</param>
        /// <param name="otherPartyPublicKey">The peer's public parameters.</param>
        /// <exception cref="CryptographicException">
        /// The curve is not supported, the keys are on different curves, a key
        /// is invalid, or the agreement is the point at infinity.
        /// </exception>
        internal static byte[] DeriveRawSecretAgreement(
            ECParameters privateKey,
            ECParameters otherPartyPublicKey)
        {
            if (privateKey.D == null || privateKey.D.Length == 0)
            {
                throw new CryptographicException("The local ECDH key has no private part.");
            }

            DerObjectIdentifier curve = GetCurveOid(privateKey.Curve);
            if (!curve.Equals(GetCurveOid(otherPartyPublicKey.Curve)))
            {
                throw new CryptographicException("The ECDH keys are on different curves.");
            }

            X9ECParameters x9 = ECNamedCurveTable.GetByOid(curve) ??
                throw new CryptographicException(
                    "The curve " + curve.Id + " is not supported for ECDH.");
            var domain = new ECDomainParameters(x9);
            ECPublicKeyParameters publicKey = CreatePublicKey(domain, otherPartyPublicKey.Q);

            var d = new BigInteger(1, privateKey.D);
            if (d.SignValue <= 0 || d.CompareTo(domain.N) >= 0)
            {
                throw new CryptographicException("The local ECDH private key is out of range.");
            }

            var agreement = new ECDHBasicAgreement();
            agreement.Init(new ECPrivateKeyParameters(d, domain));
            BigInteger z;
            try
            {
                z = agreement.CalculateAgreement(publicKey);
            }
            catch (InvalidOperationException e)
            {
                // BouncyCastle reports an agreement at infinity this way.
                throw new CryptographicException("The ECDH agreement is invalid.", e);
            }

            return BigIntegers.AsUnsignedByteArray(agreement.GetFieldSize(), z);
        }

        private static ECPublicKeyParameters CreatePublicKey(
            ECDomainParameters domain,
            ECPoint q)
        {
            if (q.X == null || q.Y == null)
            {
                throw new CryptographicException("The ECDH public key has no point.");
            }

            try
            {
                Org.BouncyCastle.Math.EC.ECPoint point = domain.Curve.CreatePoint(
                    new BigInteger(1, q.X),
                    new BigInteger(1, q.Y));

                // The constructor rejects infinity and points off the curve.
                return new ECPublicKeyParameters(point, domain);
            }
            catch (ArgumentException e)
            {
                throw new CryptographicException("The ECDH public key is not a valid point on the curve.", e);
            }
        }

        private static DerObjectIdentifier GetCurveOid(ECCurve curve)
        {
            if (!curve.IsNamed)
            {
                throw new CryptographicException("Only named curves are supported for ECDH.");
            }

            // CNG exports named curves with a friendly name and no OID value.
            string? oid = curve.Oid.Value;
            if (string.IsNullOrEmpty(oid) &&
                curve.Oid.FriendlyName != null &&
                !s_curveOids.TryGetValue(curve.Oid.FriendlyName, out oid))
            {
                X9ECParameters? x9 = ECNamedCurveTable.GetByName(curve.Oid.FriendlyName);
                oid = x9 != null ? ECNamedCurveTable.GetOid(curve.Oid.FriendlyName)?.Id : null;
            }

            if (string.IsNullOrEmpty(oid))
            {
                throw new CryptographicException(
                    "The curve " + curve.Oid.FriendlyName + " is not supported for ECDH.");
            }

            return new DerObjectIdentifier(oid);
        }

        /// <summary>
        /// The Windows names of the curves OPC UA uses, which BouncyCastle
        /// does not know by those names.
        /// </summary>
        private static readonly Dictionary<string, string> s_curveOids =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["nistP256"] = "1.2.840.10045.3.1.7",
                ["ECDH_P256"] = "1.2.840.10045.3.1.7",
                ["ECDSA_P256"] = "1.2.840.10045.3.1.7",
                ["nistP384"] = "1.3.132.0.34",
                ["ECDH_P384"] = "1.3.132.0.34",
                ["ECDSA_P384"] = "1.3.132.0.34",
                ["nistP521"] = "1.3.132.0.35",
                ["ECDH_P521"] = "1.3.132.0.35",
                ["ECDSA_P521"] = "1.3.132.0.35",
                ["brainpoolP256r1"] = "1.3.36.3.3.2.8.1.1.7",
                ["brainpoolP384r1"] = "1.3.36.3.3.2.8.1.1.11"
            };
    }
}
#endif
