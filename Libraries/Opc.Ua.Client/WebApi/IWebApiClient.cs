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
        Task<TResponse> InvokeAsync<TRequest, TResponse>(
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
        Task<IServiceResponse> InvokeRouteAsync(
            WebApiServiceRoute route,
            IServiceRequest request,
            CancellationToken ct = default);

        // === Attribute service set (Part 4 §5.11) ============================

        /// <summary>Sends a Read request.</summary>
        Task<ReadResponse> ReadAsync(ReadRequest request, CancellationToken ct = default);

        /// <summary>Sends a Write request.</summary>
        Task<WriteResponse> WriteAsync(WriteRequest request, CancellationToken ct = default);

        /// <summary>Sends a HistoryRead request.</summary>
        Task<HistoryReadResponse> HistoryReadAsync(HistoryReadRequest request, CancellationToken ct = default);

        /// <summary>Sends a HistoryUpdate request.</summary>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(HistoryUpdateRequest request, CancellationToken ct = default);

        // === Method service set (Part 4 §5.12) ===============================

        /// <summary>Sends a Call request.</summary>
        Task<CallResponse> CallAsync(CallRequest request, CancellationToken ct = default);

        // === View service set (Part 4 §5.9) ==================================

        /// <summary>Sends a Browse request.</summary>
        Task<BrowseResponse> BrowseAsync(BrowseRequest request, CancellationToken ct = default);

        /// <summary>Sends a BrowseNext request.</summary>
        Task<BrowseNextResponse> BrowseNextAsync(BrowseNextRequest request, CancellationToken ct = default);

        /// <summary>Sends a TranslateBrowsePathsToNodeIds request.</summary>
        Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            TranslateBrowsePathsToNodeIdsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a RegisterNodes request.</summary>
        Task<RegisterNodesResponse> RegisterNodesAsync(
            RegisterNodesRequest request,
            CancellationToken ct = default);

        /// <summary>Sends an UnregisterNodes request.</summary>
        Task<UnregisterNodesResponse> UnregisterNodesAsync(
            UnregisterNodesRequest request,
            CancellationToken ct = default);

        // === Discovery service set (Part 4 §5.5) =============================

        /// <summary>Sends a FindServers request.</summary>
        Task<FindServersResponse> FindServersAsync(
            FindServersRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a GetEndpoints request.</summary>
        Task<GetEndpointsResponse> GetEndpointsAsync(
            GetEndpointsRequest request,
            CancellationToken ct = default);

        // === Session service set (Part 4 §5.7) ===============================

        /// <summary>Sends a CreateSession request.</summary>
        Task<CreateSessionResponse> CreateSessionAsync(
            CreateSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends an ActivateSession request.</summary>
        Task<ActivateSessionResponse> ActivateSessionAsync(
            ActivateSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a CloseSession request.</summary>
        Task<CloseSessionResponse> CloseSessionAsync(
            CloseSessionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Cancel request.</summary>
        Task<CancelResponse> CancelAsync(CancelRequest request, CancellationToken ct = default);

        // === MonitoredItem service set (Part 4 §5.13) ========================

        /// <summary>Sends a CreateMonitoredItems request.</summary>
        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            CreateMonitoredItemsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a ModifyMonitoredItems request.</summary>
        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            ModifyMonitoredItemsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetMonitoringMode request.</summary>
        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SetMonitoringModeRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetTriggering request.</summary>
        Task<SetTriggeringResponse> SetTriggeringAsync(
            SetTriggeringRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a DeleteMonitoredItems request.</summary>
        Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            DeleteMonitoredItemsRequest request,
            CancellationToken ct = default);

        // === Subscription service set (Part 4 §5.14) =========================

        /// <summary>Sends a CreateSubscription request.</summary>
        Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            CreateSubscriptionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a ModifySubscription request.</summary>
        Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            ModifySubscriptionRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a SetPublishingMode request.</summary>
        Task<SetPublishingModeResponse> SetPublishingModeAsync(
            SetPublishingModeRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a Publish request (server-side long-poll).</summary>
        Task<PublishResponse> PublishAsync(PublishRequest request, CancellationToken ct = default);

        /// <summary>Sends a Republish request.</summary>
        Task<RepublishResponse> RepublishAsync(RepublishRequest request, CancellationToken ct = default);

        /// <summary>Sends a TransferSubscriptions request.</summary>
        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            TransferSubscriptionsRequest request,
            CancellationToken ct = default);

        /// <summary>Sends a DeleteSubscriptions request.</summary>
        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            DeleteSubscriptionsRequest request,
            CancellationToken ct = default);
    }
}
