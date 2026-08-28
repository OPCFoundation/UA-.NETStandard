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
        public SecurityPolicyInfo(string uri, string? name = null)
        {
            if (string.IsNullOrEmpty(uri))
            {
                throw new ArgumentException("The URI is not a valid security policy.", nameof(uri));
            }

            Uri = uri;
            Name = name ?? SecurityPolicies.GetNameFromUri(uri);
        }

        /// <summary>
        /// Creates a copy of an existing security policy.
        /// </summary>
        /// <param name="policy">The policy to copy.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="policy"/> is <c>null</c>.
        /// </exception>
        public SecurityPolicyInfo(SecurityPolicyInfo policy)
        {
            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            Name = policy.Name;
            Uri = policy.Uri;
            IsDeprecated = policy.IsDeprecated;
            IsFipsApproved = policy.IsFipsApproved;
            SymmetricSignatureAlgorithm = policy.SymmetricSignatureAlgorithm;
            SymmetricEncryptionAlgorithm = policy.SymmetricEncryptionAlgorithm;
            AsymmetricSignatureAlgorithm = policy.AsymmetricSignatureAlgorithm;
            AsymmetricEncryptionAlgorithm = policy.AsymmetricEncryptionAlgorithm;
            MinAsymmetricKeyLength = policy.MinAsymmetricKeyLength;
            MaxAsymmetricKeyLength = policy.MaxAsymmetricKeyLength;
            KeyDerivationAlgorithm = policy.KeyDerivationAlgorithm;
            DerivedSignatureKeyLength = policy.DerivedSignatureKeyLength;
            CertificateSignatureAlgorithm = policy.CertificateSignatureAlgorithm;
            CertificateKeyFamily = policy.CertificateKeyFamily;
            CertificateKeyAlgorithm = policy.CertificateKeyAlgorithm;
            EphemeralKeyAlgorithm = policy.EphemeralKeyAlgorithm;
            CertificateThumbprintAlgorithm = policy.CertificateThumbprintAlgorithm;
            SecureChannelNonceLength = policy.SecureChannelNonceLength;
            InitializationVectorLength = policy.InitializationVectorLength;
            SymmetricSignatureLength = policy.SymmetricSignatureLength;
            SymmetricEncryptionKeyLength = policy.SymmetricEncryptionKeyLength;
            LegacySequenceNumbers = policy.LegacySequenceNumbers;
            SecureChannelEnhancements = policy.SecureChannelEnhancements;
            PlatformSupport = policy.PlatformSupport;
            SupportedCertificateTypes = policy.SupportedCertificateTypes;
            CertificateCurve = policy.CertificateCurve;
            IsDefault = policy.IsDefault;
            IsDefaultEcc = policy.IsDefaultEcc;
            IsDefaultDeprecated = policy.IsDefaultDeprecated;
        }

        /// <summary>
        /// Short name for the policy.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// The unique identifier for the policy.
        /// </summary>
        public string Uri { get; init; }

        /// <summary>
        /// Returns true if the policy is considered deprecated and should not be used for new deployments.
        /// </summary>
        public bool IsDeprecated { get; init; }

        /// <summary>
        /// Returns true when this policy can be used on the current platform.
        /// </summary>
        public Func<bool>? PlatformSupport { get; init; }

        /// <summary>
        /// The application certificate types that can be used with this policy.
        /// </summary>
        public ArrayOf<NodeId> SupportedCertificateTypes { get; init; } = [];

        /// <summary>
        /// The ECC curve associated with this policy's application certificate type.
        /// </summary>
        public ECCurve? CertificateCurve { get; init; }

        /// <summary>
        /// Returns true when this policy is included in the default RSA policy list.
        /// </summary>
        public bool IsDefault { get; init; }

        /// <summary>
        /// Returns true when this policy is included in the default ECC policy list.
        /// </summary>
        public bool IsDefaultEcc { get; init; }

        /// <summary>
        /// Returns true when this policy is included in the default deprecated policy list.
        /// </summary>
        public bool IsDefaultDeprecated { get; init; }

        /// <summary>
        /// Returns true if every algorithm the policy uses is approved for
        /// validated cryptography.
        /// </summary>
        /// <remarks>
        /// This lives with the policy rather than in a separate list so that
        /// adding a policy forces the classification to be stated next to the
        /// algorithms it is derived from. It is what
        /// <see cref="CryptoCompliance"/> filters on under
        /// <see cref="CryptoCompliancePolicy.FipsOnly"/>.
        /// <para>
        /// ChaCha20-Poly1305 is not a NIST approved algorithm. The brainpool
        /// curves are absent from SP 800-186, as are Curve25519 and Curve448.
        /// SHA-1, and therefore the P-SHA1 key derivation used by the two oldest
        /// policies, is deprecated for new signatures by SP 800-131A.
        /// </para>
        /// </remarks>
        public bool IsFipsApproved { get; init; }

        /// <summary>
        /// Gets whether the declared cryptographic metadata meets the minimum
        /// requirements for secure OPC UA communication.
        /// </summary>
        /// <remarks>
        /// This evaluates the policy metadata against the modern algorithm and
        /// key-exchange structures defined by OPC UA Part 2 and Part 6,
        /// Sections 6.8 and 6.9. It does not assess a provider implementation.
        /// </remarks>
        public bool MeetsMinimumSecurityRequirements =>
            !IsDeprecated &&
            HasStrongSymmetricEncryption() &&
            SymmetricSignatureAlgorithm is
                SymmetricSignatureAlgorithm.HmacSha256 or
                SymmetricSignatureAlgorithm.HmacSha384 or
                SymmetricSignatureAlgorithm.ChaCha20Poly1305 or
                SymmetricSignatureAlgorithm.Aes128Gcm or
                SymmetricSignatureAlgorithm.Aes256Gcm &&
            KeyDerivationAlgorithm is
                KeyDerivationAlgorithm.PSha256 or
                KeyDerivationAlgorithm.HKDFSha256 or
                KeyDerivationAlgorithm.HKDFSha384 &&
            AsymmetricSignatureAlgorithm is
                AsymmetricSignatureAlgorithm.RsaPkcs15Sha256 or
                AsymmetricSignatureAlgorithm.RsaPssSha256 or
                AsymmetricSignatureAlgorithm.EcdsaSha256 or
                AsymmetricSignatureAlgorithm.EcdsaSha384 or
                AsymmetricSignatureAlgorithm.EcdsaPure25519 or
                AsymmetricSignatureAlgorithm.EcdsaPure448 &&
            IsApprovedKeyExchange() &&
            SecureChannelNonceLength >= 32;

        /// <summary>
        /// The symmetric signature algorithm to use.
        /// </summary>
        public SymmetricSignatureAlgorithm SymmetricSignatureAlgorithm { get; init; }

        /// <summary>
        /// The symmetric encryption algorithm to use.
        /// </summary>
        public SymmetricEncryptionAlgorithm SymmetricEncryptionAlgorithm { get; init; }

        /// <summary>
        /// The asymmetric signature algorithm to use.
        /// </summary>
        public AsymmetricSignatureAlgorithm AsymmetricSignatureAlgorithm { get; init; }

        /// <summary>
        /// The symmetric encryption algorithm to use.
        /// </summary>
        public AsymmetricEncryptionAlgorithm AsymmetricEncryptionAlgorithm { get; init; }

        /// <summary>
        /// The minimum length, in bits, for an asymmetric key.
        /// </summary>
        public int MinAsymmetricKeyLength { get; init; }

        /// <summary>
        /// The maximum length, in bits, for an asymmetric key.
        /// </summary>
        public int MaxAsymmetricKeyLength { get; init; }

        /// <summary>
        /// The key derivation algorithm to use.
        /// </summary>
        public KeyDerivationAlgorithm KeyDerivationAlgorithm { get; init; }

        /// <summary>
        /// The length in bytes of the derived key used for message authentication.
        /// </summary>
        public int DerivedSignatureKeyLength { get; init; }

        /// <summary>
        /// The asymmetric signature algorithm used to sign certificates.
        /// </summary>
        public AsymmetricSignatureAlgorithm CertificateSignatureAlgorithm { get; init; }

        /// <summary>
        /// Returns algorithm family used to create asymmetric key pairs used with Certificates.
        /// </summary>
        public CertificateKeyFamily CertificateKeyFamily { get; init; }

        /// <summary>
        /// The algorithm used to create asymmetric key pairs used with Certificates.
        /// </summary>
        public CertificateKeyAlgorithm CertificateKeyAlgorithm { get; init; }

        /// <summary>
        /// The algorithm used to create asymmetric key pairs used for EphemeralKeys.
        /// </summary>
        public CertificateKeyAlgorithm EphemeralKeyAlgorithm { get; init; }

        /// <summary>
        /// The algorithm used to calculate the thumbprint of the certificate.
        /// </summary>
        public CertificateThumbprintAlgorithm CertificateThumbprintAlgorithm { get; init; }

        /// <summary>
        /// The length, in bytes, of the Nonces used when opening a SecureChannel.
        /// </summary>
        public int SecureChannelNonceLength { get; init; }

        /// <summary>
        /// The length, in bytes, of the data used to initialize the symmetric algorithm.
        /// </summary>
        public int InitializationVectorLength { get; init; }

        /// <summary>
        /// The length, in bytes, of the symmetric signature.
        /// </summary>
        public int SymmetricSignatureLength { get; init; }

        /// <summary>
        /// The length, in bytes, of the symmetric encryption key.
        /// </summary>
        public int SymmetricEncryptionKeyLength { get; init; }

        /// <summary>
        /// If TRUE, the 1024 based SequenceNumber rules apply to the SecurityPolicy.
        /// If FALSE, the 0 based SequenceNumber rules apply.
        /// </summary>
        public bool LegacySequenceNumbers { get; init; }

        /// <summary>
        /// If TRUE, the enhancements to the SecureChannel are required for the SecurityPolicy.
        /// • Channel-bound Signature calculations in CreateSession/ActivateSession;
        /// • Session transfer tokens in ActivateSession;
        /// • Chained symmetric key derivation when renewing SecureChannels.
        /// • Allow padding when using Authenticated Encryption;
        /// </summary>
        public bool SecureChannelEnhancements { get; init; }

        /// <summary>
        /// Whether the padding is required with symmetric encryption.
        /// </summary>
        public bool NoSymmetricEncryptionPadding =>
            SymmetricEncryptionAlgorithm is SymmetricEncryptionAlgorithm.Aes256Gcm or
            SymmetricEncryptionAlgorithm.Aes128Gcm or
            SymmetricEncryptionAlgorithm.ChaCha20Poly1305;

        /// <summary>
        /// Returns the derived server key data length.
        /// </summary>
        public int ServerKeyDataLength =>
             DerivedSignatureKeyLength + SymmetricEncryptionKeyLength + InitializationVectorLength;

        /// <summary>
        /// Returns the derived client key data length.
        /// </summary>
        public int ClientKeyDataLength =>
             DerivedSignatureKeyLength + SymmetricEncryptionKeyLength + InitializationVectorLength;

        /// <summary>
        /// Returns the data to be signed by the server when creating a session.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public byte[] GetUserTokenSignatureData(
            byte[]? channelThumbprint,
            byte[]? serverNonce,
            byte[]? serverCertificate,
            byte[]? serverChannelCertificate,
            byte[]? clientCertificate,
            byte[]? clientChannelCertificate,
            byte[]? clientNonce)
        {
            if (SecureChannelEnhancements)
            {
                using HashAlgorithm hash = CertificateThumbprintAlgorithm switch
                {
                    CertificateThumbprintAlgorithm.SHA256 => SHA256.Create(),
                    CertificateThumbprintAlgorithm.SHA384 => SHA384.Create(),
                    CertificateThumbprintAlgorithm.SHA512 => SHA512.Create(),
                    _ => throw new NotSupportedException()
                };

                byte[]? serverCertificateHash =
                    serverCertificate != null ? hash.ComputeHash(serverCertificate) : null;
                byte[]? serverChannelCertificateHash =
                    serverChannelCertificate != null ? hash.ComputeHash(serverChannelCertificate) : null;
                byte[]? clientCertificateHash =
                    clientCertificate != null ? hash.ComputeHash(clientCertificate) : null;
                byte[]? clientChannelCertificateHash =
                    clientChannelCertificate != null ? hash.ComputeHash(clientChannelCertificate) : null;

                return Utils.Append(
                    channelThumbprint,
                    serverNonce,
                    serverCertificateHash,
                    serverChannelCertificateHash,
                    clientCertificateHash,
                    clientChannelCertificateHash,
                    clientNonce);
            }

            return Utils.Append(
                serverCertificate,
                serverNonce);
        }

        /// <summary>
        /// Returns the data to be signed by the server when creating a session.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public byte[] GetServerSignatureData(
            byte[]? channelThumbprint,
            byte[]? clientNonce,
            byte[]? serverChannelCertificate,
            byte[]? clientCertificate,
            byte[]? clientChannelCertificate,
            byte[]? serverNonce)
        {
            if (SecureChannelEnhancements)
            {
                using HashAlgorithm hash = CertificateThumbprintAlgorithm switch
                {
                    CertificateThumbprintAlgorithm.SHA256 => SHA256.Create(),
                    CertificateThumbprintAlgorithm.SHA384 => SHA384.Create(),
                    CertificateThumbprintAlgorithm.SHA512 => SHA512.Create(),
                    _ => throw new NotSupportedException()
                };

                byte[]? serverChannelCertificateHash =
                    serverChannelCertificate != null ? hash.ComputeHash(serverChannelCertificate) : null;
                byte[]? clientChannelCertificateHash =
                    clientChannelCertificate != null ? hash.ComputeHash(clientChannelCertificate) : null;

                return Utils.Append(
                    channelThumbprint,
                    clientNonce,
                    serverChannelCertificateHash,
                    clientChannelCertificateHash,
                    serverNonce);
            }

            return Utils.Append(
                clientCertificate,
                clientNonce);
        }

        /// <summary>
        /// Returns the data to be signed by the client when creating a session.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public byte[] GetClientSignatureData(
            byte[]? channelThumbprint,
            byte[]? serverNonce,
            byte[]? serverCertificate,
            byte[]? serverChannelCertificate,
            byte[]? clientChannelCertificate,
            byte[]? clientNonce)
        {
            if (SecureChannelEnhancements)
            {
                using HashAlgorithm hash = CertificateThumbprintAlgorithm switch
                {
                    CertificateThumbprintAlgorithm.SHA256 => SHA256.Create(),
                    CertificateThumbprintAlgorithm.SHA384 => SHA384.Create(),
                    CertificateThumbprintAlgorithm.SHA512 => SHA512.Create(),
                    _ => throw new NotSupportedException()
                };

                byte[]? serverCertificateHash = serverCertificate != null ? hash.ComputeHash(serverCertificate) : null;
                byte[]? serverChannelCertificateHash = serverChannelCertificate != null ? hash.ComputeHash(serverChannelCertificate) : null;
                byte[]? clientChannelCertificateHash = clientChannelCertificate != null ? hash.ComputeHash(clientChannelCertificate) : null;

                return Utils.Append(
                    channelThumbprint,
                    serverNonce,
                    serverCertificateHash,
                    serverChannelCertificateHash,
                    clientChannelCertificateHash,
                    clientNonce);
            }

            return Utils.Append(
                serverCertificate,
                serverNonce);
        }

        /// <summary>
        /// Returns a HMAC based on the symmetric signature algorithm.
        /// </summary>
        public HMAC? CreateSignatureHmac(byte[] signingKey)
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

        private bool HasStrongSymmetricEncryption()
        {
            return SymmetricEncryptionAlgorithm is
                SymmetricEncryptionAlgorithm.Aes128Cbc or
                SymmetricEncryptionAlgorithm.Aes256Cbc or
                SymmetricEncryptionAlgorithm.Aes128Ctr or
                SymmetricEncryptionAlgorithm.Aes256Ctr or
                SymmetricEncryptionAlgorithm.ChaCha20Poly1305 or
                SymmetricEncryptionAlgorithm.Aes128Gcm or
                SymmetricEncryptionAlgorithm.Aes256Gcm &&
                SymmetricEncryptionKeyLength >= 128 / 8 &&
                InitializationVectorLength > 0;
        }

        private bool IsApprovedKeyExchange()
        {
            return CertificateKeyFamily switch
            {
                CertificateKeyFamily.RSA =>
                    CertificateKeyAlgorithm == CertificateKeyAlgorithm.RSA &&
                    MinAsymmetricKeyLength >= 2048 &&
                    IsApprovedRsaKeyExchange(),
                CertificateKeyFamily.ECC => IsApprovedEccKeyExchange(),
                _ => false
            };
        }

        private bool IsApprovedRsaKeyExchange()
        {
            if (AsymmetricSignatureAlgorithm is not
                (AsymmetricSignatureAlgorithm.RsaPkcs15Sha256 or
                AsymmetricSignatureAlgorithm.RsaPssSha256))
            {
                return false;
            }

            if (EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                return AsymmetricEncryptionAlgorithm is
                    AsymmetricEncryptionAlgorithm.RsaOaepSha1 or
                    AsymmetricEncryptionAlgorithm.RsaOaepSha256;
            }

            return EphemeralKeyAlgorithm == CertificateKeyAlgorithm.RSADH &&
                AsymmetricEncryptionAlgorithm == AsymmetricEncryptionAlgorithm.None &&
                SecureChannelEnhancements &&
                !LegacySequenceNumbers &&
                KeyDerivationAlgorithm is
                    KeyDerivationAlgorithm.HKDFSha256 or
                    KeyDerivationAlgorithm.HKDFSha384 &&
                SecureChannelNonceLength >= 384;
        }

        private bool IsApprovedEccKeyExchange()
        {
            if (AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None ||
                CertificateKeyAlgorithm != EphemeralKeyAlgorithm)
            {
                return false;
            }

            return (CertificateKeyAlgorithm, AsymmetricSignatureAlgorithm) is
                (CertificateKeyAlgorithm.NistP256, AsymmetricSignatureAlgorithm.EcdsaSha256) or
                (CertificateKeyAlgorithm.NistP384, AsymmetricSignatureAlgorithm.EcdsaSha384) or
                (CertificateKeyAlgorithm.BrainpoolP256r1, AsymmetricSignatureAlgorithm.EcdsaSha256) or
                (CertificateKeyAlgorithm.BrainpoolP384r1, AsymmetricSignatureAlgorithm.EcdsaSha384) or
                (CertificateKeyAlgorithm.Curve25519, AsymmetricSignatureAlgorithm.EcdsaPure25519) or
                (CertificateKeyAlgorithm.Curve448, AsymmetricSignatureAlgorithm.EcdsaPure448);
        }

        /// <summary>
        /// The security policy that does not provide any security.
        /// </summary>
        public static readonly SecurityPolicyInfo None = new(SecurityPolicies.None)
        {
            // No cryptography is performed, so there is nothing to withhold.
            IsFipsApproved = true,
            DerivedSignatureKeyLength = 0,
            SymmetricEncryptionKeyLength = 0,
            InitializationVectorLength = 0,
            SymmetricSignatureLength = 0,
            MinAsymmetricKeyLength = 0,
            MaxAsymmetricKeyLength = 0,
            SecureChannelNonceLength = 32,
            LegacySequenceNumbers = true,
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.None,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            CertificateKeyFamily = CertificateKeyFamily.None,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.None,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.None,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.None,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.None,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.None,
            SecureChannelEnhancements = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            IsDefault = false
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
            AsymmetricEncryptionAlgorithm = AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1,
            AsymmetricSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            CertificateKeyFamily = CertificateKeyFamily.RSA,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.RSA,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.RsaPkcs15Sha1,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.None,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.PSha1,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha1,
            IsDeprecated = true,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            SupportedCertificateTypes = [ObjectTypeIds.RsaMinApplicationCertificateType, ObjectTypeIds.RsaSha256ApplicationCertificateType],
            IsDefaultDeprecated = true
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
            IsDeprecated = true,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            SupportedCertificateTypes = [ObjectTypeIds.RsaMinApplicationCertificateType, ObjectTypeIds.RsaSha256ApplicationCertificateType],
            IsDefaultDeprecated = true
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
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
            IsDefault = true
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
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
            IsDefault = true
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
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA1,
            SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
            IsDefault = true,
            PlatformSupport = () => RsaUtils.IsSupportingRSAPssSign.Value
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure25519,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve25519,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve25519ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
        };

        /// <summary>
        /// ECC curve25519 is a required minimum security policy. It uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_curve25519_AesGcm = new(SecurityPolicies.ECC_curve25519_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve25519ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
        };

        /// <summary>
        /// ECC curve25519 is a required minimum security policy. It uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_curve25519_ChaChaPoly = new(SecurityPolicies.ECC_curve25519_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve25519ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaPure448,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.Curve448,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve448ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
        };

        /// <summary>
        /// ECC curve448 is a required minimum security policy. It uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_curve448_AesGcm = new(SecurityPolicies.ECC_curve448_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve448ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
        };

        /// <summary>
        /// ECC Curve448 is a required minimum security policy. It uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_curve448_ChaChaPoly = new(SecurityPolicies.ECC_curve448_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccCurve448ApplicationCertificateType],
            CertificateCurve = default(ECCurve),
            PlatformSupport = SecurityPolicies.UnsupportedPolicy
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP256ApplicationCertificateType, ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP256,
            IsDefaultEcc = true,
            PlatformSupport = () => SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC_nistP256_AesGcm is an ECC nistP256 variant that uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP256_AesGcm = new(SecurityPolicies.ECC_nistP256_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP256ApplicationCertificateType, ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP256,
            PlatformSupport = () => SecurityPolicies.SupportsAesGcmPolicy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC_nistP256_AesGcm is an ECC nistP256 variant that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP256_ChaChaPoly = new(SecurityPolicies.ECC_nistP256_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP256ApplicationCertificateType, ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP256,
            PlatformSupport = () => SecurityPolicies.SupportsChaCha20Poly1305Policy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType)
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP384,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP384,
            IsDefaultEcc = true,
            PlatformSupport = () => SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy that uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP384_AesGcm = new(SecurityPolicies.ECC_nistP384_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP384,
            PlatformSupport = () => SecurityPolicies.SupportsAesGcmPolicy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC nistP384 is an optional high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_nistP384_ChaChaPoly = new(SecurityPolicies.ECC_nistP384_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccNistP384ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.nistP384,
            PlatformSupport = () => SecurityPolicies.SupportsChaCha20Poly1305Policy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType)
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.NistP256,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha256,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType, ObjectTypeIds
                .EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP256r1,
            IsDefaultEcc = true,
            PlatformSupport = () => SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC_brainpoolP256r1_AesGcm is an ECC brainpoolP256 variant that uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP256r1_AesGcm = new(SecurityPolicies.ECC_brainpoolP256r1_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType, ObjectTypeIds
                .EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP256r1,
            PlatformSupport = () => SecurityPolicies.SupportsAesGcmPolicy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC_brainpoolP256_AES is an ECC brainpoolP256 variant that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP256r1_ChaChaPoly = new(SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly)
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
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha256,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP256r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha256,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.ChaCha20Poly1305,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.ChaCha20Poly1305,
            SecureChannelEnhancements = true,
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType, ObjectTypeIds
                .EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP256r1,
            PlatformSupport = () => SecurityPolicies.SupportsChaCha20Poly1305Policy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
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
            CertificateKeyFamily = CertificateKeyFamily.ECC,
            CertificateKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            CertificateSignatureAlgorithm = AsymmetricSignatureAlgorithm.EcdsaSha384,
            EphemeralKeyAlgorithm = CertificateKeyAlgorithm.BrainpoolP384r1,
            KeyDerivationAlgorithm = KeyDerivationAlgorithm.HKDFSha384,
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes256Cbc,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.HmacSha384,
            SecureChannelEnhancements = false,
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP384r1,
            IsDefaultEcc = true,
            PlatformSupport = () => SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy that uses AES-GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP384r1_AesGcm = new(SecurityPolicies.ECC_brainpoolP384r1_AesGcm)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP384r1,
            PlatformSupport = () => SecurityPolicies.SupportsAesGcmPolicy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
        };

        /// <summary>
        /// The ECC brainpoolP384r1 is an optional high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo ECC_brainpoolP384r1_ChaChaPoly = new(SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA384,
            SupportedCertificateTypes = [ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType],
            CertificateCurve = ECCurve.NamedCurves.brainpoolP384r1,
            PlatformSupport = () => SecurityPolicies.SupportsChaCha20Poly1305Policy() &&
                SecurityPolicies.SupportsCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
        };

        /// <summary>
        /// The RSA_DH_AES_GCM is an high security policy that uses AES GCM for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo RSA_DH_AesGcm = new(SecurityPolicies.RSA_DH_AesGcm)
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
            SymmetricEncryptionAlgorithm = SymmetricEncryptionAlgorithm.Aes128Gcm,
            SymmetricSignatureAlgorithm = SymmetricSignatureAlgorithm.Aes128Gcm,
            SecureChannelEnhancements = true,
            IsDeprecated = false,
            IsFipsApproved = true,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
            PlatformSupport = SecurityPolicies.SupportsAesGcmPolicy
        };

        /// <summary>
        /// The RSA_DH_ChaChaPoly is an high security policy that uses ChaCha20Poly1305 for symmetric encryption.
        /// </summary>
        public static readonly SecurityPolicyInfo RSA_DH_ChaChaPoly = new(SecurityPolicies.RSA_DH_ChaChaPoly)
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
            IsDeprecated = false,
            IsFipsApproved = false,
            CertificateThumbprintAlgorithm = CertificateThumbprintAlgorithm.SHA256,
            SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType],
            PlatformSupport = SecurityPolicies.SupportsChaCha20Poly1305Policy
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

    /// <summary>
    /// The algorithm used to generate certificate thumbprints.
    /// </summary>
    public enum CertificateThumbprintAlgorithm
    {
        /// <summary>
        /// The SHA1 algorithm. This algorithm is considered insecure.
        /// </summary>
        SHA1,

        /// <summary>
        /// The SHA256 algorithm.
        /// </summary>
        SHA256,

        /// <summary>
        /// The SHA384 algorithm.
        /// </summary>
        SHA384,

        /// <summary>
        /// The SHA512 algorithm.
        /// </summary>
        SHA512
    }
}
