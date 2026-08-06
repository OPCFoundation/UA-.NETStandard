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
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Decides which security policies a compliance posture permits.
    /// </summary>
    /// <remarks>
    /// Several of the policies the stack supports use algorithms that are not
    /// approved for validated cryptography. They are enabled by default, because
    /// withholding them would break deployments that use them today, so a
    /// deployment that needs validated cryptography states so and this filter
    /// withholds them.
    /// </remarks>
    public static class CryptoCompliance
    {
        /// <summary>
        /// Whether a security policy may be used under a compliance posture.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy to test.</param>
        /// <param name="policy">The compliance posture.</param>
        /// <returns>
        /// <c>false</c> only when the posture forbids the policy. An unknown
        /// policy is permitted, because this filter is not the arbiter of which
        /// policies exist.
        /// </returns>
        public static bool IsPolicyPermitted(
            string? securityPolicyUri,
            CryptoCompliancePolicy policy)
        {
            if (policy != CryptoCompliancePolicy.FipsOnly)
            {
                return true;
            }

            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return true;
            }

            return !s_notApproved.Contains(securityPolicyUri!);
        }

        /// <summary>
        /// Filters a set of security policies down to those the posture permits.
        /// </summary>
        /// <param name="securityPolicyUris">The policies to filter.</param>
        /// <param name="policy">The compliance posture.</param>
        /// <returns>The permitted policies, in the order supplied.</returns>
        public static ArrayOf<string> FilterPolicies(
            ArrayOf<string> securityPolicyUris,
            CryptoCompliancePolicy policy)
        {
            if (policy != CryptoCompliancePolicy.FipsOnly)
            {
                return securityPolicyUris;
            }

            var permitted = new List<string>();
            foreach (string uri in securityPolicyUris)
            {
                if (IsPolicyPermitted(uri, policy))
                {
                    permitted.Add(uri);
                }
            }

            return new ArrayOf<string>(permitted.ToArray());
        }

        /// <summary>
        /// The security policies whose algorithms are not approved for validated
        /// cryptography.
        /// </summary>
        /// <remarks>
        /// ChaCha20-Poly1305 is not a NIST approved algorithm. The brainpool
        /// curves are absent from SP 800-186, as is Curve25519. SHA-1, and
        /// therefore the P-SHA1 key derivation used by the two oldest policies,
        /// is deprecated for new signatures by SP 800-131A.
        /// </remarks>
        private static readonly HashSet<string> s_notApproved = new(StringComparer.Ordinal)
        {
            SecurityPolicies.Basic128Rsa15,
            SecurityPolicies.Basic256,
            SecurityPolicies.RSA_DH_ChaChaPoly,
            SecurityPolicies.ECC_nistP256_ChaChaPoly,
            SecurityPolicies.ECC_nistP384_ChaChaPoly,
            SecurityPolicies.ECC_brainpoolP256r1,
            SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
            SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly,
            SecurityPolicies.ECC_brainpoolP384r1,
            SecurityPolicies.ECC_brainpoolP384r1_AesGcm,
            SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly,
            SecurityPolicies.ECC_curve25519,
            SecurityPolicies.ECC_curve25519_AesGcm,
            SecurityPolicies.ECC_curve25519_ChaChaPoly,
            SecurityPolicies.ECC_curve448,
            SecurityPolicies.ECC_curve448_AesGcm,
            SecurityPolicies.ECC_curve448_ChaChaPoly
        };
    }
}
