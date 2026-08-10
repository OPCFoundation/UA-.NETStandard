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

namespace Opc.Ua
{
    /// <summary>
    /// States that a provider can serve a purpose, optionally narrowed to one
    /// security policy and one certificate type.
    /// </summary>
    /// <param name="Purpose">The purpose that can be served.</param>
    /// <param name="SecurityPolicyUri">
    /// The security policy this capability applies to, or <c>null</c> for any.
    /// </param>
    /// <param name="CertificateType">
    /// The certificate type this capability applies to, or
    /// <see cref="NodeId.Null"/> for any.
    /// </param>
    /// <remarks>
    /// Capabilities do double duty. They select a provider for an operation, and
    /// they decide which security policies a server can advertise: a policy is
    /// only offered when some provider can serve every purpose it needs. That is
    /// what lets a provider add support the platform does not have, rather than
    /// only being filtered out by it.
    /// </remarks>
    public readonly record struct CryptoCapability(
        CryptoPurpose Purpose,
        string? SecurityPolicyUri = null,
        NodeId CertificateType = default)
    {
        /// <summary>
        /// Whether this capability satisfies a request.
        /// </summary>
        /// <param name="purpose">The purpose being requested.</param>
        /// <param name="securityPolicyUri">
        /// The security policy in play, or <c>null</c> when it does not matter.
        /// </param>
        /// <param name="certificateType">
        /// The certificate type in play, or <see cref="NodeId.Null"/> when it
        /// does not matter.
        /// </param>
        /// <returns><c>true</c> when the request is covered.</returns>
        /// <remarks>
        /// A capability that leaves the policy or certificate type unset is a
        /// wildcard for that dimension, so a provider can claim a purpose broadly
        /// and still be overridden for one policy by a more specific registration.
        /// </remarks>
        public bool Matches(
            CryptoPurpose purpose,
            string? securityPolicyUri,
            NodeId certificateType)
        {
            if (!Purpose.Equals(purpose))
            {
                return false;
            }

            if (SecurityPolicyUri != null &&
                securityPolicyUri != null &&
                !string.Equals(SecurityPolicyUri, securityPolicyUri, StringComparison.Ordinal))
            {
                return false;
            }

            if (!CertificateType.IsNull &&
                !certificateType.IsNull &&
                CertificateType != certificateType)
            {
                return false;
            }

            return true;
        }
    }
}
