// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Handles inbound PubSub Action requests for a registered target.
    /// </summary>
    public interface IPubSubActionHandler
    {
        /// <summary>
        /// Executes an inbound Action request.
        /// </summary>
        ValueTask<PubSubActionHandlerResult> HandleAsync(
            PubSubActionInvocation invocation,
            CancellationToken cancellationToken = default);
    }
}
