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

namespace Opc.Ua.PubSub.Security.Policies
{
    /// <summary>
    /// Static lookup table that maps a PubSub security policy URI to
    /// its concrete <see cref="IPubSubSecurityPolicy"/> singleton.
    /// </summary>
    /// <remarks>
    /// Implements the policy enumeration of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>. The set is
    /// fixed at compile time: <see cref="PubSubNonePolicy"/>,
    /// <see cref="PubSubAes128CtrPolicy"/> and
    /// <see cref="PubSubAes256CtrPolicy"/>.
    /// </remarks>
    public static class PubSubSecurityPolicyRegistry
    {
        private static readonly IPubSubSecurityPolicy[] s_all =
        [
            PubSubNonePolicy.Instance,
            PubSubAes128CtrPolicy.Instance,
            PubSubAes256CtrPolicy.Instance,
        ];

        /// <summary>
        /// Read-only view over every built-in policy.
        /// </summary>
        public static IReadOnlyList<IPubSubSecurityPolicy> All => s_all;

        /// <summary>
        /// Looks up the policy bundle that matches
        /// <paramref name="policyUri"/>. Returns <see langword="null"/>
        /// when the URI is not one of the built-in policies.
        /// </summary>
        /// <param name="policyUri">Policy URI to resolve.</param>
        /// <returns>The matching policy or <see langword="null"/>.</returns>
        public static IPubSubSecurityPolicy? GetByUri(string? policyUri)
        {
            if (string.IsNullOrEmpty(policyUri))
            {
                return null;
            }
            foreach (IPubSubSecurityPolicy policy in s_all)
            {
                if (string.Equals(
                    policy.PolicyUri,
                    policyUri,
                    StringComparison.Ordinal))
                {
                    return policy;
                }
            }
            return null;
        }
    }
}
