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

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Keeps the discovery endpoints an endpoint was selected from.
    /// </summary>
    public partial class ConfiguredEndpoint
    {
        /// <summary>
        /// Remembers the discovery endpoints <paramref name="selected"/> was
        /// selected from. A <see cref="ConfiguredEndpoint"/> created for the
        /// description adopts them as <see cref="DiscoveryEndpoints"/>, so the
        /// session compares them with the endpoints returned by CreateSession,
        /// and a later discovery update must return the same security options.
        /// </summary>
        /// <param name="selected">The selected endpoint description.</param>
        /// <param name="endpoints">The discovery endpoints it was selected from.</param>
        internal static void SetSelectionEndpoints(
            EndpointDescription selected,
            ArrayOf<EndpointDescription> endpoints)
        {
            if (selected == null || endpoints.IsEmpty)
            {
                return;
            }

            // kept on the description, so a clone of it keeps the list too.
            selected.SelectionEndpoints = endpoints;
        }

        /// <summary>
        /// Adopts the discovery endpoints the description was selected from.
        /// </summary>
        private void AdoptSelectionEndpoints(EndpointDescription description)
        {
            ArrayOf<EndpointDescription> selection = description.SelectionEndpoints;
            if (!selection.IsEmpty)
            {
                m_selectionEndpoints = selection;
                DiscoveryEndpoints = selection;
            }
        }

        /// <summary>
        /// Checks the endpoints of a discovery update against the endpoints the
        /// endpoint was selected from. The selection is only checked once: the
        /// updated list becomes <see cref="DiscoveryEndpoints"/> and is compared
        /// with the endpoints returned by CreateSession.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The update offers different security options than the selection.
        /// </exception>
        private void ValidateAgainstSelectionEndpoints(ArrayOf<EndpointDescription> endpoints)
        {
            ArrayOf<EndpointDescription> selection = m_selectionEndpoints;
            if (selection.IsEmpty)
            {
                return;
            }

            if (!HaveSameSecurityOptions(selection, endpoints))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The endpoints returned by discovery do not match the endpoints " +
                    "the endpoint was selected from.");
            }

            m_selectionEndpoints = default;
        }

        /// <summary>
        /// Compares the security mode, security policy, transport profile,
        /// security level and user token policies of two endpoint lists,
        /// ignoring order and URLs.
        /// </summary>
        private static bool HaveSameSecurityOptions(
            ArrayOf<EndpointDescription> first,
            ArrayOf<EndpointDescription> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            var unmatched = new List<EndpointDescription?>(second.Count);
            for (int ii = 0; ii < second.Count; ii++)
            {
                unmatched.Add(second[ii]);
            }

            for (int ii = 0; ii < first.Count; ii++)
            {
                EndpointDescription? endpoint = first[ii];
                int match = unmatched.FindIndex(candidate =>
                    candidate != null &&
                    endpoint != null &&
                    candidate.SecurityMode == endpoint.SecurityMode &&
                    candidate.SecurityLevel == endpoint.SecurityLevel &&
                    string.Equals(
                        candidate.SecurityPolicyUri,
                        endpoint.SecurityPolicyUri,
                        System.StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.TransportProfileUri,
                        endpoint.TransportProfileUri,
                        System.StringComparison.Ordinal) &&
                    HaveSameUserTokenPolicies(candidate.UserIdentityTokens, endpoint.UserIdentityTokens));
                if (match < 0)
                {
                    return false;
                }
                unmatched.RemoveAt(match);
            }

            return true;
        }

        /// <summary>
        /// Compares the user token policies of two endpoints, ignoring order:
        /// policy id, token type, security policy, issued token type and issuer.
        /// </summary>
        private static bool HaveSameUserTokenPolicies(
            ArrayOf<UserTokenPolicy> first,
            ArrayOf<UserTokenPolicy> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            var unmatched = new List<UserTokenPolicy?>(second.Count);
            for (int ii = 0; ii < second.Count; ii++)
            {
                unmatched.Add(second[ii]);
            }

            for (int ii = 0; ii < first.Count; ii++)
            {
                UserTokenPolicy? policy = first[ii];
                int match = unmatched.FindIndex(candidate =>
                    candidate != null &&
                    policy != null &&
                    candidate.TokenType == policy.TokenType &&
                    SameText(candidate.PolicyId, policy.PolicyId) &&
                    SameText(candidate.SecurityPolicyUri, policy.SecurityPolicyUri) &&
                    SameText(candidate.IssuedTokenType, policy.IssuedTokenType) &&
                    SameText(candidate.IssuerEndpointUrl, policy.IssuerEndpointUrl));
                if (match < 0)
                {
                    return false;
                }
                unmatched.RemoveAt(match);
            }

            return true;
        }

        private static bool SameText(string? first, string? second)
        {
            return string.Equals(first ?? string.Empty, second ?? string.Empty, System.StringComparison.Ordinal);
        }

        private ArrayOf<EndpointDescription> m_selectionEndpoints;
    }
}
