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
    /// <remarks>
    /// Deliberately a sealed class rather than a static one. The operations that
    /// used to live here moved to <see cref="ISecurityPolicyRegistry"/>, and the
    /// migration shim restores the removed 1.05.378 members as static extension
    /// members on this type - which the compiler only permits for a type that is
    /// not static.
    /// </remarks>
    public sealed class SecurityPolicies
    {
        private SecurityPolicies()
        {
        }

        /// <summary>
        /// The base URI for all policy URIs.
        /// </summary>
        public const string BaseUri = "http://opcfoundation.org/UA/SecurityPolicy#";

        /// <summary>
        /// The URI for a policy that uses no security.
        /// </summary>
        public const string None = BaseUri + "None";

        /// <summary>
        /// The URI for the Basic128Rsa15 security policy.
        /// </summary>
        public const string Basic128Rsa15 = BaseUri + "Basic128Rsa15";

        /// <summary>
        /// The URI for the Basic256 security policy.
        /// </summary>
        public const string Basic256 = BaseUri + "Basic256";

        /// <summary>
        /// The URI for the Aes128_Sha256_RsaOaep security policy.
        /// </summary>
        public const string Aes128_Sha256_RsaOaep = BaseUri + "Aes128_Sha256_RsaOaep";

        /// <summary>
        /// The URI for the Basic256Sha256 security policy.
        /// </summary>
        public const string Basic256Sha256 = BaseUri + "Basic256Sha256";

        /// <summary>
        /// The URI for the Aes256_Sha256_RsaPss security policy.
        /// </summary>
        public const string Aes256_Sha256_RsaPss = BaseUri + "Aes256_Sha256_RsaPss";

        /// <summary>
        /// The URI for the RSA_DH_AES_GCM security policy.
        /// </summary>
        public const string RSA_DH_AesGcm = BaseUri + "RSA_DH_AesGcm";

        /// <summary>
        /// The URI for the RSA_DH_ChaChaPoly security policy.
        /// </summary>
        public const string RSA_DH_ChaChaPoly = BaseUri + "RSA_DH_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy.
        /// </summary>
        public const string ECC_nistP256 = BaseUri + "ECC_nistP256";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy with AES-GCM.
        /// </summary>
        public const string ECC_nistP256_AesGcm = ECC_nistP256 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_nistP256 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_nistP256_ChaChaPoly = ECC_nistP256 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy.
        /// </summary>
        public const string ECC_nistP384 = BaseUri + "ECC_nistP384";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy with AES-GCM.
        /// </summary>
        public const string ECC_nistP384_AesGcm = ECC_nistP384 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_nistP384 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_nistP384_ChaChaPoly = ECC_nistP384 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP256r1 = BaseUri + "ECC_brainpoolP256r1";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy with AES-GCM.
        /// </summary>
        public const string ECC_brainpoolP256r1_AesGcm = ECC_brainpoolP256r1 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_brainpoolP256r1 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_brainpoolP256r1_ChaChaPoly = ECC_brainpoolP256r1 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy.
        /// </summary>
        public const string ECC_brainpoolP384r1 = BaseUri + "ECC_brainpoolP384r1";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy with AES-GCM.
        /// </summary>
        public const string ECC_brainpoolP384r1_AesGcm = ECC_brainpoolP384r1 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_brainpoolP384r1 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_brainpoolP384r1_ChaChaPoly = ECC_brainpoolP384r1 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy.brainpoolP384r1_AesGcm
        /// </summary>
        public const string ECC_curve25519 = BaseUri + "ECC_curve25519";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy with AES-GCM.
        /// </summary>
        public const string ECC_curve25519_AesGcm = ECC_curve25519 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_curve25519 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_curve25519_ChaChaPoly = ECC_curve25519 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the ECC_curve448 deprecated security policy.
        /// </summary>
        public const string ECC_curve448 = BaseUri + "ECC_curve448";

        /// <summary>
        /// The URI for the ECC_curve448 security policy with AES-GCM.
        /// </summary>
        public const string ECC_curve448_AesGcm = ECC_curve448 + "_AesGcm";

        /// <summary>
        /// The URI for the ECC_curve448 security policy with ChaCha20Poly1305.
        /// </summary>
        public const string ECC_curve448_ChaChaPoly = ECC_curve448 + "_ChaChaPoly";

        /// <summary>
        /// The URI for the Https security policy.
        /// </summary>
        public const string Https = BaseUri + "Https";

        internal static bool SupportsAesGcmPolicy()
        {
#if NET8_0_OR_GREATER
            return AesGcm.IsSupported;
#else
            return false;
#endif
        }

        internal static bool SupportsChaCha20Poly1305Policy()
        {
#if NET8_0_OR_GREATER
            return ChaCha20Poly1305.IsSupported;
#else
            return false;
#endif
        }

        internal static bool SupportsCertificateType(NodeId certificateType)
        {
            return Utils.IsSupportedCertificateType(certificateType);
        }

        internal static bool UnsupportedPolicy()
        {
            return false;
        }

        internal static string GetNameFromUri(string uri)
        {
            if (uri.StartsWith(BaseUri, StringComparison.Ordinal))
            {
                return uri[BaseUri.Length..];
            }

            return uri;
        }
    }
}
