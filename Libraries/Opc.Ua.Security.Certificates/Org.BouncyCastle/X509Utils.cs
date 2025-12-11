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

#if !NETSTANDARD2_1 && !NET5_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#if NET472_OR_GREATER
using System.Text.RegularExpressions;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X9;
#endif
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// Helpers to create certificates, CRLs and extensions.
    /// </summary>
    internal static class X509Utils
    {
        /// <summary>
        /// Create a Pfx blob with a private key by combining
        /// a bouncy castle X509Certificate and a private key.
        /// </summary>
        internal static byte[] CreatePfxWithPrivateKey(
            Org.BouncyCastle.X509.X509Certificate certificate,
            string friendlyName,
            AsymmetricKeyParameter privateKey,
            ReadOnlySpan<char> passcode,
            SecureRandom random)
        {
            // create pkcs12 store for cert and private key
            using var pfxData = new MemoryStream();
            var builder = new Pkcs12StoreBuilder();
            builder.SetUseDerEncoding(true);
            Pkcs12Store pkcsStore = builder.Build();
            var chain = new X509CertificateEntry[1];
            chain[0] = new X509CertificateEntry(certificate);
            if (string.IsNullOrEmpty(friendlyName))
            {
                friendlyName = GetCertificateCommonName(certificate);
            }
            pkcsStore.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(privateKey), chain);
            pkcsStore.Save(pfxData, passcode.ToArray(), random);
            return pfxData.ToArray();
        }

        /// <summary>
        /// Helper to get the Bouncy Castle hash algorithm name by .NET name .
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        internal static string GetRSAHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == HashAlgorithmName.SHA1)
            {
                return "SHA1WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA256)
            {
                return "SHA256WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA384)
            {
                return "SHA384WITHRSA";
            }
            else if (hashAlgorithmName == HashAlgorithmName.SHA512)
            {
                return "SHA512WITHRSA";
            }
            throw new CryptographicException(
                $"The hash algorithm {hashAlgorithmName} is not supported");
        }

        /// <summary>
        /// Get public key parameters from a X509Certificate2
        /// </summary>
        internal static RsaKeyParameters GetRsaPublicKeyParameter(X509Certificate2 certificate)
        {
            using RSA rsa = certificate.GetRSAPublicKey();
            return GetRsaPublicKeyParameter(rsa);
        }

        /// <summary>
        /// Get public key parameters from a RSA.
        /// </summary>
        internal static RsaKeyParameters GetRsaPublicKeyParameter(RSA rsa)
        {
            RSAParameters rsaParams = rsa.ExportParameters(false);
            return new RsaKeyParameters(
                false,
                new BigInteger(1, rsaParams.Modulus),
                new BigInteger(1, rsaParams.Exponent));
        }

        /// <summary>
        /// Get RSA private key parameters from a X509Certificate2.
        /// The private key must be exportable.
        /// </summary>
        internal static RsaPrivateCrtKeyParameters GetRsaPrivateKeyParameter(
            X509Certificate2 certificate)
        {
            // try to get signing/private key from certificate passed in
            using RSA rsa = certificate.GetRSAPrivateKey();
            if (rsa != null)
            {
                return GetRsaPrivateKeyParameter(rsa);
            }
            return null;
        }

        /// <summary>
        /// Get private key parameters from a RSA private key.
        /// The private key must be exportable.
        /// </summary>
        internal static RsaPrivateCrtKeyParameters GetRsaPrivateKeyParameter(RSA rsa)
        {
            RSAParameters rsaParams = rsa.ExportParameters(true);
            return new RsaPrivateCrtKeyParameters(
                new BigInteger(1, rsaParams.Modulus),
                new BigInteger(1, rsaParams.Exponent),
                new BigInteger(1, rsaParams.D),
                new BigInteger(1, rsaParams.P),
                new BigInteger(1, rsaParams.Q),
                new BigInteger(1, rsaParams.DP),
                new BigInteger(1, rsaParams.DQ),
                new BigInteger(1, rsaParams.InverseQ));
        }

#if NET472_OR_GREATER
        /// <summary>
        /// Get ECDsa private key parameters from a X509Certificate2.
        /// The private key must be exportable.
        /// </summary>
        internal static ECPrivateKeyParameters GetECDsaPrivateKeyParameter(
            X509Certificate2 certificate)
        {
            // try to get signing/private key from certificate passed in
            using ECDsa ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa != null)
            {
                return GetECDsaPrivateKeyParameter(ecdsa);
            }
            return null;
        }

        /// <summary>
        /// Get BouncyCastle format private key parameters from a System.Security.Cryptography.ECDsa.
        /// The private key must be exportable.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        internal static ECPrivateKeyParameters GetECDsaPrivateKeyParameter(ECDsa ec)
        {
            ECParameters ecParams = ec.ExportParameters(true);
            var d = new BigInteger(1, ecParams.D);

            X9ECParameters curve = GetX9ECParameters(ecParams);

            string friendlyName = ecParams.Curve.Oid.FriendlyName;
            if (!s_friendlyNameToOidMap.TryGetValue(friendlyName, out string oidValue))
            {
                throw new NotSupportedException($"Unknown friendly name: {friendlyName}");
            }

            var oid = new DerObjectIdentifier(oidValue);

            var namedDomainParameters = new ECNamedDomainParameters(
                oid,
                curve.Curve,
                curve.G,
                curve.N,
                curve.H,
                curve.GetSeed());

            return new ECPrivateKeyParameters(d, namedDomainParameters);
        }

        /// <summary>
        /// Identifies a named curve by the provided coefficients A and B from their first 4 bytes
        /// </summary>
        /// <returns>The successfully identified named System.Security.Cryptography.ECCurve curve
        /// or throws if no curve is identified</returns>
        /// <exception cref="ArgumentException"></exception>
        internal static ECCurve IdentifyEccCurveByCoefficients(byte[] a, byte[] b)
        {
            byte[] brainpoolP256AStart = [0x7D, 0x5A, 0x09, 0x75];
            byte[] brainpoolP256BStart = [0x26, 0xDC, 0x5C, 0x6C];
            byte[] brainpoolP384AStart = [0x7B, 0xC3, 0x82, 0xC6];
            byte[] brainpoolP384BStart = [0x04, 0xA8, 0xC7, 0xDD];
            byte[] nistP256AStart = [0xFF, 0xFF, 0xFF, 0xFF];
            byte[] nistP256BStart = [0x5A, 0xC6, 0x35, 0xD8];
            byte[] nistP384AStart = [0xFF, 0xFF, 0xFF, 0xFF];
            byte[] nistP384BStart = [0xB3, 0x31, 0x2F, 0xA7];

            if (a.Take(4).SequenceEqual(brainpoolP256AStart) &&
                b.Take(4).SequenceEqual(brainpoolP256BStart))
            {
                return ECCurve.NamedCurves.brainpoolP256r1;
            }
            else if (a.Take(4).SequenceEqual(brainpoolP384AStart) &&
                b.Take(4).SequenceEqual(brainpoolP384BStart))
            {
                return ECCurve.NamedCurves.brainpoolP384r1;
            }
            else if (a.Take(4).SequenceEqual(nistP256AStart) &&
                b.Take(4).SequenceEqual(nistP256BStart))
            {
                return ECCurve.NamedCurves.nistP256;
            }
            else if (a.Take(4).SequenceEqual(nistP384AStart) &&
                b.Take(4).SequenceEqual(nistP384BStart))
            {
                return ECCurve.NamedCurves.nistP384;
            }

            throw new ArgumentException("EccCurveByCoefficients cannot be identified");
        }

        private static readonly Dictionary<string, string> s_friendlyNameToOidMap = new(
            StringComparer.OrdinalIgnoreCase)
        {
            { "nistP256", "1.2.840.10045.3.1.7" },
            { "nistP384", "1.3.132.0.34" },
            { "brainpoolP256r1", "1.3.36.3.3.2.8.1.1.7" },
            { "brainpoolP384r1", "1.3.36.3.3.2.8.1.1.11" }
        };

        /// <summary>
        /// Return Bouncy Castle X9ECParameters value equivalent of System.Security.Cryptography.ECparameters
        /// </summary>
        /// <returns>X9ECParameters value equivalent of System.Security.Cryptography.ECparameters if found else null</returns>
        internal static X9ECParameters GetX9ECParameters(ECParameters ecParams)
        {
            if (!string.IsNullOrEmpty(ecParams.Curve.Oid.Value))
            {
                var oid = new DerObjectIdentifier(ecParams.Curve.Oid.Value);
                return ECNamedCurveTable.GetByOid(oid);
            }
            else if (!string.IsNullOrEmpty(ecParams.Curve.Oid.FriendlyName))
            {
                // nist curve names do not contain "nist" in the bouncy castle ECNamedCurveTable
                // for ex: the form is "P-256" while the microsoft is "nistP256"
                // brainpool bouncy castle curve names are identic to the microsoft ones
                string msFriendlyName = ecParams.Curve.Oid.FriendlyName;
                string bcFriendlyName = msFriendlyName;
                const string nistCurveName = "nist";
                if (msFriendlyName.StartsWith(nistCurveName, StringComparison.Ordinal))
                {
                    const string patternMatch = @"(.*?)(\d+)$"; // divide string in two capture groups (string & numeric)
                    bcFriendlyName = Regex.Replace(
                        msFriendlyName,
                        patternMatch,
                        m =>
                        {
                            string lastChar = m.Groups[1].Value.Length > 0
                                ? m.Groups[1].Value[^1].ToString()
                                : string.Empty;
                            string number = m.Groups[2].Value;
                            return lastChar + "-" + number;
                        });
                }
                return ECNamedCurveTable.GetByName(bcFriendlyName);
            }

            return null;
        }

        /// <summary>
        /// Get BouncyCastle format public key parameters from a System.Security.Cryptography.ECDsa
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static ECPublicKeyParameters GetECPublicKeyParameters(ECDsa ec)
        {
            ECParameters ecParams = ec.ExportParameters(false);

            X9ECParameters curve =
                GetX9ECParameters(ecParams)
                ?? throw new ArgumentException(
                    "Curve OID is not recognized ",
                    ecParams.Curve.Oid.ToString());

            Org.BouncyCastle.Math.EC.ECPoint q = curve.Curve.CreatePoint(
                new BigInteger(1, ecParams.Q.X),
                new BigInteger(1, ecParams.Q.Y));

            var domainParameters = new ECDomainParameters(
                curve.Curve,
                curve.G,
                curve.N,
                curve.H,
                curve.GetSeed());

            return new ECPublicKeyParameters(q, domainParameters);
        }
