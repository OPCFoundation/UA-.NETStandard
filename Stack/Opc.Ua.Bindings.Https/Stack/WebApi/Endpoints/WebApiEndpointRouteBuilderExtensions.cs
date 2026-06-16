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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Opc.Ua;
using Opc.Ua.Bindings.WebApi.Endpoints;

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
            ArgumentNullException.ThrowIfNull(endpoints);

            RouteGroupBuilder group = endpoints.MapGroup(string.Empty);
            group.MapPost("/read", ReadAsync);
            group.MapPost("/write", WriteAsync);
            group.MapPost("/historyread", HistoryReadAsync);
            group.MapPost("/historyupdate", HistoryUpdateAsync);
            group.MapPost("/call", CallAsync);
            group.MapPost("/browse", BrowseAsync);
            group.MapPost("/browsenext", BrowseNextAsync);
            group.MapPost("/translate", TranslateAsync);
            group.MapPost("/registernodes", RegisterNodesAsync);
            group.MapPost("/unregisternodes", UnregisterNodesAsync);
            group.MapPost("/findservers", FindServersAsync);
            group.MapPost("/getendpoints", GetEndpointsAsync);
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

            return group;
        }

        private static Task ReadAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<ReadRequest, ReadResponse>(ctx);

        private static Task WriteAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<WriteRequest, WriteResponse>(ctx);

        private static Task HistoryReadAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<HistoryReadRequest, HistoryReadResponse>(ctx);

        private static Task HistoryUpdateAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<HistoryUpdateRequest, HistoryUpdateResponse>(ctx);

        private static Task CallAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<CallRequest, CallResponse>(ctx);

        private static Task BrowseAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<BrowseRequest, BrowseResponse>(ctx);

        private static Task BrowseNextAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<BrowseNextRequest, BrowseNextResponse>(ctx);

        private static Task TranslateAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                TranslateBrowsePathsToNodeIdsRequest,
                TranslateBrowsePathsToNodeIdsResponse>(ctx);

        private static Task RegisterNodesAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<RegisterNodesRequest, RegisterNodesResponse>(ctx);

        private static Task UnregisterNodesAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<UnregisterNodesRequest, UnregisterNodesResponse>(ctx);

        private static Task FindServersAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<FindServersRequest, FindServersResponse>(ctx);

        private static Task GetEndpointsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<GetEndpointsRequest, GetEndpointsResponse>(ctx);

        private static Task CreateSessionAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<CreateSessionRequest, CreateSessionResponse>(ctx);

        private static Task ActivateSessionAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<ActivateSessionRequest, ActivateSessionResponse>(ctx);

        private static Task CloseSessionAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<CloseSessionRequest, CloseSessionResponse>(ctx);

        private static Task CancelAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<CancelRequest, CancelResponse>(ctx);

        private static Task CreateMonitoredItemsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                CreateMonitoredItemsRequest,
                CreateMonitoredItemsResponse>(ctx);

        private static Task ModifyMonitoredItemsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                ModifyMonitoredItemsRequest,
                ModifyMonitoredItemsResponse>(ctx);

        private static Task SetMonitoringModeAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                SetMonitoringModeRequest,
                SetMonitoringModeResponse>(ctx);

        private static Task SetTriggeringAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<SetTriggeringRequest, SetTriggeringResponse>(ctx);

        private static Task DeleteMonitoredItemsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                DeleteMonitoredItemsRequest,
                DeleteMonitoredItemsResponse>(ctx);

        private static Task CreateSubscriptionAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                CreateSubscriptionRequest,
                CreateSubscriptionResponse>(ctx);

        private static Task ModifySubscriptionAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                ModifySubscriptionRequest,
                ModifySubscriptionResponse>(ctx);

        private static Task SetPublishingModeAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                SetPublishingModeRequest,
                SetPublishingModeResponse>(ctx);

        private static Task PublishAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<PublishRequest, PublishResponse>(ctx);

        private static Task RepublishAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<RepublishRequest, RepublishResponse>(ctx);

        private static Task TransferSubscriptionsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                TransferSubscriptionsRequest,
                TransferSubscriptionsResponse>(ctx);

        private static Task DeleteSubscriptionsAsync(HttpContext ctx)
            => WebApiEndpointDispatcher.HandleAsync<
                DeleteSubscriptionsRequest,
                DeleteSubscriptionsResponse>(ctx);
    }
}
#endif
