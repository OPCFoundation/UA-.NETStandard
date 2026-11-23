// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Optional startup contract for a Publish event stream that asynchronously
    /// connects its producer. Subscription creation awaits readiness without
    /// waiting for the first event.
    /// </summary>
    public interface IEventSourceReadiness
    {
        /// <summary>
        /// Completes when the stream can receive events, or propagates its
        /// startup failure. Enumeration runs concurrently with this wait.
        /// </summary>
        ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default);
    }
}
