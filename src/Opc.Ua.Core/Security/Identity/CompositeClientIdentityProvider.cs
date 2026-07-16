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
 *
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Composes multiple <see cref="IClientIdentityProvider"/> instances
    /// and dispatches to the first registered provider that can satisfy
    /// the selected <see cref="UserTokenPolicy"/>.
    /// </summary>
    public sealed class CompositeClientIdentityProvider : IClientIdentityProvider
    {
        /// <summary>
        /// Creates a composite from the supplied providers.
        /// </summary>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ArgumentException"/>
        public CompositeClientIdentityProvider(
            IEnumerable<IClientIdentityProvider> providers)
        {
            if (providers == null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            foreach (IClientIdentityProvider provider in providers)
            {
                if (provider == null)
                {
                    throw new ArgumentException(
                        "Provider collection cannot contain null entries.",
                        nameof(providers));
                }
                m_providers.Add(provider);
            }

            if (m_providers.Count == 0)
            {
                throw new ArgumentException(
                    "At least one identity provider is required.",
                    nameof(providers));
            }
        }

        /// <summary>
        /// Creates a composite from the supplied providers.
        /// </summary>
        public CompositeClientIdentityProvider(
            params IClientIdentityProvider[] providers)
            : this((IEnumerable<IClientIdentityProvider>)providers)
        {
        }

        /// <inheritdoc/>
        public IReadOnlyList<UserTokenType> SupportedTokenTypes
        {
            get
            {
                var result = new List<UserTokenType>();
                foreach (IClientIdentityProvider provider in m_providers)
                {
                    foreach (UserTokenType tokenType in provider.SupportedTokenTypes)
                    {
                        if (!result.Contains(tokenType))
                        {
                            result.Add(tokenType);
                        }
                    }
                }
                return result;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedIssuedTokenProfileUris
        {
            get
            {
                var result = new List<string>();
                foreach (IClientIdentityProvider provider in m_providers)
                {
                    foreach (string profileUri in provider.SupportedIssuedTokenProfileUris)
                    {
                        if (!result.Contains(profileUri))
                        {
                            result.Add(profileUri);
                        }
                    }
                }
                return result;
            }
        }

        /// <inheritdoc/>
        public DateTime ExpiresAt
        {
            get
            {
                DateTime expiresAt = DateTime.MaxValue;
                lock (m_lock)
                {
                    foreach (IClientIdentityProvider provider in m_queriedProviders)
                    {
                        if (provider.ExpiresAt < expiresAt)
                        {
                            expiresAt = provider.ExpiresAt;
                        }
                    }
                }
                return expiresAt;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            List<string>? rejections = null;
            foreach (IClientIdentityProvider provider in m_providers)
            {
                CanSatisfyResult result = await provider
                    .CanSatisfyAsync(policy, context, ct)
                    .ConfigureAwait(false);
                if (result.CanSatisfy)
                {
                    return CanSatisfyResult.Yes;
                }

                rejections ??= [];
                rejections.Add(result.RejectionReason ?? "rejected");
            }

            return CanSatisfyResult.No(
                rejections is { Count: > 0 }
                    ? "No registered provider satisfied the policy: " + string.Join("; ", rejections)
                    : "No registered identity providers.");
        }

        /// <inheritdoc/>
        public async ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            List<string>? rejections = null;
            foreach (IClientIdentityProvider provider in m_providers)
            {
                CanSatisfyResult result = await provider
                    .CanSatisfyAsync(policy, context, ct)
                    .ConfigureAwait(false);
                if (!result.CanSatisfy)
                {
                    rejections ??= [];
                    rejections.Add(result.RejectionReason ?? "rejected");
                    continue;
                }

                MarkQueried(provider);
                return await provider
                    .GetIdentityAsync(policy, context, ct)
                    .ConfigureAwait(false);
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenRejected,
                "No registered client identity provider can satisfy the selected user token policy: {0}",
                rejections is { Count: > 0 } ? string.Join("; ", rejections) : "no providers registered");
        }

        private void MarkQueried(IClientIdentityProvider provider)
        {
            lock (m_lock)
            {
                if (!m_queriedProviders.Contains(provider))
                {
                    m_queriedProviders.Add(provider);
                }
            }
        }

        private readonly List<IClientIdentityProvider> m_providers = [];
        private readonly List<IClientIdentityProvider> m_queriedProviders = [];
        private readonly Lock m_lock = new();
    }
}
