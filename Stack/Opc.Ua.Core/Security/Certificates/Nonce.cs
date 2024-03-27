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

#if ECC_SUPPORT
using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Security.Certificates;
using System.Runtime.Serialization;

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
        private ECDiffieHellman m_ecdh;
        

#if CURVE25519
        private AsymmetricCipherKeyPair m_bcKeyPair;
#endif

        enum Algorithm
        {
            Unknown,
            RSA,
            nistP256,
            nistP384,
            brainpoolP256r1,
            brainpoolP384r1,
            Ed25519
        }

        private Nonce()
        {
            m_ecdh = null;
#if CURVE25519
            m_bcKeyPair = null;
#endif
        }

        private HMAC returnHMACInstance(byte[] secret, HashAlgorithmName algorithm)
        {
            switch (algorithm.Name)
            {
                case "SHA256":
                    return new HMACSHA256(secret);
                case "SHA384":
                    return new HMACSHA384(secret);
                default:
                    return new HMACSHA256(secret);
            }
        }

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
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
        #endregion

        /// <summary>
        /// Gets the nonce data.
        /// </summary>
        public byte[] Data { get; private set; }

        /// <summary>
        /// Derives a key from the remote nonce, using the specified salt, hash algorithm, and length.
        /// </summary>
        /// <param name="remoteNonce">The remote nonce to use in key derivation.</param>
        /// <param name="salt">The salt to use in key derivation.</param>
        /// <param name="algorithm">The hash algorithm to use in key derivation.</param>
        /// <param name="length">The length of the derived key.</param>
        /// <returns>The derived key.</returns>
        public byte[] DeriveKey(Nonce remoteNonce, byte[] salt, HashAlgorithmName algorithm, int length)
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
                var secret = m_ecdh.DeriveKeyFromHmac(remoteNonce.m_ecdh.PublicKey, algorithm, salt, null, null);

                byte[] output = new byte[length];

                HMAC hmac = returnHMACInstance(secret, algorithm);

                byte counter = 1;

                byte[] info = new byte[hmac.HashSize / 8 + salt.Length + 1];
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
                    info[info.Length - 1] = counter++;

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
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="nonceLength">The length of the nonce.</param>
        /// <returns>A <see cref="Nonce"/> object containing the generated nonce.</returns>
        public static Nonce CreateNonce(string securityPolicyUri, uint nonceLength)
        {
            if (securityPolicyUri == null)
            {
                throw new ArgumentNullException(nameof(securityPolicyUri));
            }

            Nonce nonce = null;

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256: { return CreateNonce(ECCurve.NamedCurves.nistP256); }
                case SecurityPolicies.ECC_nistP384: { return CreateNonce(ECCurve.NamedCurves.nistP384); }
                case SecurityPolicies.ECC_brainpoolP256r1: { return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1); }
                case SecurityPolicies.ECC_brainpoolP384r1: { return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1); }
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
                {
                    nonce = new Nonce() {
                        Data = Utils.Nonce.CreateNonce(nonceLength)
                    };

                    return nonce;
                }
            }
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

            var nonce = new Nonce() {
                Data = senderNonce,
                m_bcKeyPair = keyPair
            };

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

            var nonce = new Nonce() {
                Data = senderNonce,
                m_bcKeyPair = keyPair
            };

            return nonce;
        }
