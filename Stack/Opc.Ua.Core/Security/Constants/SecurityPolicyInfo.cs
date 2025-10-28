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
        /// A short name for the policy.
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
        /// Whether the padding is required with symmetric encryption.
        /// </summary>
        public bool NoSymmetricEncryptionPadding =>
            SymmetricEncryptionAlgorithm == SymmetricEncryptionAlgorithm.ChaCha20Poly1305;

        /// <summary>
        /// Returns the derived key data length in bytes as a little endian UInt16.
        /// </summary>
        public byte[] KeyDataLength =>
             BitConverter.GetBytes(DerivedSignatureKeyLength + SymmetricEncryptionKeyLength + InitializationVectorLength);

        /// <summary>
        /// Returns the derived key data length for an EncryptedSecret in bytes as a little endian UInt16.
        /// </summary>
        public byte[] KeyDataLengthForEncryptedSecret =>
             BitConverter.GetBytes(SymmetricEncryptionKeyLength + InitializationVectorLength);

        /// <summary>
        /// Returns a HMAC based on the symmetric signature algorithm.
        /// </summary>
        public HMAC CreateSignatureHmac(byte[] signingKey)
        {
            return SymmetricSignatureAlgorithm switch
            {
                SymmetricSignatureAlgorithm.HmacSha1 => new HMACSHA1(signingKey),
                SymmetricSignatureAlgorithm.HmacSha256 => new HMACSHA256(signingKey),
                SymmetricSignatureAlgorithm.HmacSha384 => new HMACSHA384(signingKey),
                _ => null
            };
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
            SecureChannelNonceLength = 0,
            LegacySequenceNumbers = false,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.None,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.None,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.None,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.None
        };

        /// <summary>
        /// The security policy that uses SHA1 and 128 bit encryption. This policy is considered insecure and should not be used for new deployments.
        /// </summary>
        public static readonly SecurityPolicyInfo Basic128Rsa15 = new(SecurityPolicies.Basic128Rsa15)
        {
            DerivedSignatureKeyLength = 128 / 8,
            SymmetricEncryptionKeyLength = 128 / 8,
            SymmetricSignatureLength = 128 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 1024,
            MaxAsymmetricKeyLength = 2048,
            SecureChannelNonceLength = 16,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
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
            SymmetricSignatureLength = 128 / 8,
            InitializationVectorLength = 128 / 8,
            MinAsymmetricKeyLength = 1024,
            MaxAsymmetricKeyLength = 2048,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaOaepSha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
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
        public static readonly SecurityPolicyInfo ECC_curve25519 = new(SecurityPolicies.ECC_curve25519)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            IsDeprecated = false
        };

        /// <summary>
        /// ECC curve448 is a required minimum security policy. It uses ChaChaPoly and 256 bit encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_curve448 = new(SecurityPolicies.ECC_curve448)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP256 is a required minimum security policy.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP256 = new(SecurityPolicies.ECC_nistP256)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP384 = new(SecurityPolicies.ECC_nistP384)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP256r1 is a required minimum security policy.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP256r1 = new(SecurityPolicies.ECC_brainpoolP256r1)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            IsDeprecated = false
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP384r1 = new(SecurityPolicies.ECC_brainpoolP384r1)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            IsDeprecated = false
        };
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
        /// Chacha20Poly1305
        /// </summary>
        ChaCha20Poly1305,

        /// <summary>
        /// AES-GCM with 128 bit tag
        /// </summary>
        Aes128Gcm
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
        /// Chacha20Poly1305
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
