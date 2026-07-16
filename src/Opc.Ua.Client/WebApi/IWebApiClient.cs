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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// Symmetric C# client for the OPC UA REST binding
    /// (OPC UA Part 6 §G.3 "OpenAPI Mapping"). One async method per
    /// service mirrors the server-side
    /// <see cref="WebApiServiceRoutes"/> table; calls round-trip the
    /// envelope-less <c>&lt;Service&gt;Request</c> /
    /// <c>&lt;Service&gt;Response</c> bodies via
    /// <see cref="WebApiBodyCodec"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client is multi-TFM (mirrors <c>Opc.Ua.Client</c>) and uses
    /// <c>System.Net.Http.HttpClient</c> for transport. The encoding
    /// flavour (Compact / Verbose) and authentication callbacks come
    /// from <see cref="WebApiClientOptions"/>. Build a client via
    /// <see cref="WebApiClient.Create(System.Uri, WebApiClientOptions?)"/>
    /// or the fluent builder.
    /// </para>
    /// <para>
    /// Sessionless services (Read, Write, Browse, etc.) work out of the
    /// box. Session-based services require a paired
    /// <c>CreateSession</c> + <c>ActivateSession</c> sequence; the
    /// <c>UseSession</c> wrapper handles the orchestration and
    /// injects the activation token into subsequent requests.
    /// </para>
    /// <para>
    /// <b>Relationship to <c>ISessionClientMethods</c>.</b> The per-service
    /// async methods on this interface mirror the source-generated
    /// <c>ISessionClientMethods</c> contract one-for-one in name and
    /// shape — every method takes a <c>&lt;Service&gt;Request</c> envelope
    /// and a <c>CancellationToken</c>, and returns a
    /// <c>ValueTask&lt;&lt;Service&gt;Response&gt;</c>. The two interfaces are
    /// kept distinct (rather than having <see cref="IWebApiClient"/>
    /// inherit <c>ISessionClientMethods</c>) for two reasons:
    /// (1) the WebApi binding is sessionless at the transport layer —
    /// session lifecycle (CreateSession / ActivateSession / token
    /// stitching) is layered on top of <c>HttpClient</c> and is not
    /// modelled by <c>ISessionClient</c>, which assumes a long-lived
    /// secure-channel session; and
    /// (2) the WebApi adds a route-driven <see cref="InvokeRouteAsync"/>
    /// + generic <c>InvokeAsync&lt;TRequest, TResponse&gt;</c> escape
    /// hatch (for non-AOT and AOT consumers respectively) that has no
    /// counterpart in the secure-channel client. Aligning the return
    /// types to <c>ValueTask&lt;T&gt;</c> matches the rest of the modern
    /// client stack (see <c>SessionClientBatched</c>,
    /// <c>ManagedSession.Services</c>) so callers can route through
    /// either contract identically.
    /// </para>
    /// </remarks>
    public interface IWebApiClient
    {
        /// <summary>
        /// The base URI the client targets (e.g. <c>https://server:4843/</c>).
        /// Service paths from
        /// <see cref="WebApiServiceRoutes"/> are appended to this URI.
        /// </summary>
        System.Uri BaseAddress { get; }

        /// <summary>
        /// The encoding flavour advertised on outbound requests and used
        /// to decode responses (default Compact, per Part 6 §5.4.9).
        /// </summary>
        WebApiEncoding Encoding { get; }

        /// <summary>
        /// Generic invocation of a service identified by the request's
        /// concrete CLR type. Resolves the matching route through
        /// <see cref="WebApiServiceRoutes"/> and POSTs the body to
        /// <c>BaseAddress + route.Path</c>.
        /// </summary>
        /// <typeparam name="TRequest">The concrete request type.</typeparam>
        /// <typeparam name="TResponse">The concrete response type.</typeparam>
        /// <param name="request">The request body.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The decoded response.</returns>
        ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken ct = default)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new();

        /// <summary>
        /// Non-generic invocation by explicit route. Used by
        /// <see cref="WebApiTransportChannel"/> to dispatch on the
        /// runtime CLR type of the request.
        /// </summary>
        /// <param name="route">The Web API route describing path,
        /// request type, and response type.</param>
        /// <param name="request">The request body; must be an instance
        /// of <see cref="WebApiServiceRoute.RequestType"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The decoded <see cref="IServiceResponse"/>.</returns>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Constructs route.ResponseType via Activator.CreateInstance. AOT consumers " +
            "should use the generic InvokeAsync<TRequest, TResponse> overload instead.")]
        ValueTask<IServiceResponse> InvokeRouteAsync(
            WebApiServiceRoute route,
            IServiceRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Read request.</summary>
        ValueTask<ReadResponse> ReadAsync(
            ReadRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Write request.</summary>
        ValueTask<WriteResponse> WriteAsync(
            WriteRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a HistoryRead request.</summary>
        ValueTask<HistoryReadResponse> HistoryReadAsync(
            HistoryReadRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a HistoryUpdate request.</summary>
        ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            HistoryUpdateRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Call request.</summary>
        ValueTask<CallResponse> CallAsync(
            CallRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Browse request.</summary>
        ValueTask<BrowseResponse> BrowseAsync(
            BrowseRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a BrowseNext request.</summary>
        ValueTask<BrowseNextResponse> BrowseNextAsync(
            BrowseNextRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a TranslateBrowsePathsToNodeIds request.</summary>
        ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            TranslateBrowsePathsToNodeIdsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a RegisterNodes request.</summary>
        ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            RegisterNodesRequest request,
            CancellationToken ct = default);

        /// <summary>Sends an UnregisterNodes request.</summary>
        ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            UnregisterNodesRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a FindServers request.</summary>
        ValueTask<FindServersResponse> FindServersAsync(
            FindServersRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a GetEndpoints request.</summary>
        ValueTask<GetEndpointsResponse> GetEndpointsAsync(
            GetEndpointsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a CreateSession request.</summary>
        ValueTask<CreateSessionResponse> CreateSessionAsync(
            CreateSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends an ActivateSession request.</summary>
        ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            ActivateSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a CloseSession request.</summary>
        ValueTask<CloseSessionResponse> CloseSessionAsync(
            CloseSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Cancel request.</summary>
        ValueTask<CancelResponse> CancelAsync(
            CancelRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a CreateMonitoredItems request.</summary>
        ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            CreateMonitoredItemsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a ModifyMonitoredItems request.</summary>
        ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            ModifyMonitoredItemsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetMonitoringMode request.</summary>
        ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SetMonitoringModeRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetTriggering request.</summary>
        ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            SetTriggeringRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a DeleteMonitoredItems request.</summary>
        ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            DeleteMonitoredItemsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a CreateSubscription request.</summary>
        ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            CreateSubscriptionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a ModifySubscription request.</summary>
        ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            ModifySubscriptionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetPublishingMode request.</summary>
        ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            SetPublishingModeRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Publish request (server-side long-poll).</summary>
        ValueTask<PublishResponse> PublishAsync(
            PublishRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Republish request.</summary>
        ValueTask<RepublishResponse> RepublishAsync(
            RepublishRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a TransferSubscriptions request.</summary>
        ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            TransferSubscriptionsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a DeleteSubscriptions request.</summary>
        ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            DeleteSubscriptionsRequest request,
            CancellationToken ct = default);
    }
}
