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
using System.Runtime.Serialization;
using System.Security.Cryptography;
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
    [Serializable]
    public class Nonce : IDisposable, ISerializable
    {
        /// <summary>
        /// Constructor
        /// </summary>
        private Nonce()
        {
            m_ecdh = null;
#if CURVE25519
            m_bcKeyPair = null;
#endif
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

#if NET8_0_OR_GREATER
#if xDEBUG
            Span<char> privateKey = stackalloc char[2048];

            if (m_ecdh.TryExportECPrivateKeyPem(privateKey, out int charsWritten))
            {
                Console.WriteLine($"Private Key PEM ({charsWritten} chars):");
            }
#endif

            ikm = m_ecdh.DeriveRawSecretAgreement(remoteNonce.m_ecdh.PublicKey);

            if (previousSecret != null)
            {
                for (int ii = 0; ii < ikm.Length && ii < previousSecret.Length; ii++)
                {
                    ikm[ii] ^= previousSecret[ii];
                }
            }
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
        public byte[] DeriveKey(
            byte[] secret,
            byte[] salt,
            KeyDerivationAlgorithm algorithm,
            int length)
        {
            if (m_ecdh != null)
            {
                HMAC hmac = algorithm switch
                {
                    KeyDerivationAlgorithm.HKDFSha256 => new HMACSHA256(salt),
                    KeyDerivationAlgorithm.HKDFSha384 => new HMACSHA384(salt),
                    _ => new HMACSHA256(salt)
                };

                //byte[] secret2 = m_ecdh.DeriveKeyFromHmac(
                //    remoteNonce.m_ecdh.PublicKey,
                //    algorithm,
                //    salt,
                //    null,
                //    null);

                //System.Console.WriteLine($"PRK2={Utils.ToHexString(secret2).Substring(0, 8)}");

                byte[] output = new byte[length];

                byte counter = 1;

                byte[] info = new byte[(hmac.HashSize / 8) + salt.Length + 1];
                Buffer.BlockCopy(salt, 0, info, 0, salt.Length);
                info[salt.Length] = counter++;

                byte[] hash = hmac.ComputeHash(info, 0, salt.Length + 1);

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

                    hash = hmac.ComputeHash(info, 0, info.Length);

                    for (int ii = 0; ii < hash.Length && pos < length; ii++)
                    {
                        output[pos++] = hash[ii];
                    }
                }

                return output;
            }

            return Data;
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

            switch (securityPolicy.CertificateKeyAlgorithm)
            {
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

            var nonce = new Nonce { Data = nonceData };

            switch (securityPolicy.CertificateKeyAlgorithm)
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
                    return nonce;
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

#if CURVE25519
        /// <summary>
        /// Creates a new Nonce object to be used in Curve25519 cryptography.
        /// </summary>
        /// <returns>A new Nonce object.</returns>
        private static Nonce CreateNonceForCurve25519()
        {
            SecureRandom random = new SecureRandom();
            IAsymmetricCipherKeyPairGenerator generator = new X25519KeyPairGenerator();
            generator.Init(new X25519KeyGenerationParameters(random));

            var keyPair = generator.GenerateKeyPair();

            byte[] senderNonce = new byte[X25519PublicKeyParameters.KeySize];
            ((X25519PublicKeyParameters)(keyPair.Public)).Encode(senderNonce, 0);

            var nonce = new Nonce() { Data = senderNonce, m_bcKeyPair = keyPair };

            return nonce;
        }

        /// <summary>
        /// Creates a Nonce object using the X448 elliptic curve algorithm.
        /// </summary>
        /// <returns>A Nonce object containing the generated nonce data and key pair.</returns>
        private static Nonce CreateNonceForCurve448()
        {
            SecureRandom random = new SecureRandom();
            IAsymmetricCipherKeyPairGenerator generator = new X448KeyPairGenerator();
            generator.Init(new X448KeyGenerationParameters(random));

            var keyPair = generator.GenerateKeyPair();

            byte[] senderNonce = new byte[X448PublicKeyParameters.KeySize];
            ((X448PublicKeyParameters)(keyPair.Public)).Encode(senderNonce, 0);

            var nonce = new Nonce() { Data = senderNonce, m_bcKeyPair = keyPair };

            return nonce;
        }
#endif

        /// <summary>
        /// Custom deserialization
        /// </summary>
        protected Nonce(SerializationInfo info, StreamingContext context)
        {
            string curveName = info.GetString("CurveName");

            if (curveName != null)
            {
                var ecParams = new ECParameters
                {
                    Curve = ECCurve.CreateFromFriendlyName(curveName),
                    Q = new ECPoint
                    {
                        X = (byte[])info.GetValue("QX", typeof(byte[])),
                        Y = (byte[])info.GetValue("QY", typeof(byte[]))
                    }
                };
                m_ecdh = ECDiffieHellman.Create(ecParams);
            }
            Data = (byte[])info.GetValue("Data", typeof(byte[]));
        }

        private ECDiffieHellman m_ecdh;
#if CURVE25519
        private AsymmetricCipherKeyPair m_bcKeyPair;
#endif

        private static readonly RandomNumberGenerator s_rng = RandomNumberGenerator.Create();
        private static uint s_minNonceLength = 32;

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
            if (disposing && m_ecdh != null)
            {
                m_ecdh.Dispose();
                m_ecdh = null;
            }
        }

        /// <summary>
        /// Custom serialization
        /// </summary>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (m_ecdh != null)
            {
                ECParameters ecParams = m_ecdh.ExportParameters(false);
                info.AddValue("CurveName", ecParams.Curve.Oid.FriendlyName);
                info.AddValue("QX", ecParams.Q.X);
                info.AddValue("QY", ecParams.Q.Y);
            }
            else
            {
                info.AddValue("CurveName", null);
                info.AddValue("QX", null);
                info.AddValue("QY", null);
            }
            info.AddValue("Data", Data);
        }
    }
}
