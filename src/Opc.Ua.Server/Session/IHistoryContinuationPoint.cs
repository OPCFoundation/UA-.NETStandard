// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// State a historical read leaves behind so a later HistoryRead can resume where the
    /// previous one stopped. The session holds it between calls and disposes it when the
    /// point is dropped or the session is cleared.
    /// </summary>
    /// <remarks>
    /// Implementations carry whatever the reader needs to resume; nothing outside the
    /// component that created a point interprets its contents.
    /// The historian's built-in continuation state holds the provider and, when available, the source node.
    /// Disposing the point does not clear these references, so keeping the point can also keep them alive.
    /// </remarks>
    public interface IHistoryContinuationPoint : IDisposable
    {
        /// <summary>
        /// Identifies the continuation point. It is the identifier the client receives and
        /// returns, so it must not change once the point has been saved.
        /// </summary>
        Guid Id { get; }
    }
}