#endif

        /// <summary>
        /// Get the serial number from a certificate as BigInteger.
        /// </summary>
        internal static BigInteger GetSerialNumber(X509Certificate2 certificate)
        {
            byte[] serialNumber = certificate.GetSerialNumber();
            return new BigInteger(1, [.. ((IEnumerable<byte>)serialNumber).Reverse()]);
        }

        /// <summary>
        /// Read the Common Name from a certificate.
        /// </summary>
        internal static string GetCertificateCommonName(
            Org.BouncyCastle.X509.X509Certificate certificate)
        {
            IList<string> subjectDN = certificate.SubjectDN.GetValueList(X509Name.CN);
            if (subjectDN.Count > 0)
            {
                return subjectDN[0];
            }
            return string.Empty;
        }

        /// <summary>
        /// Create secure temporary passcode.
        /// </summary>
        /// <remarks>
        /// Caller is responsible for clearing the passcode.
        /// </remarks>
        internal static char[] GeneratePasscode()
        {
            const int kLength = 18;
            using var rng = RandomNumberGenerator.Create();
            byte[] tokenBuffer = new byte[kLength];
            rng.GetBytes(tokenBuffer);
            char[] charToken = new char[kLength * 3];
            int length = Convert.ToBase64CharArray(
                tokenBuffer,
                0,
                tokenBuffer.Length,
                charToken,
                0,
                Base64FormattingOptions.None);
            Array.Clear(tokenBuffer, 0, tokenBuffer.Length);
            char[] passcode = new char[length];
            charToken.AsSpan(0, length).CopyTo(passcode);
            Array.Clear(charToken, 0, charToken.Length);
            return passcode;
        }

        /// <summary>
        /// Returns a RSA object with an imported public key.
        /// </summary>
        internal static RSA SetRSAPublicKey(byte[] publicKey)
        {
            AsymmetricKeyParameter asymmetricKeyParameter = PublicKeyFactory.CreateKey(publicKey);
            var rsaKeyParameters = asymmetricKeyParameter as RsaKeyParameters;
            var parameters = new RSAParameters
            {
                Exponent = rsaKeyParameters.Exponent.ToByteArrayUnsigned(),
                Modulus = rsaKeyParameters.Modulus.ToByteArrayUnsigned()
            };
            var rsaPublicKey = RSA.Create();
            rsaPublicKey.ImportParameters(parameters);
            return rsaPublicKey;
        }

        /// <summary>
        /// Pads a byte array with leading zeros to reach the specifieed size
        /// If the input is allready the given size, it just returns it
        /// </summary>
        /// <param name="arrayToPad">Provided array to pad</param>
        /// <param name="desiredSize">The desired total length of byte array after padding</param>
        /// <exception cref="ArgumentException"></exception>
        internal static byte[] PadWithLeadingZeros(byte[] arrayToPad, int desiredSize)
        {
            if (arrayToPad.Length == desiredSize)
            {
                return arrayToPad;
            }

            int paddingLength = desiredSize - arrayToPad.Length;
            if (paddingLength < 0)
            {
                throw new ArgumentException(
                    $"Input byte array is larger than the desired size {desiredSize} bytes.");
            }

            byte[] paddedArray = new byte[desiredSize];

            // Right-align the arrayToPad into paddedArray
            Buffer.BlockCopy(arrayToPad, 0, paddedArray, paddingLength, arrayToPad.Length);

            return paddedArray;
        }
    }
}
#endif
