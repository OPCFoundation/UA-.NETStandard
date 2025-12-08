/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
#if CURVE25519
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Digests;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Represents a cryptographic nonce used for secure communication.
    /// </summary>
    public class Nonce : IDisposable
    {
        private ECDiffieHellman m_ecdh;
        private RSADiffieHellman m_rsadh;
        private static readonly RandomNumberGenerator s_rng = RandomNumberGenerator.Create();
        private static uint s_minNonceLength = 32;

        /// <summary>
        /// Constructor
        /// </summary>
        private Nonce()
        {
            m_ecdh = null;
            m_rsadh = null;
        }

        /// <summary>
        /// Gets the nonce data.
        /// </summary>
        public byte[] Data { get; private set; }

        internal byte[] GenerateSecret(
            Nonce remoteNonce,
            byte[] previousSecret)
        {
            byte[] ikm = null;
            CryptoTrace.Start(ConsoleColor.Cyan, $"GenerateSecret");

#if NET8_0_OR_GREATER
#if xDEBUG
            Span<char> privateKey = stackalloc char[2048];

            if (m_ecdh.TryExportECPrivateKeyPem(privateKey, out int charsWritten))
            {
                CryptoTrace.WriteLine($"Private Key PEM ({charsWritten} chars):");
            }
#endif

            if (m_ecdh != null)
            {
                ikm = m_ecdh.DeriveRawSecretAgreement(remoteNonce.m_ecdh.PublicKey);

            }
            else if (m_rsadh != null)
            {
                ikm = m_rsadh.DeriveRawSecretAgreement(remoteNonce.m_rsadh);
            }

            CryptoTrace.WriteLine($"IKM-Raw={CryptoTrace.KeyToString(ikm)}");
            CryptoTrace.WriteLine($"Previous-IKM={CryptoTrace.KeyToString(previousSecret)}");

            if (previousSecret != null)
            {
                for (int ii = 0; ii < ikm.Length && ii < previousSecret.Length; ii++)
                {
                    ikm[ii] ^= previousSecret[ii];
                }
            }

            CryptoTrace.WriteLine($"IKM-XOR={CryptoTrace.KeyToString(ikm)}");
            CryptoTrace.Finish("GenerateSecret");

#endif
            return ikm;
        }

        /// <summary>
        /// Derives a key from the remote nonce, using the specified salt, hash algorithm, and length.
        /// </summary>
        /// <param name="secret">The secret to use in key derivation.</param>
        /// <param name="salt">The salt to use in key derivation.</param>
        /// <param name="algorithm">The hash algorithm to use in key derivation.</param>
        /// <param name="length">The length of the derived key.</param>
        /// <returns>The derived key.</returns>
        public byte[] DeriveKeyData(
            byte[] secret,
            byte[] salt,
            KeyDerivationAlgorithm algorithm,
            int length)
        {
            CryptoTrace.Start(ConsoleColor.DarkCyan, $"DeriveKeyData");
            CryptoTrace.WriteLine($"Secret={CryptoTrace.KeyToString(secret)}");
            CryptoTrace.WriteLine($"Salt={CryptoTrace.KeyToString(salt)}");

            using HMAC extract = algorithm switch
            {
                KeyDerivationAlgorithm.HKDFSha256 => new HMACSHA256(salt),
                KeyDerivationAlgorithm.HKDFSha384 => new HMACSHA384(salt),
                _ => new HMACSHA256(salt)
            };

            byte[] prk = extract.ComputeHash(secret);
            CryptoTrace.WriteLine($"PRK={CryptoTrace.KeyToString(prk)}");

            using HMAC expand = algorithm switch
            {
                KeyDerivationAlgorithm.HKDFSha256 => new HMACSHA256(prk),
                KeyDerivationAlgorithm.HKDFSha384 => new HMACSHA384(prk),
                _ => new HMACSHA256(prk)
            };

            byte[] output = new byte[length];
            byte counter = 1;

            byte[] info = new byte[(expand.HashSize / 8) + salt.Length + 1];
            Buffer.BlockCopy(salt, 0, info, 0, salt.Length);
            info[salt.Length] = counter++;

            // computer T(1)
            byte[] hash = expand.ComputeHash(info, 0, salt.Length + 1);
            CryptoTrace.WriteLine($"T(1)={CryptoTrace.KeyToString(hash)}");

            int pos = 0;

            for (int ii = 0; ii < hash.Length && pos < length; ii++)
            {
                output[pos++] = hash[ii];
            }

            while (pos < length)
            {
                Buffer.BlockCopy(hash, 0, info, 0, hash.Length);
                Buffer.BlockCopy(salt, 0, info, hash.Length, salt.Length);
                info[^1] = counter++;

                hash = expand.ComputeHash(info, 0, info.Length);
                CryptoTrace.WriteLine($"T({counter - 1})={CryptoTrace.KeyToString(hash)}");

                for (int ii = 0; ii < hash.Length && pos < length; ii++)
                {
                    output[pos++] = hash[ii];
                }
            }

            CryptoTrace.WriteLine($"KeyData={CryptoTrace.KeyToString(output)}");
            CryptoTrace.Finish("DeriveKeyData");

            return output;
        }

        /// <summary>
        /// Creates a nonce for the specified security policy URI and nonce length.
        /// </summary>
        public static Nonce CreateNonce(string securityPolicyUri)
        {
            var info = SecurityPolicies.GetInfo(securityPolicyUri);
            return CreateNonce(info);
        }

        /// <summary>
        /// Creates a nonce for the specified security policy and nonce length.
        /// </summary>
        public static Nonce CreateNonce(SecurityPolicyInfo securityPolicy)
        {
            if (securityPolicy == null)
            {
                throw new ArgumentNullException(nameof(securityPolicy));
            }

            switch (securityPolicy.EphemeralKeyAlgorithm)
            {
                case CertificateKeyAlgorithm.RSADH:
                    return securityPolicy.MinAsymmetricKeyLength switch
                    {
                        //2048 => CreateNonce(RSADiffieHellmanGroup.FFDHE2048),
                        //3072 => CreateNonce(RSADiffieHellmanGroup.FFDHE3072),
                        //4096 => CreateNonce(RSADiffieHellmanGroup.FFDHE4096),
                        _ => CreateNonce(RSADiffieHellmanGroup.FFDHE4096)
                    };
                case CertificateKeyAlgorithm.NistP256:
                    return CreateNonce(ECCurve.NamedCurves.nistP256);
                case CertificateKeyAlgorithm.NistP384:
                    return CreateNonce(ECCurve.NamedCurves.nistP384);
                case CertificateKeyAlgorithm.BrainpoolP256r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1);
                case CertificateKeyAlgorithm.BrainpoolP384r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1);
                default:
                    return new Nonce { Data = CreateRandomNonceData(securityPolicy.SecureChannelNonceLength) };
            }
        }

        /// <summary>
        /// Creates a new Nonce object for the specified RSA DiffieHellman group.
        /// </summary>
        public static Nonce CreateNonce(RSADiffieHellmanGroup group)
        {
            var nonce = new Nonce();
            nonce.m_rsadh = RSADiffieHellman.Create(group);
            nonce.Data = nonce.m_rsadh.GetNonce();
            return nonce;
        }

        /// <summary>
        /// Creates a new Nonce object for the specified security policy URI and nonce data.
        /// </summary>
        public static Nonce CreateNonce(SecurityPolicyInfo securityPolicy, byte[] nonceData)
        {
            if (securityPolicy == null)
            {
                throw new ArgumentNullException(nameof(securityPolicy));
            }

            if (nonceData == null)
            {
                throw new ArgumentNullException(nameof(nonceData));
            }

            if (securityPolicy.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH)
            {
                var nonce = new Nonce();
                nonce.m_rsadh = RSADiffieHellman.Create(nonceData);
                nonce.Data = nonceData;
                return nonce;
            }

            switch (securityPolicy.EphemeralKeyAlgorithm)
            {
                case CertificateKeyAlgorithm.NistP256:
                    return CreateNonce(ECCurve.NamedCurves.nistP256, nonceData);
                case CertificateKeyAlgorithm.NistP384:
                    return CreateNonce(ECCurve.NamedCurves.nistP384, nonceData);
                case CertificateKeyAlgorithm.BrainpoolP256r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1, nonceData);
                case CertificateKeyAlgorithm.BrainpoolP384r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1, nonceData);
                case CertificateKeyAlgorithm.Curve25519:
                    return CreateNonceForCurve25519(nonceData);
                case CertificateKeyAlgorithm.Curve448:
                    return CreateNonceForCurve448(nonceData);
                default:
                    return new Nonce { Data = nonceData };
            }
        }

        /// <summary>
        /// Generates a Nonce for cryptographic functions of a given length.
        /// </summary>
        /// <returns>The requested Nonce as a</returns>
        public static byte[] CreateRandomNonceData(int length)
        {
            byte[] randomBytes = new byte[length];
            s_rng.GetBytes(randomBytes);
            return randomBytes;
        }

        /// <summary>
        /// Validates the nonce for a message security mode and security policy.
        /// </summary>
        public static bool ValidateNonce(
            byte[] nonce,
            MessageSecurityMode securityMode,
            SecurityPolicyInfo securityPolicy)
        {
            return ValidateNonce(nonce, securityMode, securityPolicy.SecureChannelNonceLength);
        }

        /// <summary>
        /// Validates the nonce for a message security mode and a minimum length.
        /// </summary>
        public static bool ValidateNonce(
            byte[] nonce,
            MessageSecurityMode securityMode,
            int minNonceLength)
        {
            // no nonce needed for no security.
            if (securityMode == MessageSecurityMode.None)
            {
                return true;
            }

            // check the length.
            if (nonce == null || nonce.Length < minNonceLength)
            {
                return false;
            }

            // try to catch programming errors by rejecting nonces with all zeros.
            for (int ii = 0; ii < nonce.Length; ii++)
            {
                if (nonce[ii] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compare Nonce for equality.
        /// </summary>
        public static bool CompareNonce(byte[] a, byte[] b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            byte result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= (byte)(a[i] ^ b[i]);
            }

            return result == 0;
        }

        /// <summary>
        /// Sets the minimum nonce value to be used as default
        /// </summary>
        public static void SetMinNonceValue(uint nonceLength)
        {
            s_minNonceLength = nonceLength;
        }

        /// <summary>
        /// Creates a new Nonce object for use with Curve25519.
        /// </summary>
        /// <param name="nonceData">The nonce data to use.</param>
        /// <returns>A new Nonce object.</returns>
        private static Nonce CreateNonceForCurve25519(byte[] nonceData)
        {
            return new Nonce { Data = nonceData };
        }

        /// <summary>
        /// Creates a new Nonce instance for Curve448.
        /// </summary>
        /// <param name="nonceData">The nonce data.</param>
        /// <returns>A new Nonce instance.</returns>
        private static Nonce CreateNonceForCurve448(byte[] nonceData)
        {
            return new Nonce { Data = nonceData };
        }

        /// <summary>
        /// Creates a new Nonce instance with the specified ECC curve and nonce data.
        /// </summary>
        /// <param name="curve">The elliptic curve to use for the ECDH key exchange.</param>
        /// <param name="nonceData">The nonce data to use for the ECDH key exchange.</param>
        /// <returns>A new Nonce instance with the specified curve and nonce data.</returns>
        /// <exception cref="ArgumentException"></exception>
        private static Nonce CreateNonce(ECCurve curve, byte[] nonceData)
        {
            var nonce = new Nonce { Data = nonceData };

            int keyLength = nonceData.Length;

            byte[] qx = new byte[keyLength / 2];
            byte[] qy = new byte[keyLength / 2];
            Buffer.BlockCopy(nonceData, 0, qx, 0, keyLength / 2);
            Buffer.BlockCopy(nonceData, keyLength / 2, qy, 0, keyLength / 2);

            var ecdhParameters = new ECParameters { Curve = curve, Q = { X = qx, Y = qy } };
            //validate curve parameters as ECDiffieHellman.Create expects already validated curve parameters
            try
            {
                ecdhParameters.Validate();
                nonce.m_ecdh = ECDiffieHellman.Create(ecdhParameters);
            }
            catch (CryptographicException e)
            {
                throw new ArgumentException("Invalid nonce data provided", nameof(nonceData), e);
            }
            //On Windows a PlatformNotSupportedException is thrown when invalid parameters are provided
            catch (PlatformNotSupportedException e)
            {
                throw new ArgumentException("Invalid nonce data provided", nameof(nonceData), e);
            }

            return nonce;
        }

        /// <summary>
        /// Creates a new Nonce instance using the specified elliptic curve.
        /// </summary>
        /// <param name="curve">The elliptic curve to use for the ECDH key exchange.</param>
        /// <returns>A new Nonce instance.</returns>
        private static Nonce CreateNonce(ECCurve curve)
        {
            var ecdh = ECDiffieHellman.Create(curve);
            ECParameters ecdhParameters = ecdh.ExportParameters(false);
            int xLen = ecdhParameters.Q.X.Length;
            int yLen = ecdhParameters.Q.Y.Length;

            byte[] senderNonce = new byte[xLen + yLen];
            Array.Copy(ecdhParameters.Q.X, senderNonce, xLen);
            Array.Copy(ecdhParameters.Q.Y, 0, senderNonce, xLen, yLen);

            return new Nonce { Data = senderNonce, m_ecdh = ecdh };
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_ecdh != null)
                {
                    m_ecdh.Dispose();
                    m_ecdh = null;
                }
            }
        }
    }

    /// <summary>
    /// The known RSA Diffie-Hellman groups.
    /// </summary>
    public enum RSADiffieHellmanGroup
    {
        /// <summary>
        /// The 2048-bit finite field Diffie-Hellman ephemeral group defined in RFC 7919.
        /// </summary>
        FFDHE2048,

        /// <summary>
        /// The 3072-bit finite field Diffie-Hellman ephemeral group defined in RFC 7919.
        /// </summary>
        FFDHE3072,

        /// <summary>
        /// The 4096-bit finite field Diffie-Hellman ephemeral group defined in RFC 7919.
        /// </summary>
        FFDHE4096
    }

    /// <summary>
    /// A RSA Diffie-Hellman key exchange implementation.
    /// </summary>
    public class RSADiffieHellman
    {
        private BigInteger m_privateKey;
        private BigInteger m_publicKey;
        private int m_nonceLength;

        // ffdhe2048 prime from RFC 7919 (hex, without whitespace).  
        // (RFC 7919 Appendix A.3 — use this canonical modulus in production.)
        const string FFDHE2048_HEX = @"
            FFFFFFFF FFFFFFFF ADF85458 A2BB4A9A AFDC5620 273D3CF1
            D8B9C583 CE2D3695 A9E13641 146433FB CC939DCE 249B3EF9
            7D2FE363 630C75D8 F681B202 AEC4617A D3DF1ED5 D5FD6561
            2433F51F 5F066ED0 85636555 3DED1AF3 B557135E 7F57C935
            984F0C70 E0E68B77 E2A689DA F3EFE872 1DF158A1 36ADE735
            30ACCA4F 483A797A BC0AB182 B324FB61 D108A94B B2C8E3FB
            B96ADAB7 60D7F468 1D4F42A3 DE394DF4 AE56EDE7 6372BB19
            0B07A7C8 EE0A6D70 9E02FCE1 CDF7E2EC C03404CD 28342F61
            9172FE9C E98583FF 8E4F1232 EEF28183 C3FE3B1B 4C6FAD73
            3BB5FCBC 2EC22005 C58EF183 7D1683B2 C6F34A26 C1B2EFFA
            886B4238 61285C97 FFFFFFFF FFFFFFFF";

        static readonly Lazy<BigInteger> s_P2048 = new(() => RfcTextToBytes(FFDHE2048_HEX));

        const int k_FFDHE2048_MinExponent = 224;
        const int k_FFDHE2048_MaxExponent = 255;

        const string FFDHE3072_HEX = @"
            FFFFFFFF FFFFFFFF ADF85458 A2BB4A9A AFDC5620 273D3CF1
            D8B9C583 CE2D3695 A9E13641 146433FB CC939DCE 249B3EF9
            7D2FE363 630C75D8 F681B202 AEC4617A D3DF1ED5 D5FD6561
            2433F51F 5F066ED0 85636555 3DED1AF3 B557135E 7F57C935
            984F0C70 E0E68B77 E2A689DA F3EFE872 1DF158A1 36ADE735
            30ACCA4F 483A797A BC0AB182 B324FB61 D108A94B B2C8E3FB
            B96ADAB7 60D7F468 1D4F42A3 DE394DF4 AE56EDE7 6372BB19
            0B07A7C8 EE0A6D70 9E02FCE1 CDF7E2EC C03404CD 28342F61
            9172FE9C E98583FF 8E4F1232 EEF28183 C3FE3B1B 4C6FAD73
            3BB5FCBC 2EC22005 C58EF183 7D1683B2 C6F34A26 C1B2EFFA
            886B4238 611FCFDC DE355B3B 6519035B BC34F4DE F99C0238
            61B46FC9 D6E6C907 7AD91D26 91F7F7EE 598CB0FA C186D91C
            AEFE1309 85139270 B4130C93 BC437944 F4FD4452 E2D74DD3
            64F2E21E 71F54BFF 5CAE82AB 9C9DF69E E86D2BC5 22363A0D
            ABC52197 9B0DEADA 1DBF9A42 D5C4484E 0ABCD06B FA53DDEF
            3C1B20EE 3FD59D7C 25E41D2B 66C62E37 FFFFFFFF FFFFFFFF";

        static readonly Lazy<BigInteger> s_P3072 = new(() => RfcTextToBytes(FFDHE3072_HEX));

        const int k_FFDHE3072_MinExponent = 275;
        const int k_FFDHE3072_MaxExponent = 383;

        const string FFDHE4096_HEX = @"
            FFFFFFFF FFFFFFFF ADF85458 A2BB4A9A AFDC5620 273D3CF1
            D8B9C583 CE2D3695 A9E13641 146433FB CC939DCE 249B3EF9
            7D2FE363 630C75D8 F681B202 AEC4617A D3DF1ED5 D5FD6561
            2433F51F 5F066ED0 85636555 3DED1AF3 B557135E 7F57C935
            984F0C70 E0E68B77 E2A689DA F3EFE872 1DF158A1 36ADE735
            30ACCA4F 483A797A BC0AB182 B324FB61 D108A94B B2C8E3FB
            B96ADAB7 60D7F468 1D4F42A3 DE394DF4 AE56EDE7 6372BB19
            0B07A7C8 EE0A6D70 9E02FCE1 CDF7E2EC C03404CD 28342F61
            9172FE9C E98583FF 8E4F1232 EEF28183 C3FE3B1B 4C6FAD73
            3BB5FCBC 2EC22005 C58EF183 7D1683B2 C6F34A26 C1B2EFFA
            886B4238 611FCFDC DE355B3B 6519035B BC34F4DE F99C0238
            61B46FC9 D6E6C907 7AD91D26 91F7F7EE 598CB0FA C186D91C
            AEFE1309 85139270 B4130C93 BC437944 F4FD4452 E2D74DD3
            64F2E21E 71F54BFF 5CAE82AB 9C9DF69E E86D2BC5 22363A0D
            ABC52197 9B0DEADA 1DBF9A42 D5C4484E 0ABCD06B FA53DDEF
            3C1B20EE 3FD59D7C 25E41D2B 669E1EF1 6E6F52C3 164DF4FB
            7930E9E4 E58857B6 AC7D5F42 D69F6D18 7763CF1D 55034004
            87F55BA5 7E31CC7A 7135C886 EFB4318A ED6A1E01 2D9E6832
            A907600A 918130C4 6DC778F9 71AD0038 092999A3 33CB8B7A
            1A1DB93D 7140003C 2A4ECEA9 F98D0ACC 0A8291CD CEC97DCF
            8EC9B55A 7F88A46B 4DB5A851 F44182E1 C68A007E 5E655F6A";

        static readonly Lazy<BigInteger> s_P4096 = new(() => RfcTextToBytes(FFDHE4096_HEX));

        const int k_FFDHE4096_MinExponent = 325;
        const int k_FFDHE4096_MaxExponent = 511;

        private static readonly Lazy<RandomNumberGenerator> s_rng = new(() => RandomNumberGenerator.Create());

        // Generator for FFDHE groups is 2
        static readonly BigInteger s_G = new BigInteger(2);

        /// <summary>
        /// Creates a new RSADiffieHellman instance for the specified group.
        /// </summary>
        public static RSADiffieHellman Create(RSADiffieHellmanGroup group)
        {
            int min = 0;
            int max = 0;
            BigInteger p;

            switch (group)
            {
                case RSADiffieHellmanGroup.FFDHE2048:
                    p = s_P2048.Value;
                    min = k_FFDHE2048_MinExponent;
                    max = k_FFDHE2048_MaxExponent;
                    break;
                case RSADiffieHellmanGroup.FFDHE3072:
                    p = s_P3072.Value;
                    min = k_FFDHE3072_MinExponent;
                    max = k_FFDHE3072_MaxExponent;
                    break;
                case RSADiffieHellmanGroup.FFDHE4096:
                    p = s_P4096.Value;
                    min = k_FFDHE4096_MinExponent;
                    max = k_FFDHE4096_MaxExponent;
                    break;
                default:
                    throw new NotSupportedException("Unsupported RSA DH finite group type.");
            }

            var dh = new RSADiffieHellman();

            byte[] seed = new byte[1];
            s_rng.Value.GetBytes(seed);
            int keyLength = seed[0] % (max - min + 1) + min;

            byte[] key = new byte[1 + (keyLength + 7)/ 8];
            s_rng.Value.GetBytes(key);
            key[key.Length - 1] = 0;

            dh.m_privateKey = new BigInteger(key);
            dh.m_publicKey = BigInteger.ModPow(s_G, dh.m_privateKey, p);
            dh.m_nonceLength = max + 1;

            return dh;
        }

        /// <summary>
        /// Creates a new RSADiffieHellman instance from the nonce.
        /// </summary>
        public static RSADiffieHellman Create(byte[] nonce)
        {
            var dh = new RSADiffieHellman();

            var bytes = new byte[nonce.Length+1];

            for (int ii = 0; ii < nonce.Length; ii++)
            {
                bytes[ii] = nonce[nonce.Length - ii - 1];
            }

            dh.m_publicKey = new BigInteger(bytes);
            dh.m_nonceLength = nonce.Length;

            return dh;
        }

        /// <summary>
        /// Returns the nonce representing the public key.
        /// </summary>
        public byte[] GetNonce()
        {
            var nonce = new byte[m_nonceLength];
            var publicKey = m_publicKey.ToByteArray();

            for (int ii = 0; ii < publicKey.Length && ii < nonce.Length; ii++)
            {
                nonce[nonce.Length - 1 - ii] = publicKey[ii];
            }

            return nonce;
        }

        /// <summary>
        /// Derives the raw secret agreement from the remote key.
        /// </summary>
        public byte[] DeriveRawSecretAgreement(RSADiffieHellman remoteKey)
        {
            if (m_privateKey.IsZero)
            {
                throw new InvalidOperationException("Private key not available.");
            }

            BigInteger p;

            switch (m_nonceLength)
            {
                case 256:
                    p = s_P2048.Value;
                    break;
                case 384:
                    p = s_P3072.Value;
                    break;
                case 512:
                    p = s_P4096.Value;
                    break;
                default:
                    throw new NotSupportedException("Unsupported RSA DH finite group type.");
            }

            var shared = BigInteger.ModPow(remoteKey.m_publicKey, m_privateKey, p);

            var bytes = shared.ToByteArray();

            if (bytes.Length < m_nonceLength)
            {
                var padded = new byte[m_nonceLength];
                Array.Copy(bytes, 0, padded, 0, bytes.Length);
                bytes = padded;
            }

            // make sure bytes are in big-endian order.
            Array.Reverse(bytes);

            return bytes;
        }

        private static BigInteger RfcTextToBytes(string rfcText)
        {
            var bytes = new List<byte>();
            var digit = new char[2];
            int pos = 0;

            bytes.Add(0);

            for (int ii = 0; ii < rfcText.Length; ii++)
            {
                if (char.IsWhiteSpace(rfcText[ii]))
                {
                    continue;
                }

                digit[pos++] = rfcText[ii];

                if (pos == 2)
                {
                    bytes.Add(Convert.ToByte(new string(digit), 16));
                    pos = 0;
                }
            }

            bytes.Reverse();
            var integer = new BigInteger(bytes.ToArray());
            return integer;
        }
    }
}
