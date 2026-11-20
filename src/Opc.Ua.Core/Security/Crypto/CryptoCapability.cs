// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
