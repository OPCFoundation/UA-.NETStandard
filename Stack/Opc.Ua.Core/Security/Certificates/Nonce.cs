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
#if ECC_SUPPORT
    public class Nonce : IDisposable, ISerializable
#else
    public class Nonce : ISerializable
#endif
    {
        /// <summary>
        /// Constructor
        /// </summary>
        private Nonce()
        {
#if ECC_SUPPORT
            m_ecdh = null;
#endif
#if CURVE25519
            m_bcKeyPair = null;
#endif
        }

        /// <summary>
        /// Gets the nonce data.
        /// </summary>
        public byte[] Data { get; private set; }

#if ECC_SUPPORT
        /// <summary>
        /// Derives a key from the remote nonce, using the specified salt, hash algorithm, and length.
        /// </summary>
        /// <param name="remoteNonce">The remote nonce to use in key derivation.</param>
        /// <param name="salt">The salt to use in key derivation.</param>
        /// <param name="algorithm">The hash algorithm to use in key derivation.</param>
        /// <param name="length">The length of the derived key.</param>
        /// <returns>The derived key.</returns>
        public byte[] DeriveKey(
            Nonce remoteNonce,
            byte[] salt,
            HashAlgorithmName algorithm,
            int length)
        {
#if CURVE25519
            if (m_bcKeyPair != null)
            {
                var localPublicKey = m_bcKeyPair.Public;

                if (localPublicKey is X25519PublicKeyParameters)
                {
                    X25519Agreement agreement = new X25519Agreement();
                    agreement.Init(m_bcKeyPair.Private);

                    var key = new X25519PublicKeyParameters(remoteNonce.Data, 0);
                    byte[] secret = new byte[agreement.AgreementSize];
                    agreement.CalculateAgreement(key, secret, 0);

                    HkdfBytesGenerator generator = new HkdfBytesGenerator(new Sha256Digest());
                    generator.Init(new HkdfParameters(secret, salt, salt));

                    byte[] output = new byte[length];
                    generator.GenerateBytes(output, 0, output.Length);
                    return output;
                }

                if (localPublicKey is X448PublicKeyParameters)
                {
                    X448Agreement agreement = new X448Agreement();
                    agreement.Init(m_bcKeyPair.Private);

                    var key = new X448PublicKeyParameters(remoteNonce.Data, 0);
                    byte[] secret = new byte[agreement.AgreementSize];
                    agreement.CalculateAgreement(key, secret, 0);

                    HkdfBytesGenerator generator = new HkdfBytesGenerator(new Sha256Digest());
                    generator.Init(new HkdfParameters(secret, salt, salt));

                    byte[] output = new byte[length];
                    generator.GenerateBytes(output, 0, output.Length);
                    return output;
                }

                throw new NotSupportedException();
            }
#endif
            if (m_ecdh != null)
            {
                byte[] secret = m_ecdh.DeriveKeyFromHmac(
                    remoteNonce.m_ecdh.PublicKey,
                    algorithm,
                    salt,
                    null,
                    null);

                byte[] output = new byte[length];

                HMAC hmac = algorithm.Name switch
                {
                    "SHA256" => new HMACSHA256(secret),
                    "SHA384" => new HMACSHA384(secret),
                    _ => new HMACSHA256(secret)
                };

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
#endif

        /// <summary>
        /// Creates a nonce for the specified security policy URI and nonce length.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <returns>A <see cref="Nonce"/> object containing the generated nonce.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="securityPolicyUri"/> is <c>null</c>.</exception>
        public static Nonce CreateNonce(string securityPolicyUri)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            switch (securityPolicyUri)
            {
#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                    return CreateNonce(ECCurve.NamedCurves.nistP256);
                case SecurityPolicies.ECC_nistP384:
                    return CreateNonce(ECCurve.NamedCurves.nistP384);
                case SecurityPolicies.ECC_brainpoolP256r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1);
                case SecurityPolicies.ECC_brainpoolP384r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1);

#endif
#if CURVE25519
                case SecurityPolicies.ECC_curve25519:
                {
                    return CreateNonceForCurve25519();
                }

                case SecurityPolicies.ECC_curve448:
                {
                    return CreateNonceForCurve448();
                }
#endif
                default:
                    uint rsaNonceLength = GetNonceLength(securityPolicyUri);
                    return new Nonce { Data = CreateRandomNonceData(rsaNonceLength) };
            }
        }

        /// <summary>
        /// Creates a new Nonce object for the specified security policy URI and nonce data.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="nonceData">The nonce data.</param>
        /// <returns>A new Nonce object.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="securityPolicyUri"/> is <c>null</c>.</exception>
        public static Nonce CreateNonce(string securityPolicyUri, byte[] nonceData)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            if (nonceData == null)
            {
                throw new ArgumentNullException(nameof(nonceData));
            }

            var nonce = new Nonce { Data = nonceData };

            switch (securityPolicyUri)
            {
#if ECC_SUPPORT
                case SecurityPolicies.ECC_nistP256:
                    return CreateNonce(ECCurve.NamedCurves.nistP256, nonceData);
                case SecurityPolicies.ECC_nistP384:
                    return CreateNonce(ECCurve.NamedCurves.nistP384, nonceData);
                case SecurityPolicies.ECC_brainpoolP256r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1, nonceData);
                case SecurityPolicies.ECC_brainpoolP384r1:
                    return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1, nonceData);

