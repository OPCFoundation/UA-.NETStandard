// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Factory that creates <see cref="DefaultSubscriptionEngine"/>
    /// instances backed by the V2 channel-based publish pipeline.
    /// </summary>
    public sealed class DefaultSubscriptionEngineFactory
        : ISubscriptionEngineFactory
    {
        /// <summary>
        /// Shared singleton instance bound to <see cref="TimeProvider.System"/>.
        /// </summary>
        public static DefaultSubscriptionEngineFactory Instance { get; } = new();

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="DefaultSubscriptionEngineFactory"/> class bound to
        /// <see cref="TimeProvider.System"/>.
        /// </summary>
        public DefaultSubscriptionEngineFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="DefaultSubscriptionEngineFactory"/> class.
        /// </summary>
        /// <param name="timeProvider">An optional <see cref="TimeProvider"/>
        /// forwarded to every created <see cref="DefaultSubscriptionEngine"/>.
        /// When <see langword="null"/>, <see cref="TimeProvider.System"/> is
        /// used.</param>
        public DefaultSubscriptionEngineFactory(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ISubscriptionEngine Create(ISubscriptionEngineContext context)
        {
            return new DefaultSubscriptionEngine(context, m_timeProvider);
        }

        private readonly TimeProvider m_timeProvider;
    }
}
