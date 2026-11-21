// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// DI-resolvable factory that creates an <see cref="IPubSubTransport"/>
    /// for a given <see cref="PubSubConnectionDataType"/>. The
    /// application's transport registry holds one factory per supported
    /// <see cref="TransportProfileUri"/> and picks the matching entry at
    /// connection-enable time.
    /// </summary>
    /// <remarks>
    /// Implements the transport-factory contract described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3">
    /// Part 14 §7.3 PubSub transport mappings</see>. Each transport
    /// library (Opc.Ua.PubSub.Udp, Opc.Ua.PubSub.Mqtt) registers one
    /// implementation via DI.
    /// </remarks>
    public interface IPubSubTransportFactory
    {
        /// <summary>
        /// Transport profile URI handled by this factory (e.g.
        /// <see cref="Profiles.PubSubUdpUadpTransport"/>).
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Creates a transport bound to <paramref name="connection"/>.
        /// The returned transport is not yet open; callers invoke
        /// <see cref="IPubSubTransport.OpenAsync"/> after wiring the
        /// transport into the connection state machine.
        /// </summary>
        /// <param name="connection">PubSubConnection configuration.</param>
        /// <param name="telemetry">Telemetry context for logging and metrics.</param>
        /// <param name="timeProvider">Clock used by the transport.</param>
        /// <returns>A transport ready to be opened.</returns>
        IPubSubTransport Create(
            PubSubConnectionDataType connection,
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
