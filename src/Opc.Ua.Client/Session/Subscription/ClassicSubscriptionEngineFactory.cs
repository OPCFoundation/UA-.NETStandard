/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Factory that creates <see cref="ClassicSubscriptionEngine"/>
    /// instances. This is the default factory used when no custom
    /// factory is provided at session creation time.
    /// </summary>
    public sealed class ClassicSubscriptionEngineFactory
        : ISubscriptionEngineFactory
    {
        /// <summary>
        /// Gets the singleton instance of the factory.
        /// </summary>
        public static ClassicSubscriptionEngineFactory Instance { get; } = new();

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ClassicSubscriptionEngineFactory"/> class bound to
        /// <see cref="TimeProvider.System"/>.
        /// </summary>
        public ClassicSubscriptionEngineFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="ClassicSubscriptionEngineFactory"/> class.
        /// </summary>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// forwarded to the engines created by this factory. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public ClassicSubscriptionEngineFactory(TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public ISubscriptionEngine Create(ISubscriptionEngineContext context)
        {
            return new ClassicSubscriptionEngine(context, m_timeProvider);
        }

        private readonly TimeProvider m_timeProvider;
    }
}
