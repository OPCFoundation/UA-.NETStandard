// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Delegate-backed PubSub Action handler.
    /// </summary>
    public sealed class DelegatePubSubActionHandler : IPubSubActionHandler
    {
        private readonly Func<PubSubActionInvocation, CancellationToken,
            ValueTask<PubSubActionHandlerResult>> m_handler;

        /// <summary>
        /// Initializes a new <see cref="DelegatePubSubActionHandler"/>.
        /// </summary>
        public DelegatePubSubActionHandler(
            Func<PubSubActionInvocation, CancellationToken,
                ValueTask<PubSubActionHandlerResult>> handler)
        {
            m_handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <inheritdoc/>
        public ValueTask<PubSubActionHandlerResult> HandleAsync(
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            if (invocation is null)
            {
                throw new ArgumentNullException(nameof(invocation));
            }
            return m_handler(invocation, cancellationToken);
        }
    }
}
