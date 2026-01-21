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
using System.IO;
using System.Security.Cryptography;

namespace Opc.Ua
{
    /// <summary>
    /// Defines constants for key security policies.
    /// </summary>
    public class SecurityPolicyInfo
    {
        /// <summary>
        /// Creates a new instance of the <see cref="SecurityPolicyInfo"/> class.
        /// </summary>
        /// <param name="uri">The unique identifier.</param>
        /// <param name="name">The display name.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public SecurityPolicyInfo(string uri, string name = null)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("The URI is not a valid security policy.", nameof(uri));
            }

            Uri = uri;
            Name = name ?? SecurityPolicies.GetDisplayName(uri) ?? uri;
        }

        /// <summary>
        /// Short name for the policy.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The unique identifier for the policy.
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// Returns true if the policy is considered deprecated and should not be used for new deployments.
        /// </summary>
        public bool IsDeprecated { get; private set; }

        /// <summary>
        /// The symmetric signature algorithm to use.
        /// </summary>
        public SymmetricSignatureAlgorithm SymmetricSignatureAlgorithm { get; private set; }

        /// <summary>
        /// The symmetric encryption algorithm to use.
        /// </summary>
        public SymmetricEncryptionAlgorithm SymmetricEncryptionAlgorithm { get; private set; }

        /// <summary>
        /// The asymmetric signature algorithm to use.
        /// </summary>
        public AsymmetricSignatureAlgorithm AsymmetricSignatureAlgorithm { get; private set; }

        /// <summary>
        /// The symmetric encryption algorithm to use.
        /// </summary>
        public AsymmetricEncryptionAlgorithm AsymmetricEncryptionAlgorithm { get; private set; }

        /// <summary>
        /// The minimum length, in bits, for an asymmetric key.
        /// </summary>
        public int MinAsymmetricKeyLength { get; private set; }

        /// <summary>
        /// The maximum length, in bits, for an asymmetric key.
        /// </summary>
        public int MaxAsymmetricKeyLength { get; private set; }

        /// <summary>
        /// The key derivation algorithm to use.
        /// </summary>
        public KeyDerivationAlgorithm KeyDerivationAlgorithm { get; private set; }

        /// <summary>
        /// The length in bytes of the derived key used for message authentication.
        /// </summary>
        public int DerivedSignatureKeyLength { get; private set; }

        /// <summary>
        /// The asymmetric signature algorithm used to sign certificates.
        /// </summary>
        public AsymmetricSignatureAlgorithm CertificateSignatureAlgorithm { get; private set; }

        /// <summary>
        /// Returns algorithm family used to create asymmetric key pairs used with Certificates.
        /// </summary>
        public CertificateKeyFamily CertificateKeyFamily { get; private set; }

        /// <summary>
        /// The algorithm used to create asymmetric key pairs used with Certificates.
        /// </summary>
        public CertificateKeyAlgorithm CertificateKeyAlgorithm { get; private set; }

        /// <summary>
        /// The algorithm used to create asymmetric key pairs used for EphemeralKeys.
        /// </summary>
        public CertificateKeyAlgorithm EphemeralKeyAlgorithm { get; private set; }

        /// <summary>
        /// The length, in bytes, of the Nonces used when opening a SecureChannel.
        /// </summary>
        public int SecureChannelNonceLength { get; private set; }

        /// <summary>
        /// The length, in bytes, of the data used to initialize the symmetric algorithm.
        /// </summary>
        public int InitializationVectorLength { get; private set; }

        /// <summary>
        /// The length, in bytes, of the symmetric signature.
        /// </summary>
        public int SymmetricSignatureLength { get; private set; }

        /// <summary>
        /// The length, in bytes, of the symmetric encryption key.
        /// </summary>
        public int SymmetricEncryptionKeyLength { get; private set; }

        /// <summary>
        /// If TRUE, the 1024 based SequenceNumber rules apply to the SecurityPolicy.
        /// If FALSE, the 0 based SequenceNumber rules apply.
        /// </summary>
        public bool LegacySequenceNumbers { get; private set; }

        /// <summary>
        /// If TRUE, the enhancements to the SecureChannel are required for the SecurityPolicy.
        /// • Channel-bound Signature calculations in CreateSession/ActivateSession;
        /// • Session transfer tokens in ActivateSession;
        /// • Chained symmetric key derivation when renewing SecureChannels.
        /// • Allow padding when using Authenticated Encryption;
        /// </summary>
        public bool SecureChannelEnhancements { get; private set; }

        /// <summary>
        /// Whether the padding is required with symmetric encryption.
        /// </summary>
        public bool NoSymmetricEncryptionPadding =>
            SymmetricEncryptionAlgorithm == SymmetricEncryptionAlgorithm.Aes256Gcm ||
            SymmetricEncryptionAlgorithm == SymmetricEncryptionAlgorithm.Aes128Gcm ||
            SymmetricEncryptionAlgorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305;

        /// <summary>
        /// Returns the derived server key data length.
        /// </summary>
        public int ServerKeyDataLength =>
             (DerivedSignatureKeyLength + SymmetricEncryptionKeyLength + InitializationVectorLength);

        /// <summary>
        /// Returns the derived client key data length.
        /// </summary>
        public int ClientKeyDataLength =>
             (DerivedSignatureKeyLength + SymmetricEncryptionKeyLength + InitializationVectorLength);

        /// <summary>
        /// Returns the data to be signed by the server when creating a session.
        /// </summary>
        public byte[] GetUserTokenSignatureData(
            byte[] channelThumbprint,
            byte[] serverNonce,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] clientNonce)
        {
            byte[] data = null;

            CryptoTrace.Start(ConsoleColor.Yellow, "UserTokenSignatureData");

            if (SecureChannelEnhancements)
            {
                CryptoTrace.WriteLine($"ChannelThumbprint={CryptoTrace.KeyToString(channelThumbprint)}");
                CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverNonce)}");
                CryptoTrace.WriteLine($"ServerCertificate={CryptoTrace.KeyToString(serverCertificate)}");
                CryptoTrace.WriteLine($"ServerChannelCertificate={CryptoTrace.KeyToString(serverChannelCertificate)}");
                CryptoTrace.WriteLine($"ClientCertificate={CryptoTrace.KeyToString(clientCertificate)}");
                CryptoTrace.WriteLine($"ClientChannelCertificate={CryptoTrace.KeyToString(clientChannelCertificate)}");
                CryptoTrace.WriteLine($"ClientNonce={CryptoTrace.KeyToString(clientNonce)}");

                data = Utils.Append(
                    channelThumbprint,
                    serverNonce,
                    serverCertificate,
                    serverChannelCertificate,
                    clientCertificate,
                    clientChannelCertificate,
                    clientNonce);
            }
            else
            {
                CryptoTrace.WriteLine($"ServerCertificate={CryptoTrace.KeyToString(serverCertificate)}");
                CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverNonce)}");

                data = Utils.Append(
                    serverCertificate,
                    serverNonce);
            }

            CryptoTrace.Finish("UserTokenSignatureData");
            return data;
        }

        /// <summary>
        /// Returns the data to be signed by the server when creating a session.
        /// </summary>
        public byte[] GetServerSignatureData(
            byte[] channelThumbprint,
            byte[] clientNonce,
            byte[] serverChannelCertificate,
            byte[] clientCertificate,
            byte[] clientChannelCertificate,
            byte[] serverNonce)
        {
            byte[] data = null;

            CryptoTrace.Start(ConsoleColor.Yellow, "ServerSignatureData");

            if (SecureChannelEnhancements)
            {
                CryptoTrace.WriteLine($"ChannelThumbprint={CryptoTrace.KeyToString(channelThumbprint)}");
                CryptoTrace.WriteLine($"ClientNonce={CryptoTrace.KeyToString(clientNonce)}");
                CryptoTrace.WriteLine($"ServerChannelCertificate={CryptoTrace.KeyToString(serverChannelCertificate)}");
                CryptoTrace.WriteLine($"ClientChannelCertificate={CryptoTrace.KeyToString(clientChannelCertificate)}");
                CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverNonce)}");

                data = Utils.Append(
                    channelThumbprint,
                    clientNonce,
                    serverChannelCertificate,
                    clientChannelCertificate,
                    serverNonce);
            }
            else
            {
                CryptoTrace.WriteLine($"ClientCertificate={CryptoTrace.KeyToString(clientCertificate)}");
                CryptoTrace.WriteLine($"ClientNonce={CryptoTrace.KeyToString(clientNonce)}");

                data = Utils.Append(
                    clientCertificate,
                    clientNonce);
            }

            CryptoTrace.Finish("ServerSignatureData");
            return data;
        }

        /// <summary>
        /// Returns the data to be signed by the client when creating a session.
        /// </summary>
        public byte[] GetClientSignatureData(
            byte[] channelThumbprint,
            byte[] serverNonce,
            byte[] serverCertificate,
            byte[] serverChannelCertificate,
            byte[] clientChannelCertificate,
            byte[] clientNonce)
        {
            byte[] data = null;

            CryptoTrace.Start(ConsoleColor.Yellow, "ClientSignatureData");

            if (SecureChannelEnhancements)
            {
                CryptoTrace.WriteLine($"ChannelThumbprint={CryptoTrace.KeyToString(channelThumbprint)}");
                CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverNonce)}");
                CryptoTrace.WriteLine($"ServerCertificate={CryptoTrace.KeyToString(serverCertificate)}");
                CryptoTrace.WriteLine($"ServerChannelCertificate={CryptoTrace.KeyToString(serverChannelCertificate)}");
                CryptoTrace.WriteLine($"ClientChannelCertificate={CryptoTrace.KeyToString(clientChannelCertificate)}");
                CryptoTrace.WriteLine($"ClientNonce={CryptoTrace.KeyToString(clientNonce)}");

                data = Utils.Append(
                    channelThumbprint,
                    serverNonce,
                    serverCertificate,
                    serverChannelCertificate,
                    clientChannelCertificate,
                    clientNonce);
            }
            else
            {
                CryptoTrace.WriteLine($"ServerCertificate={CryptoTrace.KeyToString(serverCertificate)}");
                CryptoTrace.WriteLine($"ServerNonce={CryptoTrace.KeyToString(serverNonce)}");

                data = Utils.Append(
                    serverCertificate,
                    serverNonce);
            }

            CryptoTrace.Finish("ClientSignatureData");
            return data;
        }

        /// <summary>
        /// Returns a HMAC based on the symmetric signature algorithm.
        /// </summary>
        public HMAC CreateSignatureHmac(byte[] signingKey)
        {
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            return SymmetricSignatureAlgorithm switch
            {
                SymmetricSignatureAlgorithm.HmacSha1 => new HMACSHA1(signingKey),
                SymmetricSignatureAlgorithm.HmacSha256 => new HMACSHA256(signingKey),
                SymmetricSignatureAlgorithm.HmacSha384 => new HMACSHA384(signingKey),
                _ => null
            };
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
        }

        /// <summary>
        /// Returns a HashAlgorithmName based on the KeyDerivationAlgorithm.
        /// </summary>
        public HashAlgorithmName GetKeyDerivationHashAlgorithmName()
        {
            return KeyDerivationAlgorithm switch
            {
                KeyDerivationAlgorithm.PSha1 => HashAlgorithmName.SHA1,
                KeyDerivationAlgorithm.PSha256 => HashAlgorithmName.SHA256,
                KeyDerivationAlgorithm.HKDFSha256 => HashAlgorithmName.SHA256,
                KeyDerivationAlgorithm.HKDFSha384 => HashAlgorithmName.SHA384,
                _ => HashAlgorithmName.SHA256
            };
        }

        /// <summary>
        /// The security policy that does not provide any security.
        /// </summary>
        public static readonly SecurityPolicyInfo None = new(SecurityPolicies.None)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 0,
            InitializationVectorLength = 0,
            SymmetricSignatureLength = 0,
            MinAsymmetricKeyLength = 0,
            MaxAsymmetricKeyLength = 0,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            CertificateKeyFamily = CertificateKeyFamily.None,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.None,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.None,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.None,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.None,
            SecureChannelEnhancements = false
        };

        /// <summary>
        /// The security policy that uses SHA1 and 128 bit encryption. This policy is considered insecure and should not be used for new deployments.
        /// </summary>
        public static readonly SecurityPolicyInfo Basic128Rsa15 = new(SecurityPolicies.Basic128Rsa15)
        {
            DerivedSignatureKeyLength = 128 / 8,
            SymmetricEncryptionKeyLength = 128 / 8,
            // HMAC-SHA1 produces a 160-bit MAC
            SymmetricSignatureLength = 160 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 1024,
            MaxAsymmetricKeyLength = 2048,
            SecureChannelNonceLength = 16,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha1,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha1,
            IsDeprecated = true
        };

        /// <summary>
        /// The security policy that uses SHA1 and 256 bit encryption. This policy is considered insecure and should not be used for new deployments.
        /// </summary>
        public static readonly SecurityPolicyInfo Basic256 = new(SecurityPolicies.Basic256)
        {
            DerivedSignatureKeyLength = 192 / 8,
            SymmetricEncryptionKeyLength = 256 / 8,
            // HMAC-SHA1 produces a 160-bit MAC
            SymmetricSignatureLength = 160 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 1024,
            MaxAsymmetricKeyLength = 2048,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha1,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha1,
            IsDeprecated = true
        };

        /// <summary>
        /// Aes128_Sha256_RsaOaep is a required minimum security policy. It uses SHA256 and 128 bit encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo Aes128_Sha256_RsaOaep = new(SecurityPolicies.Aes128_Sha256_RsaOaep)
        {
            DerivedSignatureKeyLength = 256 / 8,
            SymmetricEncryptionKeyLength = 128 / 8,
            SymmetricSignatureLength = 256 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 2048,
            MaxAsymmetricKeyLength = 4096,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            IsDeprecated = false
        };

        /// <summary>
        /// Basic256Sha256 is a required minimum security policy. It uses SHA256 and 256 bit encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo Basic256Sha256 = new(SecurityPolicies.Basic256Sha256)
        {
            DerivedSignatureKeyLength = 256 / 8,
            SymmetricEncryptionKeyLength = 256 / 8,
            SymmetricSignatureLength = 256 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 2048,
            MaxAsymmetricKeyLength = 4096,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            IsDeprecated = false
        };

        /// <summary>
        /// Aes256_Sha256_RsaPss is a optional high security policy. It uses SHA256 and 256 bit encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo Aes256_Sha256_RsaPss = new(SecurityPolicies.Aes256_Sha256_RsaPss)
        {
            DerivedSignatureKeyLength = 256 / 8,
            SymmetricEncryptionKeyLength = 256 / 8,
            MinAsymmetricKeyLength = 2048,
            MaxAsymmetricKeyLength = 4096,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha256,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPssSha256,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            InitializationVectorLength = 128 / 8,
            SymmetricSignatureLength = 256 / 8,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve25519 is a required minimum security policy. It uses ChaChaPoly and 256 bit encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve25519 = new(SecurityPolicies.ECC_curve25519)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve25519 is a required minimum security policy. It uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve25519_AesGcm = new(SecurityPolicies.ECC_curve25519_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve25519 is a required minimum security policy. It uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve25519_ChaChaPoly = new(SecurityPolicies.ECC_curve25519_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve448 is a required minimum security policy. It uses ChaChaPoly and 256 bit encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve448 = new(SecurityPolicies.ECC_curve448)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 456,
            MaxAsymmetricKeyLength = 456,
            SecureChannelNonceLength = 56,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve448 is a required minimum security policy. It uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve448_AesGcm = new(SecurityPolicies.ECC_curve448_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 456,
            MaxAsymmetricKeyLength = 456,
            SecureChannelNonceLength = 56,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC Curve448 is a required minimum security policy. It uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_curve448_ChaChaPoly = new(SecurityPolicies.ECC_curve448_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 456,
            MaxAsymmetricKeyLength = 456,
            SecureChannelNonceLength = 56,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP256 is a required minimum security policy.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP256 = new(SecurityPolicies.ECC_nistP256)
        {
            DerivedSignatureKeyLength = 256 / 8,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 128 / 8,
            SymmetricSignatureLength = 256 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC_nistP256_AesGcm is an ECC nistP256 variant that uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP256_AesGcm = new(SecurityPolicies.ECC_nistP256_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC_nistP256_AesGcm is an ECC nistP256 variant that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP256_ChaChaPoly = new(SecurityPolicies.ECC_nistP256_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP384 = new(SecurityPolicies.ECC_nistP384)
        {
            DerivedSignatureKeyLength = 384 / 8,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 128 / 8,
            SymmetricSignatureLength = 384 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy that uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP384_AesGcm = new(SecurityPolicies.ECC_nistP384_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_nistP384_ChaChaPoly = new(SecurityPolicies.ECC_nistP384_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP256r1 is a required minimum security policy.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP256r1 = new(SecurityPolicies.ECC_brainpoolP256r1)
        {
            DerivedSignatureKeyLength = 256 / 8,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 128 / 8,
            SymmetricSignatureLength = 256 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC_brainpoolP256r1_AesGcm is an ECC brainpoolP256 variant that uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP256r1_AesGcm = new (SecurityPolicies.ECC_brainpoolP256r1_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC_brainpoolP256_AES is an ECC brainpoolP256 variant that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP256r1_ChaChaPoly = new(SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 256,
            MaxAsymmetricKeyLength = 256,
            SecureChannelNonceLength = 64,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP384r1 = new(SecurityPolicies.ECC_brainpoolP384r1)
        {
            DerivedSignatureKeyLength = 384 / 8,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            SecureChannelEnhancements = false,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy that uses AES-GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP384r1_AesGcm = new(SecurityPolicies.ECC_brainpoolP384r1_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes256Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo ECC_brainpoolP384r1_ChaChaPoly = new(SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 384,
            MaxAsymmetricKeyLength = 384,
            SecureChannelNonceLength = 96,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };
         
        /// <summary>
        /// The RSA_DH_AES_GCM is an high security policy that uses AES GCM for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo RSA_DH_AesGcm = new(SecurityPolicies.RSA_DH_AesGcm)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 128 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 2048,
            MaxAsymmetricKeyLength = 4096,
            SecureChannelNonceLength = 384,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.RSADH,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };

        /// <summary>
        /// The RSA_DH_ChaChaPoly is an high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public readonly static SecurityPolicyInfo RSA_DH_ChaChaPoly = new(SecurityPolicies.RSA_DH_ChaChaPoly)
        {
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 256 / 8,
            InitializationVectorLength = 96 / 8,
            SymmetricSignatureLength = 128 / 8,
            MinAsymmetricKeyLength = 2048,
            MaxAsymmetricKeyLength = 4096,
            SecureChannelNonceLength = 384,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.RSADH,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false
        };
    }

    /// <summary>
    /// The algorithm family used to generate key pairs.
    /// </summary>
    public enum CertificateKeyFamily
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// The RSA algorithm.
        /// </summary>
        RSA,

        /// <summary>
        /// Ellipic curve algorithms.
        /// </summary>
        ECC
    }

    /// <summary>
    /// The algorithm used to generate key pairs.
    /// </summary>
    public enum CertificateKeyAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// The RSA algorithm.
        /// </summary>
        RSA,

        /// <summary>
        /// The Diffie-Hellman algorith with RSA public keys.
        /// </summary>
        RSADH,

        /// <summary>
        /// The NIST P-256 ellipic curve algorithm.
        /// </summary>
        NistP256,

        /// <summary>
        /// The NIST P-384 ellipic curve algorithm.
        /// </summary>
        NistP384,

        /// <summary>
        /// The non-twisted Brainpool P-256 ellipic curve algorithm.
        /// </summary>
        BrainpoolP256r1,

        /// <summary>
        /// The non-twisted Brainpool P-384 ellipic curve algorithm.
        /// </summary>
        BrainpoolP384r1,

        /// <summary>
        /// The Edward Curve25519 ellipic curve algorithm.
        /// </summary>
        Curve25519,

        /// <summary>
        /// The Edward Curve25519 ellipic curve algorithm.
        /// </summary>
        Curve448
    }

    /// <summary>
    /// The symmetric key derivation algorithm used to create shared keys.
    /// </summary>
    public enum KeyDerivationAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// The P_SHA pseudo-random function with SHA1. This algorithm is considered insecure.
        /// </summary>
        PSha1,

        /// <summary>
        /// The P_SHA pseudo-random function with SHA256.
        /// </summary>
        PSha256,

        /// <summary>
        /// The HKDF pseudo-random function with SHA256.
        /// </summary>
        HKDFSha256,

        /// <summary>
        /// The HKDF pseudo-random function with SHA384.
        /// </summary>
        HKDFSha384
    }

    /// <summary>
    /// The asymmetric encryption algorithm used to encrypt messages.
    /// </summary>
    public enum AsymmetricEncryptionAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// RSA PKCS #1 v1.5. This algorithm is considered insecure.
        /// </summary>
        RsaPkcs15Sha1,

        /// <summary>
        /// RSA with OAEP padding with SHA1. This algorithm is considered insecure.
        /// </summary>
        RsaOaepSha1,

        /// <summary>
        /// RSA with OAEP padding with SHA256 .
        /// </summary>
        RsaOaepSha256
    }

    /// <summary>
    /// The asymmetric signature algorithm used to sign messages.
    /// </summary>
    public enum AsymmetricSignatureAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// RSA PKCS #1 v1.5 with SHA1. This algorithm is considered insecure.
        /// </summary>
        RsaPkcs15Sha1,

        /// <summary>
        /// RSA PKCS #1 v1.5 with SHA256.
        /// </summary>
        RsaPkcs15Sha256,

        /// <summary>
        /// RSA PSS with SHA256.
        /// </summary>
        RsaPssSha256,

        /// <summary>
        /// ECDSA with SHA256.
        /// </summary>
        EcdsaSha256,

        /// <summary>
        /// ECDSA with SHA384.
        /// </summary>
        EcdsaSha384,

        /// <summary>
        /// ECDSA with Curve 25519.
        /// </summary>
        EcdsaPure25519,

        /// <summary>
        /// ECDSA with Curve 448.
        /// </summary>
        EcdsaPure448
    }

    /// <summary>
    /// The symmetric signature algorithm used to sign messages.
    /// </summary>
    public enum SymmetricSignatureAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// HMAC with SHA1
        /// </summary>
        HmacSha1,

        /// <summary>
        /// HMAC with SHA256
        /// </summary>
        HmacSha256,

        /// <summary>
        /// HMAC with SHA384
        /// </summary>
        HmacSha384,

        /// <summary>
        /// ChaCha20Poly1305
        /// </summary>
        ChaCha20Poly1305,

        /// <summary>
        /// AES GCM with 128 bit key
        /// </summary>
        Aes128Gcm,

        /// <summary>
        /// AES GCM with 256 bit key
        /// </summary>
        Aes256Gcm
    }

    /// <summary>
    /// The symmetric ecryption algorithm used to encrypt messages.
    /// </summary>
    public enum SymmetricEncryptionAlgorithm
    {
        /// <summary>
        /// Does not apply.
        /// </summary>
        None,

        /// <summary>
        /// AES 128 bit in CBC mode
        /// </summary>
        Aes128Cbc,

        /// <summary>
        /// AES 256 bit in CBC mode
        /// </summary>
        Aes256Cbc,

        /// <summary>
        /// AES 128 bit in counter mode
        /// </summary>
        Aes128Ctr,

        /// <summary>
        /// AES 256 bit in counter mode
        /// </summary>
        Aes256Ctr,

        /// <summary>
        /// ChaCha20Poly1305
        /// </summary>
        ChaCha20Poly1305,

        /// <summary>
        /// AES 128 in GCM mode
        /// </summary>
        Aes128Gcm,

        /// <summary>
        /// AES 256 in GCM mode
        /// </summary>
        Aes256Gcm
    }
}
