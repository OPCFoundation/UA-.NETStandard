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

namespace Opc.Ua.PubSub.Security.Policies
{
    /// <summary>
    /// The PubSub security policies an application knows about.
    /// </summary>
    /// <remarks>
    /// Resolve this from the container to work against the policies that
    /// application offers. Code with no container in scope uses
    /// <see cref="PubSubSecurityPolicyRegistry.Default"/>, which carries the
    /// built-in set.
    /// </remarks>
    public interface IPubSubSecurityPolicyRegistry
    {
        /// <summary>
        /// Every policy the registry carries.
        /// </summary>
        ArrayOf<IPubSubSecurityPolicy> Policies { get; }

        /// <summary>
        /// Looks up the policy bundle that matches <paramref name="policyUri"/>.
        /// </summary>
        /// <param name="policyUri">Policy URI to resolve.</param>
        /// <returns>The matching policy, or <see langword="null"/>.</returns>
        IPubSubSecurityPolicy? GetByUri(string? policyUri);
    }

    /// <summary>
    /// Maps a PubSub security policy URI to its concrete
    /// <see cref="IPubSubSecurityPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Implements the policy enumeration of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>: <see cref="PubSubNonePolicy"/>,
    /// <see cref="PubSubAes128CtrPolicy"/> and <see cref="PubSubAes256CtrPolicy"/>.
    /// <para>
    /// The built-in policies perform their cryptography with the platform. A
    /// deployment that registers a symmetric crypto provider gets policies
    /// constructed against it instead, which is why the platform-backed
    /// instances are not public: taking one directly would quietly bypass the
    /// configured provider. Resolve through a registry.
    /// </para>
    /// </remarks>
    public sealed class PubSubSecurityPolicyRegistry : IPubSubSecurityPolicyRegistry
    {
        /// <summary>
        /// Initializes a registry carrying the built-in, platform-backed policies.
        /// </summary>
        public PubSubSecurityPolicyRegistry()
            : this(BuiltIn())
        {
        }

        /// <summary>
        /// Initializes a registry carrying the given policies.
        /// </summary>
        /// <param name="policies">The policies to carry.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubSecurityPolicyRegistry(ArrayOf<IPubSubSecurityPolicy> policies)
        {
            if (policies.IsNull)
            {
                throw new ArgumentNullException(nameof(policies));
            }

            Policies = policies;
        }

        /// <summary>
        /// The registry used when none was injected.
        /// </summary>
        public static PubSubSecurityPolicyRegistry Default { get; } = new();

        /// <inheritdoc/>
        public ArrayOf<IPubSubSecurityPolicy> Policies { get; }

        /// <inheritdoc/>
        public IPubSubSecurityPolicy? GetByUri(string? policyUri)
        {
            if (string.IsNullOrEmpty(policyUri))
            {
                return null;
            }

            foreach (IPubSubSecurityPolicy policy in Policies)
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

        /// <summary>
        /// The built-in, platform-backed policies.
        /// </summary>
        /// <remarks>
        /// Built on demand rather than held in a static field: <see cref="Default"/>
        /// is itself a static initializer, and a field declared after it would
        /// still be null when it ran.
        /// </remarks>
        private static ArrayOf<IPubSubSecurityPolicy> BuiltIn()
        {
            return new IPubSubSecurityPolicy[]
            {
                PubSubNonePolicy.Instance,
                PubSubAes128CtrPolicy.Instance,
                PubSubAes256CtrPolicy.Instance
            };
        }
    }
}