#endif
                case SecurityPolicies.ECC_curve25519:
                    return CreateNonceForCurve25519(nonceData);
                case SecurityPolicies.ECC_curve448:
                    return CreateNonceForCurve448(nonceData);
            }

            return nonce;
        }

        /// <summary>
        /// Generates a Nonce for cryptographic functions of a given length.
        /// </summary>
        /// <returns>The requested Nonce as a</returns>
        public static byte[] CreateRandomNonceData(uint length)
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
            string securityPolicyUri)
        {
            return ValidateNonce(nonce, securityMode, GetNonceLength(securityPolicyUri));
        }

        /// <summary>
        /// Validates the nonce for a message security mode and a minimum length.
        /// </summary>
        public static bool ValidateNonce(
            byte[] nonce,
            MessageSecurityMode securityMode,
            uint minNonceLength)
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
        /// Returns the length of the symmetric encryption key for a security policy.
        /// </summary>
        public static uint GetNonceLength(string securityPolicyUri)
        {
            switch (securityPolicyUri)
            {
                case SecurityPolicies.Basic256:
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                case SecurityPolicies.ECC_curve25519:
                    return 32;
                case SecurityPolicies.ECC_nistP256:
                case SecurityPolicies.ECC_brainpoolP256r1:
                    // Q.X + Q.Y = 32 + 32 = 64
                    return 64;
                case SecurityPolicies.ECC_nistP384:
                case SecurityPolicies.ECC_brainpoolP384r1:
                    // Q.X + Q.Y = 48 + 48 = 96
                    return 96;
                case SecurityPolicies.ECC_curve448:
                    // Q.X
                    return 56;
                default:
                    // Minimum nonce length by default
                    return s_minNonceLength;
            }
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

#if ECC_SUPPORT
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
#endif

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
#if ECC_SUPPORT
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
#endif
            Data = (byte[])info.GetValue("Data", typeof(byte[]));
        }

#if ECC_SUPPORT
        private ECDiffieHellman m_ecdh;

#endif

#if CURVE25519
        private AsymmetricCipherKeyPair m_bcKeyPair;
#endif

        private static readonly RandomNumberGenerator s_rng = RandomNumberGenerator.Create();
        private static uint s_minNonceLength = 32;

#if ECC_SUPPORT
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
#endif

        /// <summary>
        /// Custom serialization
        /// </summary>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
#if ECC_SUPPORT
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
#endif
            info.AddValue("Data", Data);
        }
    }
}
