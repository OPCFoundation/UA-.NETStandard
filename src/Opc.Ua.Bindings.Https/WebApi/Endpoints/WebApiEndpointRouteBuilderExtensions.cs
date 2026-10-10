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
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Bindings.WebApi.Endpoints;
using Opc.Ua.Schema.OpenApi;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Minimal-API extensions that wire the OPC UA REST binding into
    /// an <see cref="IEndpointRouteBuilder"/>. Mirrors the
    /// <c>opc.ua.openapi.allservices.json</c> spec mapping (OPC UA
    /// Part 6 §G.3) without any reflection-based controller
    /// discovery, so the binding is fully NativeAOT-compatible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each spec service is mapped to a static
    /// <see cref="RequestDelegate"/> that closes over the
    /// <c>&lt;Service&gt;Request</c> / <c>&lt;Service&gt;Response</c>
    /// CLR type pair via a generic instantiation of
    /// <c>WebApiEndpointDispatcher.HandleAsync</c>. All 28
    /// instantiations are visible to the trimmer at compile time; no
    /// <c>RequiresUnreferencedCode</c>, <c>RequiresDynamicCode</c>,
    /// or <c>UnconditionalSuppressMessage</c> attributes are needed.
    /// </para>
    /// <para>
    /// The bound endpoints carry no authorization metadata. Identity
    /// flows through the <c>ISessionlessIdentityProvider</c>
    /// (resolved from <see cref="HttpContext.RequestServices"/>) and
    /// rides on the OPC UA <c>RequestHeader.AuthenticationToken</c>
    /// for session-based services; the binding never short-circuits
    /// requests at the HTTP layer.
    /// </para>
    /// </remarks>
    public static class WebApiEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the 28 OPC UA REST service routes onto
        /// <paramref name="endpoints"/>. Each route is a POST handler
        /// that decodes the body as the spec request, dispatches to
        /// <c>IWebApiServer.InvokeAsync</c>, and encodes the response.
        /// </summary>
        /// <param name="endpoints">
        /// The endpoint route builder. Must not be <c>null</c>.
        /// </param>
        /// <returns>
        /// A grouped endpoint convention builder so callers can apply
        /// shared conventions (e.g.
        /// <c>RequireAuthorization()</c>) to all WebApi routes at
        /// once.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="endpoints"/> is <c>null</c>.
        /// </exception>
        public static IEndpointConventionBuilder MapWebApiEndpoints(
            this IEndpointRouteBuilder endpoints)
        {
            return MapWebApiEndpoints(endpoints, new WebApiTransportOptions());
        }

        /// <summary>
        /// Maps the OPC UA REST service routes selected by
        /// <see cref="WebApiTransportOptions.ServiceSet"/> onto
        /// <paramref name="endpoints"/> and, when
        /// <see cref="WebApiTransportOptions.OpenApiDocumentPath"/> is set,
        /// a <c>GET</c> route that serves the OpenAPI document of the
        /// service set.
        /// </summary>
        /// <param name="endpoints">
        /// The endpoint route builder. Must not be <c>null</c>.
        /// </param>
        /// <param name="options">
        /// The options that select the service set and the document.
        /// Only <see cref="WebApiTransportOptions.ServiceSet"/>,
        /// <see cref="WebApiTransportOptions.OpenApiDocumentPath"/> and
        /// <see cref="WebApiTransportOptions.OpenApiIncludeSchemas"/> are used.
        /// </param>
        /// <returns>
        /// A grouped endpoint convention builder so callers can apply
        /// shared conventions (e.g. <c>RequireAuthorization()</c>) to all
        /// routes, the document route included, at once.
        /// </returns>
        /// <remarks>
        /// The document route is part of the group and carries no
        /// <c>AllowAnonymous</c> metadata: it requires whatever the group
        /// requires, like the service routes. The document is generated by
        /// the <see cref="WebApiOpenApiGenerator"/> registered with the
        /// request services, or by a default generator when none is.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="endpoints"/> or <paramref name="options"/> is
        /// <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <see cref="WebApiTransportOptions.ServiceSet"/> is not a defined
        /// value.
        /// </exception>
        public static IEndpointConventionBuilder MapWebApiEndpoints(
            this IEndpointRouteBuilder endpoints,
            WebApiTransportOptions options)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(options);
            if (!Enum.IsDefined(options.ServiceSet))
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            RouteGroupBuilder group = endpoints.MapGroup(string.Empty);
            bool allServices = options.ServiceSet == WebApiServiceSet.AllServices;

            // Discovery routes are anonymous-exempt per OPC UA spec:
            // FindServers / GetEndpoints must be callable without
            // authentication so clients can discover the server's
            // endpoints and user-token policies before establishing a
            // session. Mark them explicitly so they bypass any
            // RequireAuthorization() metadata applied at the group
            // level by the contributor.
            if (allServices)
            {
                group.MapPost("/findservers", FindServersAsync).AllowAnonymous();
                group.MapPost("/getendpoints", GetEndpointsAsync).AllowAnonymous();
            }

            // All other services are subject to authorization when an
            // auth scheme is registered. The contributor decides
            // whether to apply RequireAuthorization() to the group;
            // when no auth scheme is registered, no metadata is added
            // and the historical anonymous flow is preserved.
            group.MapPost("/read", ReadAsync);
            group.MapPost("/write", WriteAsync);
            group.MapPost("/historyread", HistoryReadAsync);
            group.MapPost("/historyupdate", HistoryUpdateAsync);
            group.MapPost("/call", CallAsync);
            group.MapPost("/browse", BrowseAsync);
            group.MapPost("/browsenext", BrowseNextAsync);
            group.MapPost("/translate", TranslateAsync);

            // The eight services above are the routes of
            // opc.ua.openapi.sessionless.json.
            if (!allServices)
            {
                return MapOpenApiDocument(group, options);
            }

            group.MapPost("/registernodes", RegisterNodesAsync);
            group.MapPost("/unregisternodes", UnregisterNodesAsync);
            group.MapPost("/createsession", CreateSessionAsync);
            group.MapPost("/activatesession", ActivateSessionAsync);
            group.MapPost("/closesession", CloseSessionAsync);
            group.MapPost("/cancel", CancelAsync);
            group.MapPost("/createmonitoreditems", CreateMonitoredItemsAsync);
            group.MapPost("/modifymonitoreditems", ModifyMonitoredItemsAsync);
            group.MapPost("/setmonitoringmode", SetMonitoringModeAsync);
            group.MapPost("/settriggering", SetTriggeringAsync);
            group.MapPost("/deletemonitoreditems", DeleteMonitoredItemsAsync);
            group.MapPost("/createsubscription", CreateSubscriptionAsync);
            group.MapPost("/modifysubscription", ModifySubscriptionAsync);
            group.MapPost("/setpublishingmode", SetPublishingModeAsync);
            group.MapPost("/publish", PublishAsync);
            group.MapPost("/republish", RepublishAsync);
            group.MapPost("/transfersubscriptions", TransferSubscriptionsAsync);
            group.MapPost("/deletesubscriptions", DeleteSubscriptionsAsync);

            return MapOpenApiDocument(group, options);
        }

        private static RouteGroupBuilder MapOpenApiDocument(
            RouteGroupBuilder group,
            WebApiTransportOptions options)
        {
            if (string.IsNullOrEmpty(options.OpenApiDocumentPath))
            {
                return group;
            }

            // The route joins the group of the service routes without any
            // AllowAnonymous metadata: whatever the group requires, such as
            // the authorization the contributor adds when an authentication
            // scheme is registered, the document requires too.
            var document = new WebApiOpenApiDocument(options.ServiceSet, options.OpenApiIncludeSchemas);
            var fallback = new Lazy<WebApiOpenApiGenerator>(() => new WebApiOpenApiGenerator());
            group.MapGet(
                options.OpenApiDocumentPath,
                context => WriteOpenApiDocumentAsync(context, document, fallback));
            return group;
        }

        private static Task WriteOpenApiDocumentAsync(
            HttpContext context,
            WebApiOpenApiDocument document,
            Lazy<WebApiOpenApiGenerator> fallback)
        {
            WebApiOpenApiGenerator generator =
                context.RequestServices.GetService<WebApiOpenApiGenerator>() ?? fallback.Value;

            // OpenAPI resolves a relative server URL against the location the
            // document was loaded from; without a path base the default of
            // "/" is right and the list is omitted.
            PathString pathBase = context.Request.PathBase;
            ReadOnlyMemory<byte> bytes = document.Get(
                generator,
                pathBase.HasValue ? pathBase.Value + "/" : null).Memory;

            HttpResponse response = context.Response;
            response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status200OK;
            response.ContentType = WebApiMediaType.ContentType;
            response.ContentLength = bytes.Length;
            return response.Body.WriteAsync(bytes, context.RequestAborted).AsTask();
        }

        private static Task ReadAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<ReadRequest, ReadResponse>(ctx);
        }

        private static Task WriteAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<WriteRequest, WriteResponse>(ctx);
        }

        private static Task HistoryReadAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<HistoryReadRequest, HistoryReadResponse>(ctx);
        }

        private static Task HistoryUpdateAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<HistoryUpdateRequest, HistoryUpdateResponse>(ctx);
        }

        private static Task CallAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<CallRequest, CallResponse>(ctx);
        }

        private static Task BrowseAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<BrowseRequest, BrowseResponse>(ctx);
        }

        private static Task BrowseNextAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<BrowseNextRequest, BrowseNextResponse>(ctx);
        }

        private static Task TranslateAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        TranslateBrowsePathsToNodeIdsRequest,
                        TranslateBrowsePathsToNodeIdsResponse>(ctx);
        }

        private static Task RegisterNodesAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<RegisterNodesRequest, RegisterNodesResponse>(ctx);
        }

        private static Task UnregisterNodesAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<UnregisterNodesRequest, UnregisterNodesResponse>(ctx);
        }

        private static Task FindServersAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<FindServersRequest, FindServersResponse>(ctx);
        }

        private static Task GetEndpointsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<GetEndpointsRequest, GetEndpointsResponse>(ctx);
        }

        private static Task CreateSessionAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<CreateSessionRequest, CreateSessionResponse>(ctx);
        }

        private static Task ActivateSessionAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<ActivateSessionRequest, ActivateSessionResponse>(ctx);
        }

        private static Task CloseSessionAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<CloseSessionRequest, CloseSessionResponse>(ctx);
        }

        private static Task CancelAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<CancelRequest, CancelResponse>(ctx);
        }

        private static Task CreateMonitoredItemsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        CreateMonitoredItemsRequest,
                        CreateMonitoredItemsResponse>(ctx);
        }

        private static Task ModifyMonitoredItemsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        ModifyMonitoredItemsRequest,
                        ModifyMonitoredItemsResponse>(ctx);
        }

        private static Task SetMonitoringModeAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        SetMonitoringModeRequest,
                        SetMonitoringModeResponse>(ctx);
        }

        private static Task SetTriggeringAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<SetTriggeringRequest, SetTriggeringResponse>(ctx);
        }

        private static Task DeleteMonitoredItemsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        DeleteMonitoredItemsRequest,
                        DeleteMonitoredItemsResponse>(ctx);
        }

        private static Task CreateSubscriptionAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        CreateSubscriptionRequest,
                        CreateSubscriptionResponse>(ctx);
        }

        private static Task ModifySubscriptionAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        ModifySubscriptionRequest,
                        ModifySubscriptionResponse>(ctx);
        }

        private static Task SetPublishingModeAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        SetPublishingModeRequest,
                        SetPublishingModeResponse>(ctx);
        }

        private static Task PublishAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<PublishRequest, PublishResponse>(ctx);
        }

        private static Task RepublishAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<RepublishRequest, RepublishResponse>(ctx);
        }

        private static Task TransferSubscriptionsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        TransferSubscriptionsRequest,
                        TransferSubscriptionsResponse>(ctx);
        }

        private static Task DeleteSubscriptionsAsync(HttpContext ctx)
        {
            return WebApiEndpointDispatcher.HandleAsync<
                        DeleteSubscriptionsRequest,
                        DeleteSubscriptionsResponse>(ctx);
        }
    }
}
#endif
