// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// The client side interface with a UA server.
    /// </summary>
    public interface IClientBase : IDisposable
    {
        /// <summary>
        /// How to record activity of the client
        /// </summary>
        ClientTraceFlags ActivityTraceFlags { get; set; }

        /// <summary>
        /// The description of the endpoint.
        /// </summary>
        EndpointDescription Endpoint { get; }

        /// <summary>
        /// The configuration for the endpoint.
        /// </summary>
        EndpointConfiguration EndpointConfiguration { get; }

        /// <summary>
        /// The message context used when serializing messages.
        /// </summary>
        /// <value>The message context.</value>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets or set the channel being wrapped by the client object.
        /// </summary>
        /// <value>The transport channel.</value>
        ITransportChannel NullableTransportChannel { get; }

        /// <summary>
        /// Gets or set the channel being wrapped by the client object.
        /// If the channel is closed or null, throws <see cref="ServiceResultException"/>
        /// with status <see cref="StatusCodes.BadSecureChannelClosed"/>./>
        /// </summary>
        /// <value>The transport channel.</value>
        ITransportChannel TransportChannel { get; }

        /// <summary>
        /// What diagnostics the server should return in the response.
        /// </summary>
        /// <value>The diagnostics.</value>
        DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <summary>
        /// Sets the timeout for an operation.
        /// </summary>
        int OperationTimeout { get; set; }

        /// <summary>
        /// Set a default timeout hint that is sent to the server in the request header.
        /// If not configured the OperationTimeout is used as a fallback TimeoutHint.
        /// </summary>
        int DefaultTimeoutHint { get; set; }

        /// <summary>
        /// Whether the object has been disposed.
        /// </summary>
        /// <value><c>true</c> if disposed; otherwise, <c>false</c>.</value>
        bool Disposed { get; }

        /// <summary>
        /// Attach the channel to an already created client.
        /// </summary>
        /// <param name="channel">Channel to be used by the client</param>
        [Obsolete("Channels are now managed centrally via IClientChannelManager. " +
            "Use Session.CreateAsync(IClientChannelManager, ...) instead of manual " +
            "AttachChannel/DetachChannel. This method remains functional for back-compat.")]
        void AttachChannel(ITransportChannel channel);

        /// <summary>
        /// Detach the channel.
        /// </summary>
        [Obsolete("Channels are now managed centrally via IClientChannelManager. " +
            "Use Session.CreateAsync(IClientChannelManager, ...) instead of manual " +
            "AttachChannel/DetachChannel. This method remains functional for back-compat.")]
        void DetachChannel();

        /// <summary>
        /// Closes the channel using async call.
        /// </summary>
        Task<StatusCode> CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// Generates a unique request handle.
        /// </summary>
        uint NewRequestHandle();
    }
}
