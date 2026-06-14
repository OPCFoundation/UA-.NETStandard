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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Rest.Controllers
{
    /// <summary>
    /// Abstract base for the OPC UA REST MVC controllers. Owns the
    /// envelope-less encode / decode loop, the media-type-parameter
    /// based encoding negotiation
    /// (<see cref="RestApiMediaType"/>), and the dispatch hand-off to
    /// <see cref="IRestApiServer"/>. Concrete controllers expose one
    /// <c>[HttpPost("&lt;path&gt;")]</c> action per service that calls
    /// <see cref="ExecuteAsync{TRequest, TResponse}(CancellationToken)"/>
    /// with the typed request / response pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Faults are returned as a typed <c>&lt;Service&gt;Response</c>
    /// with the <see cref="ResponseHeader.ServiceResult"/> populated;
    /// the HTTP status stays <c>200</c> (consistent with the existing
    /// HTTPS-JSON binding behaviour and what generated OpenAPI clients
    /// expect). Decode failures surface as HTTP <c>400</c> because the
    /// concrete CLR response type cannot be built without a usable
    /// request, and transport errors surface as HTTP <c>500</c>.
    /// </para>
    /// </remarks>
    public abstract class RestApiControllerBase : ControllerBase
    {
        private readonly IRestApiServer m_server;
        private readonly ISessionlessIdentityProvider? m_identityProvider;
        private readonly ILogger m_logger;

        /// <summary>
        /// Initializes the base controller. ASP.NET Core's controller
        /// activator resolves the parameters from DI.
        /// </summary>
        /// <param name="server">The REST dispatcher.</param>
        /// <param name="loggerFactory">Logger factory.</param>
        /// <param name="identityProvider">
        /// Optional identity provider that maps the
        /// ASP.NET Core <see cref="HttpContext.User"/> to an OPC UA
        /// <see cref="IUserIdentity"/>. When <c>null</c>, controllers
        /// treat every request as anonymous.
        /// </param>
        protected RestApiControllerBase(
            IRestApiServer server,
            ILoggerFactory loggerFactory,
            ISessionlessIdentityProvider? identityProvider = null)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            m_server = server;
            m_logger = loggerFactory.CreateLogger(GetType());
            m_identityProvider = identityProvider;
        }

        /// <summary>
        /// Per-controller hook to enrich the
        /// <see cref="RestApiInvocationContext"/> handed to
        /// <see cref="IRestApiServer.InvokeAsync(IServiceRequest, RestApiInvocationContext, CancellationToken)"/>.
        /// Default implementation populates transport-level fields plus
        /// the identity resolved by
        /// <see cref="ISessionlessIdentityProvider"/> (if registered).
        /// </summary>
        /// <returns>The invocation context.</returns>
        protected virtual RestApiInvocationContext BuildInvocationContext()
        {
            return new RestApiInvocationContext
            {
                SecureChannelId = HttpContext.TraceIdentifier,
                Endpoint = null,
                ClientCertificate = HttpContext.Connection.ClientCertificate?.RawData,
                ServerCertificate = null,
                Identity = m_identityProvider?.Resolve(HttpContext)
            };
        }

        /// <summary>
        /// Decodes the request body as <typeparamref name="TRequest"/>,
        /// invokes the dispatcher, and writes the typed
        /// <typeparamref name="TResponse"/> back as the response body.
        /// </summary>
        /// <typeparam name="TRequest">
        /// Concrete CLR request type for the bound route (e.g.
        /// <c>ReadRequest</c>).
        /// </typeparam>
        /// <typeparam name="TResponse">
        /// Concrete CLR response type for the bound route (e.g.
        /// <c>ReadResponse</c>).
        /// </typeparam>
        /// <param name="ct">Cancellation token.</param>
        protected async Task ExecuteAsync<TRequest, TResponse>(CancellationToken ct)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new()
        {
            RestApiEncoding encoding = RestApiMediaType.ParseEncoding(Request.ContentType);
            RestApiEncoding acceptEncoding =
                RestApiMediaType.ParseEncoding(GetSingleAcceptHeader(), fallback: encoding);

            IServiceMessageContext messageContext = m_server.MessageContext;

            TRequest request;
            try
            {
                request = await RestApiBodyCodec
                    .DecodeBodyAsync<TRequest>(Request.Body, messageContext, ct: ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                m_logger.LogInformation(
                    sre,
                    "Failed to decode {RequestType} body for route {Path}: {Status}",
                    typeof(TRequest).Name,
                    Request.Path,
                    sre.StatusCode);
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                Response.ContentLength = 0;
                return;
            }

            RestApiInvocationContext context = BuildInvocationContext();
            IServiceResponse response = await m_server
                .InvokeAsync(request, context, ct)
                .ConfigureAwait(false);

            JsonEncoderOptions encoderOptions = RestApiMediaType.ToEncoderOptions(acceptEncoding);
            Response.StatusCode = (int)HttpStatusCode.OK;
            Response.ContentType = RestApiMediaType.FormatContentType(acceptEncoding);

            try
            {
                if (response is TResponse typed)
                {
                    await RestApiBodyCodec
                        .EncodeBodyAsync(typed, Response.Body, messageContext, encoderOptions, ct)
                        .ConfigureAwait(false);
                    return;
                }

                // ServiceFault is the only legitimate substitute for a typed
                // response; serialize it directly so the HTTP body always
                // carries a parseable OPC UA payload.
                if (response is ServiceFault fault)
                {
                    await RestApiBodyCodec
                        .EncodeBodyAsync(fault, Response.Body, messageContext, encoderOptions, ct)
                        .ConfigureAwait(false);
                    return;
                }

                m_logger.LogError(
                    "Unexpected response type {ActualType} for route {Path} (expected {ExpectedType}).",
                    response?.GetType().FullName,
                    Request.Path,
                    typeof(TResponse).FullName);
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Failed to encode {ResponseType} body for route {Path}.",
                    typeof(TResponse).Name,
                    Request.Path);
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }

        private string? GetSingleAcceptHeader()
        {
            if (!Request.Headers.TryGetValue("Accept", out Microsoft.Extensions.Primitives.StringValues values))
            {
                return null;
            }
            return values.Count > 0 ? values[0] : null;
        }
    }
}
