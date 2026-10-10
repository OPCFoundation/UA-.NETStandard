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
        public WotOpcUaSecurityRequirement(MessageSecurityMode? securityMode, string? securityPolicyUri)
            : this(securityMode, securityPolicyUri, null)
        {
        }

        /// <summary>
        /// Initializes exact channel and user-identity constraints.
        /// </summary>
        public WotOpcUaSecurityRequirement(
            MessageSecurityMode? securityMode, string? securityPolicyUri, UserTokenType? userIdentityToken)
            : this(securityMode, securityPolicyUri, userIdentityToken, null)
        {
        }

        /// <summary>
        /// Initializes one exact alternative and its optional automatic-selection floor.
        /// </summary>
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
        public bool SatisfiesIdentity(UserTokenType? tokenType)
        {
            return !UserIdentityToken.HasValue || tokenType == UserIdentityToken;
        }

        internal static string GetSecurityPolicyName(string? policyUri)
        {
            if (policyUri is null || !policyUri.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }
            string name = policyUri.Substring(PolicyPrefix.Length);
            return WotBindingConformance.IsSecurityPolicy(name) ? name : string.Empty;
        }

        internal static void Validate(ArrayOf<WotOpcUaSecurityRequirement> requirements, string parameterName)
        {
            if (requirements.Contains(requirement => requirement is null))
            {
                throw new ArgumentException("A security alternative cannot be null.", parameterName);
            }
        }

        internal const string PolicyPrefix = WotBindingConformance.OpcUaNamespace + "SecurityPolicy#";
    }
}
