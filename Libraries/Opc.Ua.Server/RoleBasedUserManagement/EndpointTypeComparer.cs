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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Compares <see cref="EndpointType"/> instances per OPC UA Part 18 §4.4.2:
    /// "Fields that have default values as defined in the EndpointType DataType
    /// are ignored during the comparison."
    /// </summary>
    internal static class EndpointTypeComparer
    {
        /// <summary>
        /// Returns <c>true</c> if the candidate endpoint matches the rule
        /// endpoint per §4.4.2 semantics: a field set to the default value on
        /// the rule side acts as a wildcard.
        /// </summary>
        public static bool Matches(EndpointType ruleEndpoint, EndpointType candidate)
        {
            if (ruleEndpoint == null || candidate == null)
            {
                return false;
            }

            if (!IsDefault(ruleEndpoint.EndpointUrl)
                && !string.Equals(ruleEndpoint.EndpointUrl, candidate.EndpointUrl, StringComparison.Ordinal))
            {
                return false;
            }

            if (ruleEndpoint.SecurityMode != MessageSecurityMode.Invalid
                && ruleEndpoint.SecurityMode != candidate.SecurityMode)
            {
                return false;
            }

            if (!IsDefault(ruleEndpoint.SecurityPolicyUri)
                && !string.Equals(ruleEndpoint.SecurityPolicyUri, candidate.SecurityPolicyUri, StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsDefault(ruleEndpoint.TransportProfileUri)
                && !string.Equals(ruleEndpoint.TransportProfileUri, candidate.TransportProfileUri, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two rule endpoints for storage-equivalence (used to detect
        /// <c>Bad_AlreadyExists</c> when adding the same endpoint twice).
        /// Default values are compared as default values — i.e., two rules are
        /// equivalent only if every field agrees, including unset vs set.
        /// </summary>
        public static bool RulesEqual(EndpointType a, EndpointType b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            return string.Equals(Normalize(a.EndpointUrl), Normalize(b.EndpointUrl), StringComparison.Ordinal)
                && a.SecurityMode == b.SecurityMode
                && string.Equals(Normalize(a.SecurityPolicyUri), Normalize(b.SecurityPolicyUri), StringComparison.Ordinal)
                && string.Equals(Normalize(a.TransportProfileUri), Normalize(b.TransportProfileUri), StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns a deep clone so callers can't mutate stored endpoints.
        /// </summary>
        public static EndpointType Clone(EndpointType source)
        {
            return new EndpointType
            {
                EndpointUrl = source.EndpointUrl,
                SecurityMode = source.SecurityMode,
                SecurityPolicyUri = source.SecurityPolicyUri,
                TransportProfileUri = source.TransportProfileUri
            };
        }

        private static bool IsDefault(string? value)
        {
            return string.IsNullOrEmpty(value);
        }

        private static string Normalize(string? value)
        {
            return value ?? string.Empty;
        }
    }
}
