/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// The set of security policies an application knows about, and the
    /// operations that read it.
    /// </summary>
    /// <remarks>
    /// Resolve this from the container to work against the policies that
    /// application registered. Code with no container in scope - configuration
    /// loading, for instance - uses <see cref="SecurityPolicyRegistry.Default"/>,
    /// which carries the built-in set.
    /// </remarks>
    public interface ISecurityPolicyRegistry
    {
        /// <summary>
        /// The registered policies, as a snapshot that does not change while it
        /// is being enumerated.
        /// </summary>
        ArrayOf<SecurityPolicyInfo> Policies { get; }

        /// <summary>
        /// Looks up a policy by URI or display name, whether or not the platform
        /// supports it.
        /// </summary>
        /// <param name="policyUriOrName">The policy URI or short name.</param>
        /// <returns>The policy, or <c>null</c> when no such policy is registered.</returns>
        SecurityPolicyInfo? Find(string policyUriOrName);

        /// <summary>
        /// Registers a security policy.
        /// </summary>
        /// <param name="securityPolicy">The security policy to register.</param>
        /// <param name="replaceExisting">Whether to deliberately replace an existing policy with the same name or URI.</param>
        /// <returns>A registration that restores the previous policy snapshot when disposed.</returns>
        IDisposable Register(SecurityPolicyInfo securityPolicy, bool replaceExisting = false);

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri.
        /// Supports both full URI and short name (without SecurityPolicies.BaseUri prefix).
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri or short name.</param>
        SecurityPolicyInfo? GetInfo(string securityPolicyUri);

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri whether
        /// or not the policy is supported on this platform.
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri or short name.</param>
        SecurityPolicyInfo? GetInfoIgnoringPlatformSupport(string securityPolicyUri);

        /// <summary>
        /// Returns the uri associated with the display name.
        /// </summary>
        /// <param name="displayName">The policy display name.</param>
        string? GetUri(string displayName);

        /// <summary>
        /// Returns a display name for a security policy uri.
        /// </summary>
        /// <param name="policyUri">The policy uri.</param>
        string? GetDisplayName(string policyUri);

        /// <summary>
        /// If a security policy is known and spelled according to the spec.
        /// </summary>
        /// <param name="policyUri">The policy uri.</param>
        bool IsValidSecurityPolicyUri(string policyUri);

        /// <summary>
        /// Returns the display names for all security policy uris including https.
        /// </summary>
        string[] GetDisplayNames();

        /// <summary>
        /// Returns the deprecated RSA security policy uri.
        /// </summary>
        string[] GetDefaultDeprecatedUris();

        /// <summary>
        /// Returns the default RSA security policy uri.
        /// </summary>
        string[] GetDefaultUris();

        /// <summary>
        /// Returns the default ECC security policy uri.
        /// </summary>
        string[] GetDefaultEccUris();

        /// <summary>
        /// Returns the policy uris that support the certificate type.
        /// </summary>
        /// <param name="certificateType">The certificate type.</param>
        ArrayOf<string> GetSupportedUrisForCertificateType(NodeId certificateType);

        /// <summary>
        /// Returns the certificate types the security policy supports.
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri.</param>
        ArrayOf<NodeId> GetCertificateTypes(string securityPolicyUri);

        /// <summary>
        /// Returns the elliptic curve for the certificate type.
        /// </summary>
        /// <param name="certificateType">The certificate type.</param>
        ECCurve? GetCurveFromCertificateTypeId(NodeId certificateType);

        /// <summary>
        /// Encrypts the text using the SecurityPolicyUri and returns the result.
        /// </summary>
        /// <param name="certificate">The certificate to encrypt for.</param>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="plainText">The text to encrypt.</param>
        EncryptedData Encrypt(
            Certificate certificate,
            string securityPolicyUri,
            ReadOnlySpan<byte> plainText);

        /// <summary>
        /// Decrypts the CipherText using the SecurityPolicyUri and returns the PlainText.
        /// </summary>
        /// <param name="certificate">The certificate holding the private key.</param>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        byte[]? Decrypt(
            Certificate certificate,
            string securityPolicyUri,
            EncryptedData dataToDecrypt);

        /// <summary>
        /// Decrypts the CipherText without occupying the calling thread while a
        /// key served over a network completes.
        /// </summary>
        /// <param name="certificate">The certificate holding the private key.</param>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        /// <param name="ct">Cancels the operation.</param>
        ValueTask<byte[]?> DecryptAsync(
            Certificate certificate,
            string securityPolicyUri,
            EncryptedData dataToDecrypt,
            CancellationToken ct = default);

        /// <summary>
        /// Signs the channel data using the SecurityPolicyUri and returns the signature.
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="signingCertificate">The certificate holding the private key.</param>
        /// <param name="secureChannelSecret">The secure channel secret, when one applies.</param>
        /// <param name="remoteCertificate">The peer certificate.</param>
        /// <param name="remoteChannelCertificate">The peer channel certificate.</param>
        /// <param name="localChannelCertificate">The local channel certificate.</param>
        /// <param name="remoteNonce">The peer nonce.</param>
        /// <param name="localNonce">The local nonce.</param>
        SignatureData CreateSignatureData(
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[]? secureChannelSecret,
            byte[]? remoteCertificate,
            byte[]? remoteChannelCertificate,
            byte[]? localChannelCertificate,
            byte[]? remoteNonce,
            byte[]? localNonce);

        /// <summary>
        /// Signs the data using the SecurityPolicyUri and returns the signature.
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="localCertificate">The certificate holding the private key.</param>
        /// <param name="dataToSign">The data to sign.</param>
        SignatureData CreateSignatureData(
            string securityPolicyUri,
            Certificate localCertificate,
            byte[] dataToSign);

        /// <summary>
        /// Signs the data using the security policy and returns the signature.
        /// </summary>
        /// <param name="securityPolicy">The security policy.</param>
        /// <param name="localCertificate">The certificate holding the private key.</param>
        /// <param name="dataToSign">The data to sign.</param>
        SignatureData CreateSignatureData(
            SecurityPolicyInfo securityPolicy,
            Certificate localCertificate,
            byte[] dataToSign);

        /// <summary>
        /// Signs the data without occupying the calling thread while a key
        /// served over a network completes.
        /// </summary>
        /// <param name="securityPolicy">The security policy.</param>
        /// <param name="localCertificate">The certificate holding the private key.</param>
        /// <param name="dataToSign">The data to sign.</param>
        /// <param name="ct">Cancels the operation.</param>
        ValueTask<SignatureData> CreateSignatureDataAsync(
            SecurityPolicyInfo securityPolicy,
            Certificate localCertificate,
            byte[] dataToSign,
            CancellationToken ct = default);

        /// <summary>
        /// Verifies a channel signature using the SecurityPolicyUri.
        /// </summary>
        /// <param name="signature">The signature to verify.</param>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="signingCertificate">The certificate to verify against.</param>
        /// <param name="secureChannelSecret">The secure channel secret, when one applies.</param>
        /// <param name="localCertificate">The local certificate.</param>
        /// <param name="localChannelCertificate">The local channel certificate.</param>
        /// <param name="remoteChannelCertificate">The peer channel certificate.</param>
        /// <param name="localNonce">The local nonce.</param>
        /// <param name="remoteNonce">The peer nonce.</param>
        bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[]? secureChannelSecret,
            byte[]? localCertificate,
            byte[]? localChannelCertificate,
            byte[]? remoteChannelCertificate,
            byte[]? localNonce,
            byte[]? remoteNonce);

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and returns true if valid.
        /// </summary>
        /// <param name="signature">The signature to verify.</param>
        /// <param name="securityPolicyUri">The policy uri.</param>
        /// <param name="signingCertificate">The certificate to verify against.</param>
        /// <param name="dataToVerify">The signed data.</param>
        bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[] dataToVerify);

        /// <summary>
        /// Verifies the signature using the security policy and returns true if valid.
        /// </summary>
        /// <param name="signature">The signature to verify.</param>
        /// <param name="securityPolicy">The security policy.</param>
        /// <param name="signingCertificate">The certificate to verify against.</param>
        /// <param name="dataToVerify">The signed data.</param>
        bool VerifySignatureData(
            SignatureData signature,
            SecurityPolicyInfo securityPolicy,
            Certificate signingCertificate,
            byte[] dataToVerify);
    }

    /// <summary>
    /// The security policies an application knows about.
    /// </summary>
    /// <remarks>
    /// Owns its own policy set, so registering a policy in one registry does not
    /// affect another. <see cref="Default"/> carries the built-in set and is what
    /// code with no container in scope uses.
    /// </remarks>
    public sealed class SecurityPolicyRegistry : ISecurityPolicyRegistry, IDisposable
    {
        /// <summary>
        /// Initializes a registry carrying the built-in policies.
        /// </summary>
        /// <param name="telemetry">
        /// Used to create the logger the security operations report through.
        /// </param>
        public SecurityPolicyRegistry(ITelemetryContext? telemetry = null)
        {
            m_logger = telemetry.CreateLogger<SecurityPolicyRegistry>();
            m_snapshot = CreateBuiltInSnapshot();
        }

        /// <summary>
        /// The registry used when none was injected.
        /// </summary>
        /// <remarks>
        /// Configuration loading and the other paths that run before a container
        /// exists resolve their policies here. It carries exactly the built-in
        /// set, so behaviour is unchanged when an application registers nothing.
        /// </remarks>
        public static SecurityPolicyRegistry Default { get; } = new();

        /// <inheritdoc/>
        public ArrayOf<SecurityPolicyInfo> Policies => m_snapshot.Policies;

        /// <inheritdoc/>
        public SecurityPolicyInfo? Find(string policyUriOrName)
        {
            return TryGetPolicy(policyUriOrName, out SecurityPolicyInfo? info) ? info : null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            IDisposable[] registrations;

            lock (m_registrationLock)
            {
                registrations = [.. m_registrations];
                m_registrations.Clear();
            }

            for (int ii = registrations.Length - 1; ii >= 0; ii--)
            {
                registrations[ii].Dispose();
            }
        }

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri.
        /// Supports both full URI and short name (without SecurityPolicies.BaseUri prefix).
        /// </summary>
        public SecurityPolicyInfo? GetInfo(string securityPolicyUri)
        {
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return SecurityPolicyInfo.None;
            }

            if (TryGetPolicy(securityPolicyUri, out SecurityPolicyInfo? info) &&
                IsPlatformSupported(info!))
            {
                return info;
            }

            return null;
        }

        /// <summary>
        /// Returns the info object associated with the SecurityPolicyUri whether
        /// or not the policy is supported on this platform.
        /// </summary>
        /// <param name="securityPolicyUri">The policy uri or short name.</param>
        /// <returns>The info object, or <c>null</c> when no such policy exists.</returns>
        /// <remarks>
        /// <see cref="GetInfo"/> answers "can this policy be used here", which is
        /// what almost every caller wants. This answers "what is this policy",
        /// which is what a caller reasoning about the policy's properties needs -
        /// compliance classification, for instance, is a property of the
        /// algorithms and does not change because a platform lacks them.
        /// </remarks>
        public SecurityPolicyInfo? GetInfoIgnoringPlatformSupport(string securityPolicyUri)
        {
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return SecurityPolicyInfo.None;
            }

            return TryGetPolicy(securityPolicyUri, out SecurityPolicyInfo? info)
                ? info
                : null;
        }

        /// <summary>
        /// Returns the uri associated with the display name. This includes http and all
        /// other supported platform security policies.
        /// </summary>
        public string? GetUri(string displayName)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;
            if (snapshot.NameToInfo.ContainsKey(displayName) &&
                IsPlatformSupported(snapshot.NameToInfo[displayName]))
            {
                return snapshot.NameToInfo[displayName].Uri;
            }

            return null;
        }

        /// <summary>
        /// Returns a display name for a security policy uri. This includes http and all
        /// other supported platform security policies.
        /// </summary>
        public string? GetDisplayName(string policyUri)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;
            if (snapshot.UriToInfo.ContainsKey(policyUri) &&
                IsPlatformSupported(snapshot.UriToInfo[policyUri]))
            {
                return snapshot.UriToInfo[policyUri].Name;
            }

            return null;
        }

        /// <summary>
        /// If a security policy is known and spelled according to the spec.
        /// </summary>
        /// <remarks>
        /// This functions returns only information if a security policy Uri is
        /// valid and existing according to the spec.
        /// It does not provide the information if the policy is supported
        /// by the application or by the platform.
        /// </remarks>
        public bool IsValidSecurityPolicyUri(string policyUri)
        {
            return m_snapshot.UriToInfo.ContainsKey(policyUri);
        }

        /// <summary>
        /// Returns the display names for all security policy uris including https.
        /// </summary>
        public string[] GetDisplayNames()
        {
            SecurityPolicySnapshot snapshot = m_snapshot;
            var names = new List<string>(snapshot.Policies.Length);

            foreach (SecurityPolicyInfo info in snapshot.Policies)
            {
                if (IsPlatformSupported(info))
                {
                    names.Add(info.Name);
                }
            }

            return [.. names];
        }

        /// <summary>
        /// Returns the deprecated RSA security policy uri.
        /// </summary>
        public string[] GetDefaultDeprecatedUris()
        {
            return GetOrderedUris(s_defaultDeprecatedPolicyUris, policy => policy.IsDefaultDeprecated);
        }

        /// <summary>
        /// Returns the default RSA security policy uri.
        /// </summary>
        public string[] GetDefaultUris()
        {
            return GetOrderedUris(s_defaultPolicyUris, policy => policy.IsDefault);
        }

        /// <summary>
        /// Returns the default ECC security policy uri.
        /// </summary>
        public string[] GetDefaultEccUris()
        {
            return GetOrderedUris(s_defaultEccPolicyUris, policy => policy.IsDefaultEcc);
        }

        /// <summary>
        /// Registers a security policy and makes it visible to the security policy lookup helpers.
        /// </summary>
        /// <param name="securityPolicy">The security policy to register.</param>
        /// <param name="replaceExisting">
        /// When <c>true</c>, an existing policy with the same URI or name is deliberately replaced until
        /// the returned registration is disposed.
        /// </param>
        /// <returns>A registration that restores the previous snapshot when disposed.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="securityPolicy"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A policy with the same URI or name already exists and <paramref name="replaceExisting"/> is <c>false</c>.
        /// </exception>
        public IDisposable Register(SecurityPolicyInfo securityPolicy, bool replaceExisting = false)
        {
            IDisposable registration = RegisterCore(securityPolicy, replaceExisting);

            lock (m_registrationLock)
            {
                m_registrations.Add(registration);
            }

            return registration;
        }

        /// <summary>
        /// Encrypts the text using the SecurityPolicyUri and returns the result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public EncryptedData Encrypt(
            Certificate certificate,
            string securityPolicyUri,
            ReadOnlySpan<byte> plainText)
        {
            var encryptedData = new EncryptedData { Algorithm = null };

            // check if nothing to do.
            if (plainText.Length == 0 || string.IsNullOrEmpty(securityPolicyUri))
            {
                encryptedData.Data = plainText.ToArray();
                return encryptedData;
            }

            // get the info object.
            // unsupported policy.
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            // check if asymmetric encryption is possible.
            if (info.AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None)
            {
                switch (info.AsymmetricEncryptionAlgorithm)
                {
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaep;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.OaepSHA1,
                            m_logger);
                        break;
                    case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                        encryptedData.Algorithm = SecurityAlgorithms.Rsa15;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.Pkcs1,
                            m_logger);
                        break;
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha256:
                        encryptedData.Algorithm = SecurityAlgorithms.RsaOaepSha256;
                        encryptedData.Data = RsaUtils.Encrypt(
                            plainText,
                            certificate,
                            RsaUtils.Padding.OaepSHA256,
                            m_logger);
                        break;
                }
            }
            else
            {
                // No asymmetric encryption is defined for this policy – return the plaintext.
                encryptedData.Data = plainText.ToArray();
            }

            return encryptedData;
        }

        /// <summary>
        /// Decrypts the CipherText using the SecurityPolicyUri and returns the PlainText.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public byte[]? Decrypt(
            Certificate certificate,
            string securityPolicyUri,
            EncryptedData? dataToDecrypt)
        {
            // check if nothing to do.
            if (dataToDecrypt == null)
            {
                return null;
            }

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return dataToDecrypt.Data;
            }

            // get the info object.
            // unsupported policy.
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            // check if asymmetric encryption is possible.
            if (info.AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None)
            {
                switch (info.AsymmetricEncryptionAlgorithm)
                {
                    case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaep)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data!),
                                certificate,
                                RsaUtils.Padding.OaepSHA1,
                                m_logger);
                        }
                        break;
                    case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.Rsa15)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data!),
                                certificate,
                                RsaUtils.Padding.Pkcs1,
                                m_logger);
                        }
                        break;
                    default:
                        if (dataToDecrypt.Algorithm == SecurityAlgorithms.RsaOaepSha256)
                        {
                            return RsaUtils.Decrypt(
                                new ArraySegment<byte>(dataToDecrypt.Data!),
                                certificate,
                                RsaUtils.Padding.OaepSHA256,
                                m_logger);
                        }
                        break;
                }
            }

            if (string.IsNullOrEmpty(dataToDecrypt.Algorithm))
            {
                return dataToDecrypt.Data;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "Unexpected encryption algorithm : {0}",
                dataToDecrypt.Algorithm!);
        }

        /// <summary>
        /// Decrypts the CipherText without occupying the calling thread when the
        /// private key is served over a network.
        /// </summary>
        /// <param name="certificate">
        /// The certificate whose private key decrypts.
        /// </param>
        /// <param name="securityPolicyUri">The security policy in play.</param>
        /// <param name="dataToDecrypt">The data to decrypt.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The plain text.</returns>
        /// <exception cref="ServiceResultException"></exception>
        /// <remarks>
        /// The returned task completes synchronously unless the private key
        /// declares <see cref="IAsyncRsaKey"/>, so a software key behaves exactly
        /// as it does through
        /// <see cref="Decrypt(Certificate, string, EncryptedData)"/>.
        /// </remarks>
        public ValueTask<byte[]?> DecryptAsync(
            Certificate certificate,
            string securityPolicyUri,
            EncryptedData? dataToDecrypt,
            CancellationToken ct = default)
        {
            if (dataToDecrypt == null)
            {
                return new ValueTask<byte[]?>((byte[]?)null);
            }

            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return new ValueTask<byte[]?>(dataToDecrypt.Data);
            }

            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            if (info.AsymmetricEncryptionAlgorithm != AsymmetricEncryptionAlgorithm.None &&
                TryGetDecryptionPadding(
                    info.AsymmetricEncryptionAlgorithm,
                    dataToDecrypt.Algorithm,
                    out RsaUtils.Padding padding))
            {
                return RsaUtils.DecryptAsync(
                    new ArraySegment<byte>(dataToDecrypt.Data!),
                    certificate,
                    padding,
                    m_logger,
                    ct)!;
            }

            if (string.IsNullOrEmpty(dataToDecrypt.Algorithm))
            {
                return new ValueTask<byte[]?>(dataToDecrypt.Data);
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "Unexpected encryption algorithm : {0}",
                dataToDecrypt.Algorithm!);
        }

        /// <summary>
        /// Maps the policy's encryption algorithm and the algorithm named on the
        /// data onto the padding to undo.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the two do not agree, in which case the caller falls
        /// through to its existing handling rather than decrypting with the wrong
        /// padding.
        /// </returns>
        private static bool TryGetDecryptionPadding(
            AsymmetricEncryptionAlgorithm policyAlgorithm,
            string? dataAlgorithm,
            out RsaUtils.Padding padding)
        {
            switch (policyAlgorithm)
            {
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1
                    when dataAlgorithm == SecurityAlgorithms.RsaOaep:
                    padding = RsaUtils.Padding.OaepSHA1;
                    return true;
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1
                    when dataAlgorithm == SecurityAlgorithms.Rsa15:
                    padding = RsaUtils.Padding.Pkcs1;
                    return true;
                case AsymmetricEncryptionAlgorithm.RsaOaepSha1:
                case AsymmetricEncryptionAlgorithm.RsaPkcs15Sha1:
                    padding = default;
                    return false;
                default:
                    if (dataAlgorithm == SecurityAlgorithms.RsaOaepSha256)
                    {
                        padding = RsaUtils.Padding.OaepSHA256;
                        return true;
                    }
                    padding = default;
                    return false;
            }
        }

        /// <summary>
        /// Creates a signature using the security enhancements if required by the SecurityPolicy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public SignatureData CreateSignatureData(
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[]? secureChannelSecret,
            byte[]? remoteCertificate,
            byte[]? remoteChannelCertificate,
            byte[]? localChannelCertificate,
            byte[]? remoteNonce,
            byte[]? localNonce)
        {
            var signatureData = new SignatureData();

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return signatureData;
            }

            // get the info object.
            // unsupported policy.
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            // create the data to sign.
            byte[] dataToSign = info.SecureChannelEnhancements
                ? Utils.Append(
                    secureChannelSecret ?? [],
                    remoteCertificate ?? [],
                    remoteChannelCertificate ?? [],
                    localChannelCertificate ?? [],
                    remoteNonce ?? [],
                    localNonce ?? [])
                :
                  Utils.Append(
                    remoteCertificate ?? [],
                    remoteNonce);

            return CreateSignatureData(info, signingCertificate, dataToSign);
        }

        /// <summary>
        /// Creates a signature on the data provided using the SecurityPolicy.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// <paramref name="securityPolicyUri"/> is not a supported
        /// security policy.
        /// </exception>
        public SignatureData CreateSignatureData(
           string securityPolicyUri,
           Certificate localCertificate,
           byte[] dataToSign)
        {
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);
            return CreateSignatureData(info, localCertificate, dataToSign);
        }

        /// <summary>
        /// Creates a signature on the data provided using the SecurityPolicy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public SignatureData CreateSignatureData(
           SecurityPolicyInfo securityPolicy,
           Certificate localCertificate,
           byte[] dataToSign)
        {
            var signatureData = new SignatureData();

            // sign data.
            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha1;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    signatureData.Algorithm = null;
                    break;
                case AsymmetricSignatureAlgorithm.None:
                    signatureData.Algorithm = null;
                    signatureData.Signature = default;
                    return signatureData;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicy.Uri);
            }

            if (securityPolicy.SecureChannelEnhancements)
            {
                signatureData.Signature = default;
            }

            signatureData.Signature = CryptoUtils.Sign(
                new ArraySegment<byte>(dataToSign),
                localCertificate,
                securityPolicy.AsymmetricSignatureAlgorithm).ToByteString();

            return signatureData;
        }

        /// <summary>
        /// Creates a signature on the data provided without occupying the calling
        /// thread when the private key is served over a network.
        /// </summary>
        /// <param name="securityPolicy">The security policy in play.</param>
        /// <param name="localCertificate">
        /// The certificate whose private key signs.
        /// </param>
        /// <param name="dataToSign">The data to sign.</param>
        /// <param name="ct">Cancels the operation.</param>
        /// <returns>The signature.</returns>
        /// <exception cref="ServiceResultException"></exception>
        /// <remarks>
        /// The returned task completes synchronously unless the private key
        /// declares <see cref="IAsyncRsaKey"/> or <see cref="IAsyncEcdsaKey"/>,
        /// so a software key behaves exactly as it does through
        /// <see cref="CreateSignatureData(SecurityPolicyInfo, Certificate, byte[])"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<SignatureData> CreateSignatureDataAsync(
            SecurityPolicyInfo securityPolicy,
            Certificate localCertificate,
            byte[] dataToSign,
            CancellationToken ct = default)
        {
            if (securityPolicy is null)
            {
                throw new ArgumentNullException(nameof(securityPolicy));
            }

            var signatureData = new SignatureData();

            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha1;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaSha256;
                    break;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    signatureData.Algorithm = SecurityAlgorithms.RsaPssSha256;
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:
                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    signatureData.Algorithm = null;
                    break;
                case AsymmetricSignatureAlgorithm.None:
                    signatureData.Algorithm = null;
                    signatureData.Signature = default;
                    return signatureData;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityPolicyRejected,
                        "Unsupported security policy: {0}",
                        securityPolicy.Uri);
            }

            byte[]? signature = await CryptoUtils
                .SignAsync(
                    new ArraySegment<byte>(dataToSign),
                    localCertificate,
                    securityPolicy.AsymmetricSignatureAlgorithm,
                    ct)
                .ConfigureAwait(false);

            signatureData.Signature = signature.ToByteString();

            return signatureData;
        }

        /// <summary>
        /// Creates a signature using the security enhancements if required by the SecurityPolicy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[]? secureChannelSecret,
            byte[]? localCertificate,
            byte[]? localChannelCertificate,
            byte[]? remoteChannelCertificate,
            byte[]? localNonce,
            byte[]? remoteNonce)
        {
            _ = new SignatureData();

            // nothing more to do if no encryption.
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return true;
            }

            // get the info object.
            // unsupported policy.
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);

            // create the data to sign.
            byte[] dataToVerify = info.SecureChannelEnhancements
                ? Utils.Append(
                    secureChannelSecret ?? [],
                    localCertificate ?? [],
                    localChannelCertificate ?? [],
                    remoteChannelCertificate ?? [],
                    localNonce ?? [],
                    remoteNonce ?? [])
                :
                  Utils.Append(
                    localCertificate ?? [],
                    localNonce);

            return VerifySignatureData(signature, info, signingCertificate, dataToVerify);
        }

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public bool VerifySignatureData(
            SignatureData signature,
            string securityPolicyUri,
            Certificate signingCertificate,
            byte[] dataToVerify)
        {
            SecurityPolicyInfo info = GetInfo(securityPolicyUri) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityPolicyRejected,
                    "Unsupported security policy: {0}",
                    securityPolicyUri);
            return VerifySignatureData(signature, info, signingCertificate, dataToVerify);
        }

        /// <summary>
        /// Verifies the signature using the SecurityPolicyUri and return true if valid.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public bool VerifySignatureData(
            SignatureData signature,
            SecurityPolicyInfo securityPolicy,
            Certificate signingCertificate,
            byte[] dataToVerify)
        {
            // check if nothing to do.
            if (signature == null)
            {
                return true;
            }

            // sign data.
            switch (securityPolicy.AsymmetricSignatureAlgorithm)
            {
                // always accept signatures if security is not used.
                case AsymmetricSignatureAlgorithm.None:
                    return true;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha1:
                    if (signature.Algorithm == SecurityAlgorithms.RsaSha1)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            signingCertificate,
                            HashAlgorithmName.SHA1,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                case AsymmetricSignatureAlgorithm.RsaPkcs15Sha256:
                    if (string.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == SecurityAlgorithms.RsaSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            signingCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1);
                    }
                    break;
                case AsymmetricSignatureAlgorithm.RsaPssSha256:
                    if (string.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == SecurityAlgorithms.RsaPssSha256)
                    {
                        return RsaUtils.Rsa_Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            signingCertificate,
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pss);
                    }
                    break;
                case AsymmetricSignatureAlgorithm.EcdsaSha256:

                case AsymmetricSignatureAlgorithm.EcdsaSha384:
                    if (string.IsNullOrEmpty(signature.Algorithm) || signature.Algorithm == securityPolicy.Uri)
                    {
                        return CryptoUtils.Verify(
                            new ArraySegment<byte>(dataToVerify),
                            signature.Signature.ToArray(),
                            signingCertificate,
                            securityPolicy.AsymmetricSignatureAlgorithm);
                    }

                    break;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadSecurityChecksFailed,
                "Unexpected SignatureData algorithm: {0}",
                signature.Algorithm ?? string.Empty);
        }

        private bool TryGetPolicy(string policyUriOrName, out SecurityPolicyInfo? info)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;

            if (snapshot.UriToInfo.ContainsKey(policyUriOrName))
            {
                info = snapshot.UriToInfo[policyUriOrName];
                return true;
            }

            if (snapshot.NameToInfo.ContainsKey(policyUriOrName))
            {
                info = snapshot.NameToInfo[policyUriOrName];
                return true;
            }

            info = null;
            return false;
        }

        private static bool IsPlatformSupported(SecurityPolicyInfo policy)
        {
            return policy.PlatformSupport?.Invoke() ?? true;
        }

        private string[] GetOrderedUris(string[] builtInOrder, Func<SecurityPolicyInfo, bool> predicate)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;
            var defaultUris = new List<string>();

            foreach (string policyUri in builtInOrder)
            {
                if (snapshot.UriToInfo.ContainsKey(policyUri))
                {
                    SecurityPolicyInfo policy = snapshot.UriToInfo[policyUri];
                    if (predicate(policy) && IsPlatformSupported(policy))
                    {
                        defaultUris.Add(policy.Uri);
                    }
                }
            }

            foreach (SecurityPolicyInfo policy in snapshot.Policies)
            {
                if (!Contains(defaultUris, policy.Uri) &&
                    predicate(policy) &&
                    IsPlatformSupported(policy))
                {
                    defaultUris.Add(policy.Uri);
                }
            }

            return [.. defaultUris];
        }

        private static bool ContainsCertificateType(SecurityPolicyInfo policy, NodeId certificateType)
        {
            foreach (NodeId supportedCertificateType in policy.SupportedCertificateTypes)
            {
                if (supportedCertificateType == certificateType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public ArrayOf<string> GetSupportedUrisForCertificateType(NodeId certificateType)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;
            var securityPolicies = new List<string>();

            foreach (string policyUri in GetBuiltInPolicyOrder(certificateType))
            {
                if (snapshot.UriToInfo.ContainsKey(policyUri))
                {
                    SecurityPolicyInfo policy = snapshot.UriToInfo[policyUri];
                    if (IsPlatformSupported(policy))
                    {
                        securityPolicies.Add(policy.Uri);
                    }
                }
            }

            foreach (SecurityPolicyInfo policy in snapshot.Policies)
            {
                if (Contains(securityPolicies, policy.Uri))
                {
                    continue;
                }

                if (!IsPlatformSupported(policy))
                {
                    continue;
                }

                if (certificateType.IsNull)
                {
                    if (!policy.IsDeprecated &&
                        policy.CertificateKeyFamily == CertificateKeyFamily.RSA &&
                        ContainsCertificateType(policy, ObjectTypeIds.RsaSha256ApplicationCertificateType))
                    {
                        securityPolicies.Add(policy.Uri);
                    }

                    continue;
                }

                if (ContainsCertificateType(policy, certificateType))
                {
                    securityPolicies.Add(policy.Uri);
                }
                else if (certificateType == ObjectTypeIds.ApplicationCertificateType &&
                    policy.CertificateKeyFamily == CertificateKeyFamily.RSA &&
                    (ContainsCertificateType(policy, ObjectTypeIds.RsaSha256ApplicationCertificateType) ||
                        ContainsCertificateType(policy, ObjectTypeIds.RsaMinApplicationCertificateType)))
                {
                    securityPolicies.Add(policy.Uri);
                }
            }

            return securityPolicies.ToArrayOf();
        }

        private static string[] GetBuiltInPolicyOrder(NodeId certificateType)
        {
            if (certificateType.IsNull)
            {
                return s_defaultCertificatePolicyUris;
            }

            if (certificateType == ObjectTypeIds.RsaMinApplicationCertificateType)
            {
                return s_defaultDeprecatedPolicyUris;
            }

            if (certificateType == ObjectTypeIds.ApplicationCertificateType ||
                certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                return s_defaultRsaCertificatePolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
            {
                return s_eccNistP256PolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccNistP384ApplicationCertificateType)
            {
                return s_eccNistP384PolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                return s_eccBrainpoolP256r1PolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                return s_eccBrainpoolP384r1PolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccCurve25519ApplicationCertificateType)
            {
                return s_eccCurve25519PolicyUris;
            }

            if (certificateType == ObjectTypeIds.EccCurve448ApplicationCertificateType)
            {
                return s_eccCurve448PolicyUris;
            }

            return [];
        }

        private static bool Contains(List<string> values, string value)
        {
            foreach (string item in values)
            {
                if (item.Equals(value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public ECCurve? GetCurveFromCertificateTypeId(NodeId certificateType)
        {
            SecurityPolicySnapshot snapshot = m_snapshot;

            if (certificateType == ObjectTypeIds.EccApplicationCertificateType)
            {
                return ECCurve.NamedCurves.nistP256;
            }

            foreach (SecurityPolicyInfo policy in snapshot.Policies)
            {
                if (policy.CertificateCurve.HasValue &&
                    policy.SupportedCertificateTypes.Count > 0 &&
                    policy.SupportedCertificateTypes[0] == certificateType)
                {
                    return policy.CertificateCurve.Value;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> GetCertificateTypes(string securityPolicyUri)
        {
            return TryGetPolicy(securityPolicyUri, out SecurityPolicyInfo? info)
                ? info!.SupportedCertificateTypes
                : [];
        }

        private SecurityPolicyRegistration RegisterCore(SecurityPolicyInfo securityPolicy, bool replaceExisting)
        {
            if (securityPolicy is null)
            {
                throw new ArgumentNullException(nameof(securityPolicy));
            }

            lock (m_registrationLock)
            {
                SecurityPolicySnapshot snapshot = m_snapshot;
                var policies = new List<SecurityPolicyInfo>(snapshot.Policies);
                SecurityPolicyInfo? previous = null;
                int previousIndex = -1;

                for (int ii = 0; ii < policies.Count; ii++)
                {
                    SecurityPolicyInfo existing = policies[ii];
                    if (existing.Uri.Equals(securityPolicy.Uri, StringComparison.Ordinal) ||
                        existing.Name.Equals(securityPolicy.Name, StringComparison.Ordinal))
                    {
                        if (!replaceExisting)
                        {
                            throw new InvalidOperationException(
                                "A security policy with the same URI or name is already registered.");
                        }

                        previous = existing;
                        previousIndex = ii;
                        break;
                    }
                }

                if (previousIndex >= 0)
                {
                    policies[previousIndex] = securityPolicy;
                }
                else
                {
                    policies.Add(securityPolicy);
                }

                m_snapshot = SecurityPolicySnapshot.Create([.. policies]);

                return new SecurityPolicyRegistration(this, securityPolicy, previous, previousIndex);
            }
        }

        private void Unregister(SecurityPolicyInfo policy, SecurityPolicyInfo? previous, int previousIndex)
        {
            lock (m_registrationLock)
            {
                SecurityPolicySnapshot snapshot = m_snapshot;
                var policies = new List<SecurityPolicyInfo>(snapshot.Policies);

                for (int ii = 0; ii < policies.Count; ii++)
                {
                    if (!policies[ii].Uri.Equals(policy.Uri, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!ReferenceEquals(policies[ii], policy))
                    {
                        return;
                    }

                    if (previous != null && previousIndex >= 0)
                    {
                        policies[ii] = previous;
                    }
                    else
                    {
                        policies.RemoveAt(ii);
                    }

                    m_snapshot = SecurityPolicySnapshot.Create([.. policies]);
                    return;
                }
            }
        }

        private static SecurityPolicySnapshot CreateBuiltInSnapshot()
        {
            return SecurityPolicySnapshot.Create(
            [
                SecurityPolicyInfo.None,
                SecurityPolicyInfo.Basic128Rsa15,
                SecurityPolicyInfo.Basic256,
                SecurityPolicyInfo.Aes128_Sha256_RsaOaep,
                SecurityPolicyInfo.Basic256Sha256,
                SecurityPolicyInfo.Aes256_Sha256_RsaPss,
                SecurityPolicyInfo.RSA_DH_AesGcm,
                SecurityPolicyInfo.RSA_DH_ChaChaPoly,
                SecurityPolicyInfo.ECC_nistP256,
                SecurityPolicyInfo.ECC_nistP256_AesGcm,
                SecurityPolicyInfo.ECC_nistP256_ChaChaPoly,
                SecurityPolicyInfo.ECC_nistP384,
                SecurityPolicyInfo.ECC_nistP384_AesGcm,
                SecurityPolicyInfo.ECC_nistP384_ChaChaPoly,
                SecurityPolicyInfo.ECC_brainpoolP256r1,
                SecurityPolicyInfo.ECC_brainpoolP256r1_AesGcm,
                SecurityPolicyInfo.ECC_brainpoolP256r1_ChaChaPoly,
                SecurityPolicyInfo.ECC_brainpoolP384r1,
                SecurityPolicyInfo.ECC_brainpoolP384r1_AesGcm,
                SecurityPolicyInfo.ECC_brainpoolP384r1_ChaChaPoly,
                SecurityPolicyInfo.ECC_curve25519,
                SecurityPolicyInfo.ECC_curve25519_AesGcm,
                SecurityPolicyInfo.ECC_curve25519_ChaChaPoly,
                SecurityPolicyInfo.ECC_curve448,
                SecurityPolicyInfo.ECC_curve448_AesGcm,
                SecurityPolicyInfo.ECC_curve448_ChaChaPoly
            ]);
        }

        private readonly Lock m_registrationLock = new();
        private readonly List<IDisposable> m_registrations = [];
        private readonly ILogger m_logger;

        private volatile SecurityPolicySnapshot m_snapshot;

        private static readonly string[] s_defaultPolicyUris =
        [
            SecurityPolicies.Basic256Sha256,
            SecurityPolicies.Aes128_Sha256_RsaOaep,
            SecurityPolicies.Aes256_Sha256_RsaPss
        ];

        private static readonly string[] s_defaultDeprecatedPolicyUris =
        [
            SecurityPolicies.Basic128Rsa15,
            SecurityPolicies.Basic256
        ];

        private static readonly string[] s_defaultEccPolicyUris =
        [
            SecurityPolicies.ECC_nistP256,
            SecurityPolicies.ECC_nistP384,
            SecurityPolicies.ECC_brainpoolP256r1,
            SecurityPolicies.ECC_brainpoolP384r1
        ];

        private static readonly string[] s_defaultCertificatePolicyUris =
        [
            SecurityPolicies.Basic256Sha256,
            SecurityPolicies.Aes128_Sha256_RsaOaep,
            SecurityPolicies.Aes256_Sha256_RsaPss,
            SecurityPolicies.RSA_DH_AesGcm,
            SecurityPolicies.RSA_DH_ChaChaPoly
        ];

        private static readonly string[] s_defaultRsaCertificatePolicyUris =
        [
            SecurityPolicies.Basic256Sha256,
            SecurityPolicies.Aes128_Sha256_RsaOaep,
            SecurityPolicies.Aes256_Sha256_RsaPss,
            SecurityPolicies.RSA_DH_AesGcm,
            SecurityPolicies.RSA_DH_ChaChaPoly,
            SecurityPolicies.Basic128Rsa15,
            SecurityPolicies.Basic256
        ];

        private static readonly string[] s_eccNistP256PolicyUris =
        [
            SecurityPolicies.ECC_nistP256,
            SecurityPolicies.ECC_nistP256_AesGcm,
            SecurityPolicies.ECC_nistP256_ChaChaPoly
        ];

        private static readonly string[] s_eccNistP384PolicyUris =
        [
            SecurityPolicies.ECC_nistP256,
            SecurityPolicies.ECC_nistP256_AesGcm,
            SecurityPolicies.ECC_nistP256_ChaChaPoly,
            SecurityPolicies.ECC_nistP384,
            SecurityPolicies.ECC_nistP384_AesGcm,
            SecurityPolicies.ECC_nistP384_ChaChaPoly
        ];

        private static readonly string[] s_eccBrainpoolP256r1PolicyUris =
        [
            SecurityPolicies.ECC_brainpoolP256r1,
            SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
            SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly
        ];

        private static readonly string[] s_eccBrainpoolP384r1PolicyUris =
        [
            SecurityPolicies.ECC_brainpoolP256r1,
            SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
            SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly,
            SecurityPolicies.ECC_brainpoolP384r1,
            SecurityPolicies.ECC_brainpoolP384r1_AesGcm,
            SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly
        ];

        private static readonly string[] s_eccCurve25519PolicyUris =
        [
            SecurityPolicies.ECC_curve25519,
            SecurityPolicies.ECC_curve25519_AesGcm,
            SecurityPolicies.ECC_curve25519_ChaChaPoly
        ];

        private static readonly string[] s_eccCurve448PolicyUris =
        [
            SecurityPolicies.ECC_curve448,
            SecurityPolicies.ECC_curve448_AesGcm,
            SecurityPolicies.ECC_curve448_ChaChaPoly
        ];

        private sealed class SecurityPolicyRegistration : IDisposable
        {
            public SecurityPolicyRegistration(
                SecurityPolicyRegistry owner,
                SecurityPolicyInfo policy,
                SecurityPolicyInfo? previous,
                int previousIndex)
            {
                m_owner = owner;
                m_policy = policy;
                m_previous = previous;
                m_previousIndex = previousIndex;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    m_owner.Unregister(m_policy, m_previous, m_previousIndex);
                }
            }

            private readonly SecurityPolicyRegistry m_owner;
            private readonly SecurityPolicyInfo m_policy;
            private readonly SecurityPolicyInfo? m_previous;
            private readonly int m_previousIndex;
            private int m_disposed;
        }

        private sealed class SecurityPolicySnapshot
        {
            private SecurityPolicySnapshot(
                SecurityPolicyInfo[] policies,
                IReadOnlyDictionary<string, SecurityPolicyInfo> nameToInfo,
                IReadOnlyDictionary<string, SecurityPolicyInfo> uriToInfo)
            {
                Policies = policies;
                NameToInfo = nameToInfo;
                UriToInfo = uriToInfo;
            }

            public SecurityPolicyInfo[] Policies { get; }

            public IReadOnlyDictionary<string, SecurityPolicyInfo> NameToInfo { get; }

            public IReadOnlyDictionary<string, SecurityPolicyInfo> UriToInfo { get; }

            public static SecurityPolicySnapshot Create(SecurityPolicyInfo[] policies)
            {
                var nameToInfo = new Dictionary<string, SecurityPolicyInfo>(StringComparer.Ordinal);
                var uriToInfo = new Dictionary<string, SecurityPolicyInfo>(StringComparer.Ordinal);

                foreach (SecurityPolicyInfo policy in policies)
                {
                    nameToInfo.Add(policy.Name, policy);
                    uriToInfo.Add(policy.Uri, policy);
                }

#if NET8_0_OR_GREATER
                return new SecurityPolicySnapshot(
                    policies,
                    nameToInfo.ToFrozenDictionary(StringComparer.Ordinal),
                    uriToInfo.ToFrozenDictionary(StringComparer.Ordinal));
#else
                return new SecurityPolicySnapshot(
                    policies,
                    new ReadOnlyDictionary<string, SecurityPolicyInfo>(nameToInfo),
                    new ReadOnlyDictionary<string, SecurityPolicyInfo>(uriToInfo));
#endif
            }
        }
    }
}
