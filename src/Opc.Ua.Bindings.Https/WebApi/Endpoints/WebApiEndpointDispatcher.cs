// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NET8_0_OR_GREATER
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Opc.Ua;

namespace Opc.Ua.Bindings.WebApi.Endpoints
{
    /// <summary>
    /// Free-function dispatcher for the OPC UA REST binding's
    /// Minimal-API endpoints. Owns the envelope-less encode / decode
    /// loop, the media-type-parameter encoding negotiation
    /// (<see cref="WebApiMediaType"/>), and the dispatch hand-off to
    /// <see cref="IWebApiServer"/>. Replaces the reflection-driven MVC
    /// controller surface so the binding compiles trim-clean for
    /// NativeAOT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All generic instantiations of
    /// <see cref="HandleAsync{TRequest, TResponse}(HttpContext)"/> are
    /// closed over concrete request/response pairs by the
    /// <c>MapWebApiEndpoints</c> route-builder extension, so the
    /// trimmer can see every reachable type at compile time without
    /// scanning the controller assembly.
    /// </para>
    /// <para>
    /// Faults are returned as a typed <c>&lt;Service&gt;Response</c>
    /// with the <see cref="ResponseHeader.ServiceResult"/> populated;
    /// the HTTP status stays <c>200</c> (consistent with the
    /// HTTPS-JSON binding behaviour and what generated OpenAPI clients
    /// expect). Decode failures surface as HTTP <c>400</c> because the
    /// concrete CLR response type cannot be built without a usable
    /// request, and unexpected transport errors surface as HTTP
    /// <c>500</c>.
    /// </para>
    /// </remarks>
    public static class WebApiEndpointDispatcher
    {
        /// <summary>
        /// Decodes the request body as <typeparamref name="TRequest"/>,
        /// invokes the dispatcher, and writes the typed
        /// <typeparamref name="TResponse"/> back as the response body.
        /// Services are resolved from
        /// <see cref="HttpContext.RequestServices"/> (per-request
        /// scope).
        /// </summary>
        /// <typeparam name="TRequest">
        /// Concrete CLR request type for the bound route (e.g.
        /// <c>ReadRequest</c>).
        /// </typeparam>
        /// <typeparam name="TResponse">
        /// Concrete CLR response type for the bound route (e.g.
        /// <c>ReadResponse</c>).
        /// </typeparam>
        /// <param name="context">The active HTTP request context.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.
        /// </exception>
        public static async Task HandleAsync<TRequest, TResponse>(HttpContext context)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new()
        {
            ArgumentNullException.ThrowIfNull(context);

            WebApiServer? boundServer = context.Features.Get<WebApiServer>();
            IWebApiServer server = boundServer ??
                context.RequestServices.GetRequiredService<IWebApiServer>();
            ISessionlessIdentityProvider? identityProvider = context.RequestServices
                .GetService<ISessionlessIdentityProvider>();
            ILoggerFactory loggerFactory = context.RequestServices
                .GetRequiredService<ILoggerFactory>();
            ILogger logger = loggerFactory.CreateLogger(typeof(WebApiEndpointDispatcher));

            HttpRequest request = context.Request;
            HttpResponse response = context.Response;
            CancellationToken ct = context.RequestAborted;

            WebApiEncoding encoding = WebApiMediaType.ParseEncoding(request.ContentType);
            WebApiEncoding acceptEncoding = WebApiMediaType.ParseEncoding(
                GetSingleAcceptHeader(request),
                fallback: encoding);

            IServiceMessageContext messageContext = server.MessageContext;
            int maxLength = GetMaxRequestBodySize<TRequest>(messageContext.MaxMessageSize);

            // A listener-bound route charges its body like every other HTTPS body: as the
            // bytes arrive, to the reassembly pool and the peer's ceiling, until decoded.
            HttpsTransportListener? listener = boundServer?.Listener;
            using HttpBodyAdmission? admission = listener?.TryAdmitHttpBody(context);
            if (listener != null && admission == null)
            {
                response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                response.ContentLength = 0;
                return;
            }

            TRequest decoded;
            try
            {
                byte[] payload;
                if (listener != null)
                {
                    payload = await listener.ReadAdmittedBodyAsync(request, maxLength, admission!, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    // Without a listener there is no pool to charge, but the body still
                    // grows only with the bytes that arrive, never from the announced length.
                    payload = await HttpBodyAdmission.ReadBodyAsync(
                        request.Body,
                        request.ContentLength,
                        maxLength,
                        admission: null,
                        TimeSpan.FromMilliseconds(TcpMessageLimits.DefaultChannelLifetime),
                        TimeProvider.System,
                        HttpBodyAdmission.CancelPendingBodyRead,
                        context,
                        ct).ConfigureAwait(false);
                }
                decoded = WebApiBodyCodec.DecodeBody<TRequest>(payload, messageContext);
                admission?.Release();
            }
            catch (ServiceResultException sre) when (
                sre.StatusCode == StatusCodes.BadServerTooBusy ||
                sre.StatusCode == StatusCodes.BadTimeout)
            {
                response.StatusCode = sre.StatusCode == StatusCodes.BadServerTooBusy
                    ? (int)HttpStatusCode.ServiceUnavailable
                    : (int)HttpStatusCode.RequestTimeout;
                response.ContentLength = 0;
                return;
            }
            catch (ServiceResultException sre)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.FailedToDecodeBody(
                        sre,
                        typeof(TRequest).Name,
                        SanitizePathForLog(request.Path),
                        sre.StatusCode);
                }
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.ContentLength = 0;
                return;
            }

