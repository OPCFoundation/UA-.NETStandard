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
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The security floor a document puts on an <c>auto</c> endpoint selection
    /// through <c>uav:minimumSecurity</c> (WoT Binding Section 5.7.1).
    /// </summary>
    /// <remarks>
    /// <c>auto</c> says only that the client chooses, so two clients reading
    /// one Thing Description may land on different security levels, including
    /// the weakest the Server offers. The floor states "choose freely, but not
    /// below this" in the two dimensions the <c>uav:channelsec</c> scheme
    /// already defines. It constrains a choice among the endpoints a Server
    /// already offers and nothing else: certificate trust, endpoint filtering
    /// on any other attribute, transport-profile negotiation and the user-token
    /// policy within an endpoint stay with the application's own security
    /// configuration.
    /// </remarks>
    public sealed class WotSecurityFloor
    {
        /// <summary>
        /// Initializes a new security floor.
        /// </summary>
        /// <param name="securityMode">
        /// The weakest <c>MessageSecurityMode</c> that may be selected, or
        /// <c>null</c> when the document does not constrain the mode.
        /// </param>
        /// <param name="securityPolicy">
        /// The weakest security policy that may be selected, or <c>null</c>
        /// when the document does not constrain the policy.
        /// </param>
        public WotSecurityFloor(string? securityMode, string? securityPolicy)
        {
            SecurityMode = securityMode;
            SecurityPolicy = securityPolicy;
        }

        /// <summary>
        /// A floor that constrains nothing, which is how an <c>auto</c> scheme
        /// without <c>uav:minimumSecurity</c> behaves.
        /// </summary>
        public static WotSecurityFloor Unconstrained { get; } = new WotSecurityFloor(null, null);

        /// <summary>
        /// Gets the weakest <c>MessageSecurityMode</c> that may be selected.
        /// </summary>
        public string? SecurityMode { get; }

        /// <summary>
        /// Gets the weakest security policy that may be selected.
        /// </summary>
        public string? SecurityPolicy { get; }

        /// <summary>
        /// Gets whether the floor constrains neither dimension.
        /// </summary>
        public bool IsEmpty => SecurityMode is null && SecurityPolicy is null;

        /// <summary>
        /// Determines whether an endpoint's mode and policy satisfy the floor.
        /// </summary>
        /// <remarks>
        /// A policy this Binding does not name ranks below every policy it
        /// names (WoT Binding Section 5.7.1), so an unnamed policy never
        /// satisfies a stated policy floor. An unnamed <em>mode</em> is not a
        /// mode at all and never satisfies a mode floor.
        /// </remarks>
        /// <param name="securityMode">The endpoint's <c>MessageSecurityMode</c> name.</param>
        /// <param name="securityPolicy">The endpoint's security-policy name.</param>
        /// <returns><c>true</c> when the endpoint is at or above the floor.</returns>
        public bool Permits(string? securityMode, string? securityPolicy)
        {
            if (SecurityMode is not null)
            {
                if (!WotBindingConformance.TryGetSecurityModeRank(SecurityMode, out int floorRank) ||
                    !WotBindingConformance.TryGetSecurityModeRank(securityMode, out int rank) ||
                    rank < floorRank)
                {
                    return false;
                }
            }
            if (SecurityPolicy is not null)
            {
                if (!WotBindingConformance.TryGetSecurityPolicyRank(
                        SecurityPolicy, out int floorRank) ||
                    !WotBindingConformance.TryGetSecurityPolicyRank(securityPolicy, out int rank) ||
                    rank < floorRank)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Parses and validates a <c>uav:minimumSecurity</c> object against the
        /// rules of WoT Binding Sections 5.7.1 and 7.
        /// </summary>
        /// <param name="minimumSecurity">The term's value.</param>
        /// <param name="floor">The parsed floor.</param>
        /// <param name="error">The rule violated, when parsing failed.</param>
        /// <returns><c>true</c> when the value is a valid floor.</returns>
        public static bool TryParse(
            JsonElement minimumSecurity,
            out WotSecurityFloor? floor,
            out string error)
        {
            floor = null;
            if (minimumSecurity.ValueKind != JsonValueKind.Object)
            {
                error = $"The {WotBindingConformance.MinimumSecurityTerm} term shall be an object " +
                    $"carrying {WotBindingConformance.SecurityModeTerm}, " +
                    $"{WotBindingConformance.SecurityPolicyTerm}, or both.";
                return false;
            }

            string? securityMode = null;
            string? securityPolicy = null;
            int members = 0;
            foreach (JsonProperty member in minimumSecurity.EnumerateObject())
            {
                members++;
                if (string.Equals(
                    member.Name,
                    WotBindingConformance.SecurityModeTerm,
                    StringComparison.Ordinal))
                {
                    securityMode = member.Value.ValueKind == JsonValueKind.String
                        ? member.Value.GetString()
                        : null;
                    if (!WotBindingConformance.IsSecurityMode(securityMode))
                    {
                        error = $"The {WotBindingConformance.SecurityModeTerm} of a security floor " +
                            "shall be None, Sign or SignAndEncrypt.";
                        return false;
                    }
                    continue;
                }
                if (string.Equals(
                    member.Name,
                    WotBindingConformance.SecurityPolicyTerm,
                    StringComparison.Ordinal))
                {
                    securityPolicy = member.Value.ValueKind == JsonValueKind.String
                        ? member.Value.GetString()
                        : null;
                    if (!WotBindingConformance.IsSecurityPolicy(securityPolicy))
                    {
                        error = $"The {WotBindingConformance.SecurityPolicyTerm} of a security " +
                            "floor shall be one of the policy names WoT Binding Section 5.7 lists.";
                        return false;
                    }
                    continue;
                }
                error = $"The {WotBindingConformance.MinimumSecurityTerm} object carries " +
                    $"'{member.Name}'. It constrains the secure-channel mode and policy only, " +
                    "not trust-list policy, endpoint filtering or transport-profile negotiation " +
                    "(Section 5.7.1).";
                return false;
            }

            if (members == 0)
            {
                error = $"The {WotBindingConformance.MinimumSecurityTerm} object shall carry " +
                    $"{WotBindingConformance.SecurityModeTerm}, " +
                    $"{WotBindingConformance.SecurityPolicyTerm}, or both; an empty floor states " +
                    "nothing.";
                return false;
            }

            floor = new WotSecurityFloor(securityMode, securityPolicy);
            error = string.Empty;
            return true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IsEmpty)
            {
                return "unconstrained";
            }
            return "mode >= " + (SecurityMode ?? "any") + ", policy >= " + (SecurityPolicy ?? "any");
        }
    }
}
