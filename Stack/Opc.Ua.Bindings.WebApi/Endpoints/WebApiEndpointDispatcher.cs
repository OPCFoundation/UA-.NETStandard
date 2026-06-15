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

            IWebApiServer server = context.RequestServices
                .GetRequiredService<IWebApiServer>();
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

            TRequest decoded;
            try
            {
                decoded = await WebApiBodyCodec
                    .DecodeBodyAsync<TRequest>(request.Body, messageContext, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                logger.LogInformation(
                    sre,
                    "Failed to decode {RequestType} body for route {Path}: {Status}",
                    typeof(TRequest).Name,
                    request.Path,
                    sre.StatusCode);
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

                logger.LogError(
                    "Unexpected response type {ActualType} for route {Path} (expected {ExpectedType}).",
                    dispatched?.GetType().FullName,
                    request.Path,
                    typeof(TResponse).FullName);
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to encode {ResponseType} body for route {Path}.",
                    typeof(TResponse).Name,
                    request.Path);
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }

        private static string? GetSingleAcceptHeader(HttpRequest request)
        {
            if (!request.Headers.TryGetValue("Accept", out StringValues values))
            {
                return null;
            }
            return values.Count > 0 ? values[0] : null;
        }
    }
}
