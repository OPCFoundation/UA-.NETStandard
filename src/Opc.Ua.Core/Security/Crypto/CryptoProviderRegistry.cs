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
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// The default <see cref="ICryptoProviderRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Registrations are expected during start up and reads during operation, so
    /// the registry is guarded by a simple lock rather than a concurrent
    /// collection; contention is not a concern on a path that runs once per bound
    /// object.
    /// </remarks>
    public sealed class CryptoProviderRegistry : ICryptoProviderRegistry
    {
        /// <summary>
        /// Initializes a registry whose fallback is the platform provider.
        /// </summary>
        public CryptoProviderRegistry()
            : this(PlatformCryptoProvider.Instance)
        {
        }

        /// <summary>
        /// Initializes a registry with an explicit fallback provider.
        /// </summary>
        /// <param name="fallback">
        /// The provider used when nothing else matches. Must not be <c>null</c>.
        /// </param>
        public CryptoProviderRegistry(ICryptoProvider fallback)
        {
            m_fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        /// <inheritdoc/>
        public ArrayOf<ICryptoProvider> Providers
        {
            get
            {
                lock (m_lock)
                {
                    var providers = new List<ICryptoProvider>();
                    AddDistinct(providers, m_default ?? m_fallback);

                    foreach (ICryptoProvider provider in m_byPurposeAndPolicy.Values)
                    {
                        AddDistinct(providers, provider);
                    }

                    foreach (ICryptoProvider provider in m_byPurpose.Values)
                    {
                        AddDistinct(providers, provider);
                    }

                    return new ArrayOf<ICryptoProvider>(providers.ToArray());
                }
            }
        }

        /// <inheritdoc/>
        public void RegisterFor(
            CryptoPurpose purpose,
            string securityPolicyUri,
            ICryptoProvider provider)
        {
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new ArgumentException(
                    "A security policy is required.", nameof(securityPolicyUri));
            }

            lock (m_lock)
            {
                m_byPurposeAndPolicy[(purpose, securityPolicyUri)] = provider ?? throw new ArgumentNullException(nameof(provider));
            }
        }

        /// <inheritdoc/>
        public void RegisterFor(CryptoPurpose purpose, ICryptoProvider provider)
        {

            lock (m_lock)
            {
                m_byPurpose[purpose] = provider ?? throw new ArgumentNullException(nameof(provider));
            }
        }

        /// <inheritdoc/>
        public void RegisterDefault(ICryptoProvider provider)
        {

            lock (m_lock)
            {
                m_default = provider ?? throw new ArgumentNullException(nameof(provider));
            }
        }

        /// <inheritdoc/>
        public ICryptoProvider Resolve(
            CryptoPurpose purpose,
            string? securityPolicyUri = null,
            NodeId certificateType = default)
        {
            lock (m_lock)
            {
                if (securityPolicyUri != null &&
                    m_byPurposeAndPolicy.TryGetValue(
                        (purpose, securityPolicyUri), out ICryptoProvider? exact) &&
                    CanServe(exact, purpose, securityPolicyUri, certificateType))
                {
                    return exact;
                }

                if (m_byPurpose.TryGetValue(purpose, out ICryptoProvider? forPurpose) &&
                    CanServe(forPurpose, purpose, securityPolicyUri, certificateType))
                {
                    return forPurpose;
                }

                ICryptoProvider registeredDefault = m_default ?? m_fallback;
                if (CanServe(registeredDefault, purpose, securityPolicyUri, certificateType))
                {
                    return registeredDefault;
                }

                return m_fallback;
            }
        }

        /// <summary>
        /// Whether a provider declares a capability covering the request.
        /// </summary>
        /// <remarks>
        /// A provider bound to a purpose it cannot actually serve is a
        /// configuration mistake, and silently using it would fail later and
        /// further away. Falling through to the next candidate keeps the failure
        /// close to the cause.
        /// </remarks>
        private static bool CanServe(
            ICryptoProvider provider,
            CryptoPurpose purpose,
            string? securityPolicyUri,
            NodeId certificateType)
        {
            foreach (CryptoCapability capability in provider.Capabilities)
            {
                if (capability.Matches(purpose, securityPolicyUri, certificateType))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddDistinct(List<ICryptoProvider> providers, ICryptoProvider provider)
        {
            if (!providers.Any(p => ReferenceEquals(p, provider)))
            {
                providers.Add(provider);
            }
        }

        private readonly Dictionary<(CryptoPurpose, string), ICryptoProvider> m_byPurposeAndPolicy = [];
        private readonly Dictionary<CryptoPurpose, ICryptoProvider> m_byPurpose = [];
        private readonly ICryptoProvider m_fallback;
        private readonly Lock m_lock = new();
        private ICryptoProvider? m_default;
    }
}
