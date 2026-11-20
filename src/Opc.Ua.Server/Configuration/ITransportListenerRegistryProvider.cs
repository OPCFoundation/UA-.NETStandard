// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that surfaces the server's bound
    /// transport listeners. Implemented by
    /// <see cref="ServerInternalData"/>; the live list is populated by
    /// <see cref="StandardServer"/> once listeners are opened. Consumers
    /// resolve the registry via
    /// <c>(server as ITransportListenerRegistryProvider)?.TransportListeners</c>
    /// so external/mocked <see cref="IServerInternal"/> implementations
    /// remain binary-compatible.
    /// </summary>
    /// <remarks>
    /// Primary consumer: <see cref="ConfigurationNodeManager"/> needs the
    /// listeners to fan-out post-response channel cuts as required by
    /// OPC UA Part 12 §7.10.9 (ApplyChanges → force renegotiate).
    /// </remarks>
    public interface ITransportListenerRegistryProvider
    {
        /// <summary>
        /// A snapshot of the transport listeners currently bound to the
        /// server. Returns an empty list when listeners have not yet been
        /// opened or have already been closed.
        /// </summary>
        IReadOnlyList<ITransportListener> TransportListeners { get; }
    }
}