            var invocation = new WebApiInvocationContext
            {
                SecureChannelId = context.TraceIdentifier,
                Endpoint = null,
                ClientCertificate = context.Connection.ClientCertificate?.RawData,
                ServerCertificate = null,
                PeerAddress = context.Connection.RemoteIpAddress,
                Identity = identityProvider?.Resolve(context)
            };

            IServiceResponse dispatched = await server
                .InvokeAsync(decoded, invocation, ct)
                .ConfigureAwait(false);

            JsonEncoderOptions encoderOptions = WebApiMediaType.ToEncoderOptions(acceptEncoding);
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = WebApiMediaType.FormatContentType(acceptEncoding);

            try
            {
                if (dispatched is TResponse typed)
                {
                    await WebApiBodyCodec
                        .EncodeBodyAsync(typed, response.Body, messageContext, encoderOptions, ct)
                        .ConfigureAwait(false);
                    return;
                }

                if (dispatched is ServiceFault fault)
                {
                    await WebApiBodyCodec
                        .EncodeBodyAsync(fault, response.Body, messageContext, encoderOptions, ct)
                        .ConfigureAwait(false);
                    return;
                }

                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.UnexpectedResponseType(
                        dispatched?.GetType().FullName,
                        SanitizePathForLog(request.Path),
                        typeof(TResponse).FullName);
                }
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (Exception ex)
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.FailedToEncodeBody(ex, typeof(TResponse).Name, SanitizePathForLog(request.Path));
                }
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                // encoded or abandoned: the response is no longer read, which releases
                // pooled and leased payloads (a Publish response's notification message),
                // as the binary transports do.
                (dispatched as IPooledEncodeable)?.Reuse();
            }
        }

        /// <summary>
        /// The largest body accepted for <typeparamref name="TRequest"/>. Discovery
        /// routes are anonymous, so their bodies are capped at the discovery message
        /// size, as a discovery-only SecureChannel caps them.
        /// </summary>
        /// <typeparam name="TRequest">The request type of the route.</typeparam>
        internal static int GetMaxRequestBodySize<TRequest>(int maxMessageSize)
        {
            if (typeof(TRequest) == typeof(GetEndpointsRequest) ||
                typeof(TRequest) == typeof(FindServersRequest) ||
                typeof(TRequest) == typeof(FindServersOnNetworkRequest))
            {
                return maxMessageSize > 0
                    ? Math.Min(maxMessageSize, TcpMessageLimits.DefaultDiscoveryMaxMessageSize)
                    : TcpMessageLimits.DefaultDiscoveryMaxMessageSize;
            }
            return maxMessageSize;
        }

        private static string? GetSingleAcceptHeader(HttpRequest request)
        {
            if (!request.Headers.TryGetValue("Accept", out StringValues values))
            {
                return null;
            }
            return values.Count > 0 ? values[0] : null;
        }

        /// <summary>
        /// Percent-encodes a user-supplied request path before it is written
        /// to a log message. Mitigates the CodeQL "Log entries created from
        /// user input" finding (cs/log-forging): a peer may include CR/LF or
        /// other control characters in the URL path to forge fake log lines.
        /// <see cref="Uri.EscapeDataString(string)"/> is recognised by CodeQL
        /// as a log-forging sanitizer and percent-encodes every control
        /// character (including CR/LF) along with reserved URI characters.
        /// </summary>
        private static string SanitizePathForLog(PathString path)
        {
            string? value = path.HasValue ? path.Value : null;
            return string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="WebApiEndpointDispatcher"/>.
    /// </summary>
    internal static partial class WebApiEndpointDispatcherLog
    {
        [LoggerMessage(EventId = BindingsHttpsEventIds.WebApiEndpointDispatcher + 0, Level = LogLevel.Information,
            Message = "Failed to decode {RequestType} body for route {Path}: {Status}")]
        public static partial void FailedToDecodeBody(
            this ILogger logger,
            Exception exception,
            string requestType,
            string path,
            StatusCode status);

        [LoggerMessage(EventId = BindingsHttpsEventIds.WebApiEndpointDispatcher + 1, Level = LogLevel.Error,
            Message = "Unexpected response type {ActualType} for route {Path} (expected {ExpectedType}).")]
        public static partial void UnexpectedResponseType(
            this ILogger logger,
            string? actualType,
            string path,
            string? expectedType);

        [LoggerMessage(EventId = BindingsHttpsEventIds.WebApiEndpointDispatcher + 2, Level = LogLevel.Error,
            Message = "Failed to encode {ResponseType} body for route {Path}.")]
        public static partial void FailedToEncodeBody(
            this ILogger logger,
            Exception exception,
            string responseType,
            string path);
    }
}
#endif
