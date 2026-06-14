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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// Session-bound REST API client that injects the activated session
    /// authentication token into all service requests.
    /// </summary>
    public interface IWebApiSession : IAsyncDisposable
    {
        /// <summary>
        /// The OPC UA session id assigned by the server.
        /// </summary>
        NodeId SessionId { get; }

        /// <summary>
        /// The revised session timeout returned by CreateSession.
        /// </summary>
        double RevisedSessionTimeout { get; }

        /// <summary>
        /// Whether the REST session has been activated and is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Notification messages received by the auto-publish loop.
        /// </summary>
        IAsyncEnumerable<NotificationMessage> Notifications { get; }

        /// <summary>
        /// Raised when the REST session connection state changes.
        /// </summary>
        event EventHandler<WebApiSessionStateEventArgs>? SessionStateChanged;

        /// <summary>
        /// Creates and activates the REST-backed OPC UA session.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when the session is connected.</returns>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>Sends a Read request.</summary>
        Task<ReadResponse> ReadAsync(ReadRequest request, CancellationToken ct = default);

        /// <summary>Sends a Write request.</summary>
        Task<WriteResponse> WriteAsync(WriteRequest request, CancellationToken ct = default);

        /// <summary>Sends a HistoryRead request.</summary>
        Task<HistoryReadResponse> HistoryReadAsync(HistoryReadRequest request, CancellationToken ct = default);

        /// <summary>Sends a HistoryUpdate request.</summary>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(HistoryUpdateRequest request, CancellationToken ct = default);

        /// <summary>Sends a Call request.</summary>
        Task<CallResponse> CallAsync(CallRequest request, CancellationToken ct = default);

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

        /// <summary>Sends a Cancel request.</summary>
        Task<CancelResponse> CancelAsync(CancelRequest request, CancellationToken ct = default);

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

        /// <summary>Sends a Publish request.</summary>
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
