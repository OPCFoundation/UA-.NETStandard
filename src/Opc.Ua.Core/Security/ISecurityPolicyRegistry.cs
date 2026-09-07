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
    /// loading, for instance - uses <see cref="SecurityPolicies.Default"/>,
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
}
