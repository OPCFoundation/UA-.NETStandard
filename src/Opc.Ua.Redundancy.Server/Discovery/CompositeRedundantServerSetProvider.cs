// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="IRedundantServerSetProvider"/> that prefers a dynamic
    /// discovery provider and falls back to a statically-configured provider when discovery has not (yet) found
    /// any peers. This realises "static configuration is the fallback for dynamic discovery": <c>FindServers</c>
    /// returns the discovered replica set once discovery yields peers, and the configured set until then.
    /// </summary>
    public sealed class CompositeRedundantServerSetProvider : IRedundantServerSetProvider
    {
        /// <summary>
        /// Creates a composite provider.
        /// </summary>
        /// <param name="primary">The dynamic discovery provider consulted first.</param>
        /// <param name="fallback">The statically-configured provider used when discovery is empty.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="primary"/> or <paramref name="fallback"/> is <c>null</c>.
        /// </exception>
        public CompositeRedundantServerSetProvider(
            IRedundantServerSetProvider primary,
            IRedundantServerSetProvider fallback)
        {
            m_primary = primary ?? throw new ArgumentNullException(nameof(primary));
            m_fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        }

        /// <inheritdoc/>
        public ArrayOf<ApplicationDescription> GetRedundantServerSet()
        {
            ArrayOf<ApplicationDescription> discovered = m_primary.GetRedundantServerSet();
            return discovered.Count > 0 ? discovered : m_fallback.GetRedundantServerSet();
        }

        private readonly IRedundantServerSetProvider m_primary;
        private readonly IRedundantServerSetProvider m_fallback;
    }
}
