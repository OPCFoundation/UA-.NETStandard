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
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.OpcUa
{
    /// <summary>
    /// Applies the <c>auto</c> endpoint-selection rules of WoT Binding
    /// Section 5.7.1 to a <c>GetEndpoints</c> response: it discards every
    /// endpoint below a stated floor and breaks a tie among the rest by a total
    /// order, so two clients reading one document and one response reach the
    /// same endpoint.
    /// </summary>
    /// <remarks>
    /// The clause constrains a choice among the endpoints a Server already
    /// offers and nothing else. Certificate trust and trust-list policy, the
    /// filtering of endpoints on any other attribute, transport-profile
    /// negotiation and the user-token policy within an endpoint stay with the
    /// application's own security configuration, so none of them is decided
    /// here.
    /// </remarks>
    public static class OpcUaWotEndpointSelector
    {
        /// <summary>
        /// Determines whether an endpoint satisfies a security floor.
        /// </summary>
        /// <param name="endpoint">The discovered endpoint.</param>
        /// <param name="floor">The floor, or <c>null</c> for no constraint.</param>
        /// <returns><c>true</c> when the endpoint is at or above the floor.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpoint"/> is <c>null</c>.
        /// </exception>
        public static bool Satisfies(EndpointDescription endpoint, WotSecurityFloor? floor)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            return floor is null ||
                floor.Permits(
                    endpoint.SecurityMode.ToString(),
                    GetSecurityPolicyName(endpoint.SecurityPolicyUri));
        }

        /// <summary>
        /// Selects the endpoint a client that honours WoT Binding Section 5.7.1
        /// connects to.
        /// </summary>
        /// <remarks>
        /// Every endpoint below the floor in either dimension is discarded, and
        /// the strongest of what remains is taken: the strongest
        /// <c>securityMode</c>, then the strongest <c>securityPolicyUri</c>
        /// ranking any policy this Binding does not name below every policy it
        /// names, then the highest <c>securityLevel</c> the Server reports,
        /// then the smallest <c>endpointUrl</c> in ascending Unicode code point
        /// order, then the earliest position in the response. Steps three to
        /// five exist only to break a tie. A client <em>shall not</em> fall
        /// back below the floor, so no endpoint is returned when none remains.
        /// </remarks>
        /// <param name="endpoints">The <c>GetEndpoints</c> response, in order.</param>
        /// <param name="floor">The floor, or <c>null</c> for no constraint.</param>
        /// <returns>
        /// The selected endpoint, or <c>null</c> when no endpoint satisfies the
        /// floor.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpoints"/> is <c>null</c>.
        /// </exception>
        public static EndpointDescription? Select(
            IReadOnlyList<EndpointDescription> endpoints, WotSecurityFloor? floor)
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }
            EndpointDescription? best = null;
            for (int ii = 0; ii < endpoints.Count; ii++)
            {
                EndpointDescription candidate = endpoints[ii];
                if (candidate is null || !Satisfies(candidate, floor))
                {
                    continue;
                }
                if (best is null || IsStronger(candidate, best))
                {
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Maps a security-policy URI onto the policy name WoT Binding
        /// Section 5.7 uses, which is the last segment of the URI. A URI this
        /// Binding does not name keeps its own last segment and therefore
        /// ranks below every policy it names.
        /// </summary>
        /// <param name="securityPolicyUri">The endpoint's policy URI.</param>
        /// <returns>The policy name.</returns>
        public static string GetSecurityPolicyName(string? securityPolicyUri)
        {
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                return string.Empty;
            }
            int separator = securityPolicyUri!.LastIndexOf('#');
            if (separator < 0)
            {
                separator = securityPolicyUri.LastIndexOf('/');
            }
            return separator >= 0 && separator + 1 < securityPolicyUri.Length
                ? securityPolicyUri.Substring(separator + 1)
                : securityPolicyUri;
        }

        /// <summary>
        /// Compares two eligible endpoints by the total order of WoT Binding
        /// Section 5.7.1. The earliest position in the response wins a complete
        /// tie, which is why this reports strictly stronger only.
        /// </summary>
        private static bool IsStronger(EndpointDescription candidate, EndpointDescription best)
        {
            int comparison = CompareRank(
                candidate.SecurityMode.ToString(), best.SecurityMode.ToString(), mode: true);
            if (comparison != 0)
            {
                return comparison > 0;
            }
            comparison = CompareRank(
                GetSecurityPolicyName(candidate.SecurityPolicyUri),
                GetSecurityPolicyName(best.SecurityPolicyUri),
                mode: false);
            if (comparison != 0)
            {
                return comparison > 0;
            }
            if (candidate.SecurityLevel != best.SecurityLevel)
            {
                return candidate.SecurityLevel > best.SecurityLevel;
            }
            return string.CompareOrdinal(
                candidate.EndpointUrl ?? string.Empty, best.EndpointUrl ?? string.Empty) < 0;
        }

        private static int CompareRank(string? left, string? right, bool mode)
        {
            int leftRank = GetRank(left, mode);
            int rightRank = GetRank(right, mode);
            return leftRank.CompareTo(rightRank);
        }

        private static int GetRank(string? value, bool mode)
        {
            int rank;
            bool known = mode
                ? WotBindingConformance.TryGetSecurityModeRank(value, out rank)
                : WotBindingConformance.TryGetSecurityPolicyRank(value, out rank);
            // A policy this Binding does not name ranks below every policy it
            // names (Section 5.7.1), so an unnamed value sorts under the
            // weakest named one rather than being treated as equal to it.
            return known ? rank : -1;
        }
    }
}