#endif

        /// <summary>
        /// Creates a new Nonce instance using the specified elliptic curve.
        /// </summary>
        /// <param name="curve">The elliptic curve to use for the ECDH key exchange.</param>
        /// <returns>A new Nonce instance.</returns>
        private static Nonce CreateNonce(ECCurve curve)
        {
            var ecdh = (ECDiffieHellman)ECDiffieHellman.Create(curve);
            var ecdhParameters = ecdh.ExportParameters(false);
            int xLen = ecdhParameters.Q.X.Length;
            int yLen = ecdhParameters.Q.Y.Length;
            byte[] senderNonce = new byte[xLen + yLen];
            Array.Copy(ecdhParameters.Q.X, senderNonce, xLen);
            Array.Copy(ecdhParameters.Q.Y, 0, senderNonce, xLen, yLen);

            var nonce = new Nonce() {
                Data = senderNonce,
                m_ecdh = ecdh
            };

            return nonce;
        }

        /// <summary>
        /// Creates a new Nonce object for the specified security policy URI and nonce data.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy URI.</param>
        /// <param name="nonceData">The nonce data.</param>
        /// <returns>A new Nonce object.</returns>
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

            Nonce nonce = new Nonce() {
                Data = nonceData
            };

            switch (securityPolicyUri)
            {
                case SecurityPolicies.ECC_nistP256: { return CreateNonce(ECCurve.NamedCurves.nistP256, nonceData); }
                case SecurityPolicies.ECC_nistP384: { return CreateNonce(ECCurve.NamedCurves.nistP384, nonceData); }
                case SecurityPolicies.ECC_brainpoolP256r1: { return CreateNonce(ECCurve.NamedCurves.brainpoolP256r1, nonceData); }
                case SecurityPolicies.ECC_brainpoolP384r1: { return CreateNonce(ECCurve.NamedCurves.brainpoolP384r1, nonceData); }

                case SecurityPolicies.ECC_curve25519:
                {
                    return CreateNonceForCurve25519(nonceData);
                }

                case SecurityPolicies.ECC_curve448:
                {
                    return CreateNonceForCurve448(nonceData);
                }

                default:
                {
                    break;
                }
            }

            return nonce;
        }


        /// <summary>
        /// Creates a new Nonce object for use with Curve25519.
        /// </summary>
        /// <param name="nonceData">The nonce data to use.</param>
        /// <returns>A new Nonce object.</returns>
        private static Nonce CreateNonceForCurve25519(byte[] nonceData)
        {
            var nonce = new Nonce() {
                Data = nonceData,
            };

            return nonce;
        }

        /// <summary>
        /// Creates a new Nonce instance for Curve448.
        /// </summary>
        /// <param name="nonceData">The nonce data.</param>
        /// <returns>A new Nonce instance.</returns>
        private static Nonce CreateNonceForCurve448(byte[] nonceData)
        {
            var nonce = new Nonce() {
                Data = nonceData,
            };

            return nonce;
        }

        /// <summary>
        /// Creates a new Nonce instance with the specified curve and nonce data.
        /// </summary>
        /// <param name="curve">The elliptic curve to use for the ECDH key exchange.</param>
        /// <param name="nonceData">The nonce data to use for the ECDH key exchange.</param>
        /// <returns>A new Nonce instance with the specified curve and nonce data.</returns>
        private static Nonce CreateNonce(ECCurve curve, byte[] nonceData)
        {
            Nonce nonce = new Nonce() {
                Data = nonceData
            };

            int keyLength = nonceData.Length;

            byte[] qx = new byte[keyLength / 2];
            byte[] qy = new byte[keyLength / 2];
            Buffer.BlockCopy(nonceData, 0, qx, 0, keyLength / 2);
            Buffer.BlockCopy(nonceData, keyLength / 2, qy, 0, keyLength / 2);

            var ecdhParameters = new ECParameters {
                Curve = curve,
                Q = { X = qx, Y = qy }
            };

            nonce.m_ecdh = ECDiffieHellman.Create(ecdhParameters);

            return nonce;
        }

        /// <summary>
        /// Custom deserialization
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected Nonce(SerializationInfo info, StreamingContext context)
        {
            var curveName = info.GetString("CurveName");
            var ecParams = new ECParameters {
                Curve = ECCurve.CreateFromFriendlyName(curveName),
                Q = new ECPoint {
                    X = (byte[])info.GetValue("QX", typeof(byte[])),
                    Y = (byte[])info.GetValue("QY", typeof(byte[])),
                }
            };
            m_ecdh = ECDiffieHellman.Create(ecParams);

            Data = (byte[])info.GetValue("Data", typeof (byte[]));
        }

        /// <summary>
        /// Custom serializarion
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (m_ecdh != null)
            {
                var ecParams = m_ecdh.ExportParameters(false);
                info.AddValue("CurveName", ecParams.Curve.Oid.FriendlyName);
                info.AddValue("QX", ecParams.Q.X);
                info.AddValue("QY", ecParams.Q.Y);
            }
            if (Data != null)
            {
                info.AddValue("Data", Data);
            }
        }
    }
}
#endif
