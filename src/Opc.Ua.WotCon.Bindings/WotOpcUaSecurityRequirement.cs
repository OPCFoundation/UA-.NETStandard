/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// One conjunction of OPC UA channel, user identity and automatic-selection
    /// constraints. An exact mode or policy must match, not merely be stronger.
    /// </summary>
    public sealed class WotOpcUaSecurityRequirement
    {
        /// <summary>
        /// Initializes exact channel constraints using a complete standard policy URI.
        /// A null member leaves that dimension unconstrained.
        /// </summary>
        /// <param name="securityMode">The security mode used to initialize the instance.</param>
        /// <param name="securityPolicyUri">The security policy URI used to initialize the instance.</param>
        public WotOpcUaSecurityRequirement(MessageSecurityMode? securityMode, string? securityPolicyUri)
            : this(securityMode, securityPolicyUri, null)
        {
        }

        /// <summary>
        /// Initializes exact channel and user-identity constraints.
        /// </summary>
        /// <param name="securityMode">The security mode used to initialize the instance.</param>
        /// <param name="securityPolicyUri">The security policy URI used to initialize the instance.</param>
        /// <param name="userIdentityToken">The user identity token used to initialize the instance.</param>
        public WotOpcUaSecurityRequirement(
            MessageSecurityMode? securityMode, string? securityPolicyUri, UserTokenType? userIdentityToken)
            : this(securityMode, securityPolicyUri, userIdentityToken, null)
        {
        }

        /// <summary>
        /// Initializes one exact alternative and its optional automatic-selection floor.
        /// </summary>
        /// <param name="securityMode">The security mode used to initialize the instance.</param>
        /// <param name="securityPolicyUri">The security policy URI used to initialize the instance.</param>
        /// <param name="userIdentityToken">The user identity token used to initialize the instance.</param>
        /// <param name="minimumSecurity">The minimum security used to initialize the instance.</param>
        public WotOpcUaSecurityRequirement(
            MessageSecurityMode? securityMode,
            string? securityPolicyUri,
            UserTokenType? userIdentityToken,
            WotSecurityFloor? minimumSecurity)
            : this(securityMode, securityPolicyUri, userIdentityToken, minimumSecurity, null)
        {
        }

        /// <summary>
        /// Initializes constraints and the secret-free scheme reference used to acquire an issued token.
        /// </summary>
        /// <param name="securityMode">The security mode used to initialize the instance.</param>
        /// <param name="securityPolicyUri">The security policy URI used to initialize the instance.</param>
        /// <param name="userIdentityToken">The user identity token used to initialize the instance.</param>
        /// <param name="minimumSecurity">The minimum security used to initialize the instance.</param>
        /// <param name="issueTokenReference">The issue token reference used to initialize the instance.</param>
        public WotOpcUaSecurityRequirement(
            MessageSecurityMode? securityMode,
            string? securityPolicyUri,
            UserTokenType? userIdentityToken,
            WotSecurityFloor? minimumSecurity,
            WotCredentialReference? issueTokenReference)
        {
            if (securityMode is { } mode && !WotBindingConformance.IsSecurityMode(mode.ToString()))
            {
                throw new ArgumentOutOfRangeException(nameof(securityMode));
            }
            if (issueTokenReference is not null && userIdentityToken != UserTokenType.IssuedToken)
            {
                throw new ArgumentException(
                    "Only IssuedToken authentication can reference a token acquisition scheme.",
                    nameof(issueTokenReference));
            }
            if (securityPolicyUri is not null && GetSecurityPolicyName(securityPolicyUri).Length == 0)
            {
                throw new ArgumentException(
                    "The policy must be a complete standard policy URI.", nameof(securityPolicyUri));
            }
            if (userIdentityToken.HasValue && userIdentityToken.Value is not (
                UserTokenType.Anonymous or UserTokenType.UserName or
                UserTokenType.Certificate or UserTokenType.IssuedToken))
            {
                throw new ArgumentOutOfRangeException(nameof(userIdentityToken));
            }
            SecurityMode = securityMode;
            SecurityPolicyUri = securityPolicyUri;
            UserIdentityToken = userIdentityToken;
            MinimumSecurity = minimumSecurity;
            IssueTokenReference = issueTokenReference;
        }

        /// <summary>
        /// Gets the exact mode, or null if no exact mode is required.
        /// </summary>
        public MessageSecurityMode? SecurityMode { get; }

        /// <summary>
        /// Gets the exact policy URI, or null if no exact policy is required.
        /// </summary>
        public string? SecurityPolicyUri { get; }

        /// <summary>
        /// Gets the required user identity token kind, or null if it is unconstrained.
        /// </summary>
        public UserTokenType? UserIdentityToken { get; }

        /// <summary>
        /// Gets the floor that applies within this alternative only.
        /// </summary>
        public WotSecurityFloor? MinimumSecurity { get; }

        /// <summary>
        /// Gets the token-acquisition scheme reference for out-of-band credential resolution.
        /// This is a scheme name, not an issuer URL or secret.
        /// </summary>
        public WotCredentialReference? IssueTokenReference { get; }

        /// <summary>
        /// Gets whether the security mode, policy, and minimum security requirement are mutually compatible.
        /// </summary>
        internal bool IsConsistent =>
            (!SecurityMode.HasValue || SecurityPolicyUri is null ||
             ((SecurityMode == MessageSecurityMode.None) ==
              (SecurityPolicyUri == PolicyPrefix + "None"))) &&
            (MinimumSecurity is null || MinimumSecurity.Permits(
                SecurityMode?.ToString() ?? MinimumSecurity.SecurityMode,
                SecurityPolicyUri is null ? MinimumSecurity.SecurityPolicy : GetSecurityPolicyName(SecurityPolicyUri)));

        /// <summary>
        /// Determines whether an endpoint satisfies every exact channel constraint.
        /// </summary>
        /// <param name="endpoint">The endpoint description.</param>
        public bool Satisfies(EndpointDescription endpoint)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            return IsConsistent && (!SecurityMode.HasValue || endpoint.SecurityMode == SecurityMode.Value) &&
                (SecurityPolicyUri is null ||
                 string.Equals(endpoint.SecurityPolicyUri, SecurityPolicyUri, StringComparison.Ordinal)) &&
                (MinimumSecurity is null || MinimumSecurity.Permits(
                    endpoint.SecurityMode.ToString(), GetSecurityPolicyName(endpoint.SecurityPolicyUri)));
        }

        /// <summary>
        /// Determines whether the established session's token kind satisfies the requirement.
        /// A missing identity cannot satisfy an explicit requirement.
        /// </summary>
        /// <param name="tokenType">The token type.</param>
        public bool SatisfiesIdentity(UserTokenType? tokenType)
        {
            return !UserIdentityToken.HasValue || tokenType == UserIdentityToken;
        }

        /// <summary>
        /// Gets the security policy name for a policy URI, or an empty string when it is not recognized.
        /// </summary>
        /// <param name="policyUri">The policy URI.</param>
        internal static string GetSecurityPolicyName(string? policyUri)
        {
            if (policyUri is null || !policyUri.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }
            string name = policyUri.Substring(PolicyPrefix.Length);
            return WotBindingConformance.IsSecurityPolicy(name) ? name : string.Empty;
        }

        /// <summary>
        /// Validates the security requirement alternatives.
        /// </summary>
        /// <param name="requirements">The security requirement alternatives.</param>
        /// <param name="parameterName">The parameter name.</param>
        internal static void Validate(ArrayOf<WotOpcUaSecurityRequirement> requirements, string parameterName)
        {
            if (requirements.Contains(requirement => requirement is null))
            {
                throw new ArgumentException("A security alternative cannot be null.", parameterName);
            }
        }

        /// <summary>
        /// The policy prefix used by <c>WotOpcUaSecurityRequirement</c>.
        /// </summary>
        internal const string PolicyPrefix = WotBindingConformance.OpcUaNamespace + "SecurityPolicy#";
    }
}
