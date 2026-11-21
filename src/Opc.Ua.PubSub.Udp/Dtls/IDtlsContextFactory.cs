// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Factory for DTLS 1.3 contexts used by the UDP PubSub transport.
    /// </summary>
    public interface IDtlsContextFactory
    {
        /// <summary>
        /// Creates a DTLS context for a parsed unicast endpoint and resolved profile.
        /// </summary>
        ValueTask<IDtlsContext> CreateAsync(
            PubSubConnectionDataType connection,
            UdpEndpoint endpoint,
            DtlsProfile profile,
            ITelemetryContext telemetry,
            TimeProvider timeProvider,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Per-endpoint DTLS record-protection context.
    /// </summary>
    public interface IDtlsContext : IDisposable
    {
        /// <summary>
        /// Negotiated DTLS profile.
        /// </summary>
        DtlsProfile Profile { get; }

        /// <summary>
        /// Runs the DTLS handshake before application datagrams flow.
        /// </summary>
        ValueTask OpenAsync(IDtlsDatagramChannel channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Protects a UADP NetworkMessage into a DTLS record.
        /// </summary>
        ValueTask<ReadOnlyMemory<byte>> ProtectAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Authenticates and unprotects a DTLS record into a UADP NetworkMessage.
        /// </summary>
        ValueTask<ReadOnlyMemory<byte>> UnprotectAsync(
            ReadOnlyMemory<byte> record,
            CancellationToken cancellationToken = default);
    }
}
