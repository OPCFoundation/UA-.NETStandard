// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Creates channels and provides ability to monitor them
    /// </summary>
    public interface ITransportChannelManager
    {
        /// <summary>
        /// Callback to register and receive channel diagnostics
        /// </summary>
        event Action<ITransportChannel, TransportChannelDiagnostic>? OnDiagnostics;

        /// <summary>
        /// Opens a transport and transfers the supplied certificate handles to it, releasing them on failure.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <param name="context">The service message context.</param>
        /// <param name="clientCertificate">The client certificate handle whose ownership is transferred.</param>
        /// <param name="clientCertificateChain">The certificate chain whose ownership is transferred.</param>
        /// <param name="connection">An optional reverse-connect waiting connection.</param>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        /// <returns>The opened transport channel.</returns>
        /// <remarks>
        /// Ownership transfers when this method is called, even if the manager is disposed or argument validation
        /// fails before transport creation. Callers retaining reusable certificate material must pass independently
        /// owned references rather than reuse the transferred handles.
        /// </remarks>
        ValueTask<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            IServiceMessageContext context,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain = null,
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default);
    }
}
