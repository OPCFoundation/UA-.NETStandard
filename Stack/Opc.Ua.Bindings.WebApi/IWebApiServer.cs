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

using System.Security.Cryptography.X509Certificates;
using Opc.Ua;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Per-request context handed to the OPC UA REST server dispatcher
    /// (<see cref="IWebApiServer"/>). Captures the transport-layer
    /// information the underlying
    /// <see cref="ITransportListenerCallback.ProcessRequestAsync(SecureChannelContext, IServiceRequest, System.Threading.CancellationToken)"/>
    /// needs to build a <see cref="SecureChannelContext"/>.
    /// </summary>
    public sealed class WebApiInvocationContext
    {
        /// <summary>
        /// Synthetic secure-channel identifier representing the inbound HTTPS
        /// connection. Generated per-request by the endpoint pipeline.
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
        /// The authenticated user identity resolved by the ASP.NET Core
        /// authentication pipeline (Anonymous / Bearer / Basic / MTLS), or
        /// <c>null</c> when no identity provider produced one. Sessionless
        /// services rely on this to evaluate role-based access; session
        /// services flow the identity through the
        /// <see cref="RequestHeader.AuthenticationToken"/> instead.
        /// </summary>
        public IUserIdentity? Identity { get; init; }
    }

    /// <summary>
    /// Server-side dispatcher used by the OPC UA REST Minimal-API
    /// endpoints (OPC UA Part 6 §G.3 "OpenAPI Mapping") to flow a
    /// decoded request through the same pipeline as the binary and
    /// <c>opcua+uajson</c> transports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations bridge the endpoint layer to
    /// <see cref="ITransportListenerCallback.ProcessRequestAsync(SecureChannelContext, IServiceRequest, System.Threading.CancellationToken)"/>:
    /// they translate the
    /// <see cref="WebApiInvocationContext"/> into a
    /// <see cref="SecureChannelContext"/>, invoke the callback registered
    /// by the host server, and return the resulting service response (or
    /// a fault response if the callback throws).
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
        /// Invokes the server dispatcher for the supplied request and
        /// returns its response. Faults raised by the dispatcher are
        /// returned as <see cref="ServiceFault"/> bodies; never as
        /// exceptions thrown to the caller.
        /// </summary>
        /// <param name="request">The decoded service request.</param>
        /// <param name="context">The per-request transport context.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The resulting service response.</returns>
        System.Threading.Tasks.ValueTask<IServiceResponse> InvokeAsync(
            IServiceRequest request,
            WebApiInvocationContext context,
            System.Threading.CancellationToken ct);
    }
}
