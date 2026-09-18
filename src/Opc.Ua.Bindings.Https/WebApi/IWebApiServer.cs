/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET8_0_OR_GREATER
using System.Net;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Per-request transport context supplied to the OPC UA REST dispatcher.
    /// The dispatcher uses it to build the <see cref="SecureChannelContext"/>
    /// passed to <see cref="ITransportListenerCallback.ProcessRequestAsync"/>.
    /// </summary>
    public sealed class WebApiInvocationContext
    {
        /// <summary>
        /// Request identifier supplied by the endpoint pipeline.
        /// The built-in dispatcher uses the listener's stable channel identifier
        /// for service dispatch rather than this per-request value.
        /// </summary>
        public required string SecureChannelId { get; init; }

        /// <summary>
        /// The <see cref="EndpointDescription"/> the REST binding advertised
        /// for the listener that accepted this request. May be <c>null</c>
        /// when no endpoint match is available (e.g. discovery-only
        /// invocations on a listener that does not yet have a configured
        /// endpoint set).
        /// </summary>
        public EndpointDescription? Endpoint { get; init; }

        /// <summary>
        /// Raw client certificate (DER bytes) presented by mutual-TLS clients,
        /// or <c>null</c> when MTLS is disabled.
        /// </summary>
        public byte[]? ClientCertificate { get; init; }

        /// <summary>
        /// Raw server certificate (DER bytes) used by the TLS connection that
        /// carried this request, or <c>null</c> when not available.
        /// </summary>
        public byte[]? ServerCertificate { get; init; }

        /// <summary>
        /// The listener's observed peer IP address, or <c>null</c> when unavailable.
        /// The dispatcher forwards it for client lockout accounting.
        /// </summary>
        public IPAddress? PeerAddress { get; init; }

        /// <summary>
        /// The authenticated user identity resolved by the ASP.NET Core
        /// authentication pipeline (Anonymous / Bearer / Basic / MTLS), or
        /// <c>null</c> when no identity was supplied. The dispatcher forwards
        /// it as the upstream identity for the OPC UA service pipeline.
        /// </summary>
        public IUserIdentity? Identity { get; init; }
    }

    /// <summary>
    /// Dispatches decoded OPC UA REST requests through the host server's service pipeline.
    /// Used by the Minimal-API endpoints for OPC UA Part 6, G.3, "OpenAPI Mapping".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations translate <see cref="WebApiInvocationContext"/> into a
    /// <see cref="SecureChannelContext"/> and call
    /// <see cref="ITransportListenerCallback.ProcessRequestAsync"/>.
    /// OPC UA service errors become fault responses. Other errors propagate to the caller.
    /// </para>
    /// <para>
    /// The interface is intentionally narrow so the same endpoint
    /// surface works in two hosting modes (shared inside the existing
    /// <c>HttpsTransportListener</c> Kestrel pipeline, or own
    /// <c>WebApiTransportListener</c>). Each hosting mode provides a
    /// concrete <see cref="IWebApiServer"/> implementation that wires
    /// the DI-resolved dispatcher back to the listener's callback.
    /// </para>
    /// </remarks>
    public interface IWebApiServer
    {
        /// <summary>
        /// The encoding context (namespace tables, server tables, quotas,
        /// telemetry) shared with the host server. Reused by the
        /// endpoints for body decode / encode so OPC UA built-ins are
        /// (de)serialized with the same tables as binary / uajson channels.
        /// </summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Indicates whether the dispatcher is wired up to a host server
        /// callback. Endpoints should reject requests with
        /// <see cref="StatusCodes.BadServerHalted"/> when this is
        /// <c>false</c> (e.g. the listener has not yet been started or has
        /// been shut down).
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Dispatches a request and returns its service response or an OPC UA service fault.
        /// </summary>
        /// <param name="request">The decoded service request.</param>
        /// <param name="context">The per-request transport context.</param>
        /// <param name="ct">Cancellation token passed to the host callback.</param>
        /// <returns>
        /// The service response, or a <see cref="ServiceFault"/> for an OPC UA service error.
        /// A dispatcher without a host callback returns <see cref="StatusCodes.BadServerHalted"/>.
        /// </returns>
        /// <remarks>
        /// <see cref="ServiceResultException"/> is converted to a fault response.
        /// Cancellation and unexpected callback exceptions propagate to the caller.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="request"/> or <paramref name="context"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="System.OperationCanceledException">The host callback was canceled.</exception>
        System.Threading.Tasks.ValueTask<IServiceResponse> InvokeAsync(
            IServiceRequest request,
            WebApiInvocationContext context,
            System.Threading.CancellationToken ct);
    }
}
#endif
