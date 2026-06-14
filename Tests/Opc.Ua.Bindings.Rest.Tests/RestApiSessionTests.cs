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
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Rest;

namespace Opc.Ua.Bindings.Rest.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RestApiSession"/>.
    /// </summary>
    [TestFixture]
    [Category("RestApiSession")]
    public sealed class RestApiSessionTests
    {
        [Test]
        [CancelAfter(10_000)]
        public async Task OpenAsyncCallsCreateSessionThenActivateSession(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            var session = new RestApiSession(client, CreateOptions());
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);

                Assert.That(client.Calls, Has.Count.EqualTo(2));
                Assert.That(client.Calls[0], Is.EqualTo("CreateSession"));
                Assert.That(client.Calls[1], Is.EqualTo("ActivateSession"));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task MethodsAutoInjectAuthenticationToken(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            var session = new RestApiSession(client, CreateOptions());
            try
            {
                await session.ReadAsync(new ReadRequest(), ct).ConfigureAwait(false);

                Assert.That(client.LastReadRequest, Is.Not.Null);
                Assert.That(
                    client.LastReadRequest!.RequestHeader.AuthenticationToken,
                    Is.EqualTo(client.AuthenticationToken));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task KeepAliveTimerPostsReadOnInterval(CancellationToken ct)
        {
            var timeProvider = new FakeTimeProvider();
            var client = new StubRestApiClient { RevisedSessionTimeout = 40 };
            RestApiSessionOptions options = CreateOptions(timeProvider);
            var session = new RestApiSession(client, options);
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);
                timeProvider.Advance(TimeSpan.FromMilliseconds(10));
                await WaitUntilAsync(() => client.ReadCount > 0, ct).ConfigureAwait(false);

                Assert.That(client.ReadCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task AutoPublishKeepsNMaxConcurrentInFlight(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            int active = 0;
            int maxActive = 0;
            int started = 0;
            client.PublishHandler = async (_, token) =>
            {
                int current = Interlocked.Increment(ref active);
                UpdateMax(ref maxActive, current);
                Interlocked.Increment(ref started);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }

                return StubRestApiClient.CreatePublishResponse(1, 1);
            };

            var session = new RestApiSession(
                client,
                CreateOptions(autoPublish: true, maxConcurrentPublish: 3));
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);
                await WaitUntilAsync(() => Volatile.Read(ref started) >= 3, ct).ConfigureAwait(false);

                Assert.That(maxActive, Is.EqualTo(3));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task AutoPublishQueuesNotificationsForReaders(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            int calls = 0;
            client.PublishHandler = async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return StubRestApiClient.CreatePublishResponse(7, 123);
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return StubRestApiClient.CreatePublishResponse(7, 124);
            };

            var session = new RestApiSession(
                client,
                CreateOptions(autoPublish: true, maxConcurrentPublish: 1));
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);
                NotificationMessage notification = await ReadNextNotificationAsync(session, ct).ConfigureAwait(false);

                Assert.That(notification.SequenceNumber, Is.EqualTo(123u));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task AutoPublishAcksOnNextPublish(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            Channel<PublishRequest> publishRequests = Channel.CreateUnbounded<PublishRequest>();
            int calls = 0;
            client.PublishHandler = async (request, token) =>
            {
                await publishRequests.Writer.WriteAsync(request, token).ConfigureAwait(false);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return StubRestApiClient.CreatePublishResponse(77, 55);
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return StubRestApiClient.CreatePublishResponse(77, 56);
            };

            var session = new RestApiSession(
                client,
                CreateOptions(autoPublish: true, maxConcurrentPublish: 1));
            try
            {
                await session.OpenAsync(ct).ConfigureAwait(false);
                PublishRequest first = await publishRequests.Reader.ReadAsync(ct).ConfigureAwait(false);
                PublishRequest second = await publishRequests.Reader.ReadAsync(ct).ConfigureAwait(false);

                Assert.That(first.SubscriptionAcknowledgements, Has.Count.EqualTo(0));
                Assert.That(second.SubscriptionAcknowledgements, Has.Count.EqualTo(1));
                Assert.That(second.SubscriptionAcknowledgements[0].SubscriptionId, Is.EqualTo(77u));
                Assert.That(second.SubscriptionAcknowledgements[0].SequenceNumber, Is.EqualTo(55u));
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task DisposeAsyncCallsCloseSessionWithDeleteSubscriptionsTrue(CancellationToken ct)
        {
            var client = new StubRestApiClient();
            var session = new RestApiSession(client, CreateOptions());

            await session.OpenAsync(ct).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);

            Assert.That(client.LastCloseSessionRequest, Is.Not.Null);
            Assert.That(client.LastCloseSessionRequest!.DeleteSubscriptions, Is.True);
        }

        [Test]
        [CancelAfter(10_000)]
        public async Task DisposeAsyncStopsKeepAliveAndPublishLoop(CancellationToken ct)
        {
            var timeProvider = new FakeTimeProvider();
            var client = new StubRestApiClient { RevisedSessionTimeout = 40 };
            int publishStarted = 0;
            int publishCanceled = 0;
            client.PublishHandler = async (_, token) =>
            {
                Interlocked.Increment(ref publishStarted);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref publishCanceled);
                    throw;
                }

                return StubRestApiClient.CreatePublishResponse(1, 1);
            };

            var session = new RestApiSession(
                client,
                CreateOptions(timeProvider, autoPublish: true, maxConcurrentPublish: 1));

            await session.OpenAsync(ct).ConfigureAwait(false);
            await WaitUntilAsync(() => Volatile.Read(ref publishStarted) > 0, ct).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            int readCount = client.ReadCount;

            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(25, ct).ConfigureAwait(false);

            Assert.That(client.ReadCount, Is.EqualTo(readCount));
            Assert.That(publishCanceled, Is.GreaterThanOrEqualTo(1));
        }

        private static RestApiSessionOptions CreateOptions(
            TimeProvider? timeProvider = null,
            bool autoPublish = false,
            int maxConcurrentPublish = 2)
        {
            return new RestApiSessionOptions
            {
                Identity = new UserIdentity(),
                RequestedSessionTimeout = 60_000,
                KeepAliveDivisor = 4.0,
                AutoPublish = autoPublish,
                MaxConcurrentPublish = maxConcurrentPublish,
                TimeProvider = timeProvider
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
        {
            while (!condition())
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }

        private static async Task<NotificationMessage> ReadNextNotificationAsync(
            RestApiSession session,
            CancellationToken ct)
        {
            await foreach (NotificationMessage notification in session.Notifications.WithCancellation(ct)
                .ConfigureAwait(false))
            {
                return notification;
            }

            throw new InvalidOperationException("Notification stream completed before a message was received.");
        }

        private static void UpdateMax(ref int max, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref max);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref max, value, current) != current);
        }

        private sealed class StubRestApiClient : IRestApiClient
        {
            private readonly System.Threading.Lock m_lock = new();
            private readonly List<string> m_calls = [];
            private int m_readCount;

            public Uri BaseAddress { get; } = new("https://localhost:4843/");

            public RestApiEncoding Encoding => RestApiEncoding.Compact;

            public NodeId SessionId { get; } = new(1234u);

            public NodeId AuthenticationToken { get; } = new(5678u);

            public double RevisedSessionTimeout { get; set; } = 60_000;

            public Func<PublishRequest, CancellationToken, Task<PublishResponse>>? PublishHandler { get; set; }

            public IReadOnlyList<string> Calls
            {
                get
                {
                    lock (m_lock)
                    {
                        return [.. m_calls];
                    }
                }
            }

            public int ReadCount => Volatile.Read(ref m_readCount);

            public ReadRequest? LastReadRequest { get; private set; }

            public CloseSessionRequest? LastCloseSessionRequest { get; private set; }

            public Task<TResponse> InvokeAsync<TRequest, TResponse>(
                TRequest request,
                CancellationToken ct = default)
                where TRequest : IServiceRequest, new()
                where TResponse : IServiceResponse, new()
            {
                return CompleteAsync<TRequest, TResponse>(
                    RestApiServiceRoutes.TryGetByRequestType(typeof(TRequest), out RestApiServiceRoute route)
                        ? route.OperationId
                        : typeof(TRequest).Name,
                    request,
                    ct);
            }

            public Task<ReadResponse> ReadAsync(ReadRequest request, CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_readCount);
                LastReadRequest = request;
                return CompleteAsync<ReadRequest, ReadResponse>("Read", request, ct);
            }

            public Task<WriteResponse> WriteAsync(WriteRequest request, CancellationToken ct = default)
                => CompleteAsync<WriteRequest, WriteResponse>("Write", request, ct);

            public Task<HistoryReadResponse> HistoryReadAsync(
                HistoryReadRequest request,
                CancellationToken ct = default)
                => CompleteAsync<HistoryReadRequest, HistoryReadResponse>("HistoryRead", request, ct);

            public Task<HistoryUpdateResponse> HistoryUpdateAsync(
                HistoryUpdateRequest request,
                CancellationToken ct = default)
                => CompleteAsync<HistoryUpdateRequest, HistoryUpdateResponse>("HistoryUpdate", request, ct);

            public Task<CallResponse> CallAsync(CallRequest request, CancellationToken ct = default)
                => CompleteAsync<CallRequest, CallResponse>("Call", request, ct);

            public Task<BrowseResponse> BrowseAsync(BrowseRequest request, CancellationToken ct = default)
                => CompleteAsync<BrowseRequest, BrowseResponse>("Browse", request, ct);

            public Task<BrowseNextResponse> BrowseNextAsync(BrowseNextRequest request, CancellationToken ct = default)
                => CompleteAsync<BrowseNextRequest, BrowseNextResponse>("BrowseNext", request, ct);

            public Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
                TranslateBrowsePathsToNodeIdsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<TranslateBrowsePathsToNodeIdsRequest, TranslateBrowsePathsToNodeIdsResponse>(
                    "TranslateBrowsePathsToNodeIds",
                    request,
                    ct);

            public Task<RegisterNodesResponse> RegisterNodesAsync(
                RegisterNodesRequest request,
                CancellationToken ct = default)
                => CompleteAsync<RegisterNodesRequest, RegisterNodesResponse>("RegisterNodes", request, ct);

            public Task<UnregisterNodesResponse> UnregisterNodesAsync(
                UnregisterNodesRequest request,
                CancellationToken ct = default)
                => CompleteAsync<UnregisterNodesRequest, UnregisterNodesResponse>("UnregisterNodes", request, ct);

            public Task<FindServersResponse> FindServersAsync(
                FindServersRequest request,
                CancellationToken ct = default)
                => CompleteAsync<FindServersRequest, FindServersResponse>("FindServers", request, ct);

            public Task<GetEndpointsResponse> GetEndpointsAsync(
                GetEndpointsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<GetEndpointsRequest, GetEndpointsResponse>("GetEndpoints", request, ct);

            public Task<CreateSessionResponse> CreateSessionAsync(
                CreateSessionRequest request,
                CancellationToken ct = default)
            {
                AddCall("CreateSession");
                return Task.FromResult(new CreateSessionResponse
                {
                    ResponseHeader = CreateResponseHeader(request),
                    SessionId = SessionId,
                    AuthenticationToken = AuthenticationToken,
                    RevisedSessionTimeout = RevisedSessionTimeout,
                    ServerNonce = ByteString.From([1, 2, 3, 4]),
                    ServerCertificate = default,
                    ServerEndpoints = [],
                    MaxRequestMessageSize = 0
                });
            }

            public Task<ActivateSessionResponse> ActivateSessionAsync(
                ActivateSessionRequest request,
                CancellationToken ct = default)
                => CompleteAsync<ActivateSessionRequest, ActivateSessionResponse>("ActivateSession", request, ct);

            public Task<CloseSessionResponse> CloseSessionAsync(
                CloseSessionRequest request,
                CancellationToken ct = default)
            {
                LastCloseSessionRequest = request;
                return CompleteAsync<CloseSessionRequest, CloseSessionResponse>("CloseSession", request, ct);
            }

            public Task<CancelResponse> CancelAsync(CancelRequest request, CancellationToken ct = default)
                => CompleteAsync<CancelRequest, CancelResponse>("Cancel", request, ct);

            public Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
                CreateMonitoredItemsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<CreateMonitoredItemsRequest, CreateMonitoredItemsResponse>(
                    "CreateMonitoredItems",
                    request,
                    ct);

            public Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
                ModifyMonitoredItemsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<ModifyMonitoredItemsRequest, ModifyMonitoredItemsResponse>(
                    "ModifyMonitoredItems",
                    request,
                    ct);

            public Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
                SetMonitoringModeRequest request,
                CancellationToken ct = default)
                => CompleteAsync<SetMonitoringModeRequest, SetMonitoringModeResponse>(
                    "SetMonitoringMode",
                    request,
                    ct);

            public Task<SetTriggeringResponse> SetTriggeringAsync(
                SetTriggeringRequest request,
                CancellationToken ct = default)
                => CompleteAsync<SetTriggeringRequest, SetTriggeringResponse>("SetTriggering", request, ct);

            public Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
                DeleteMonitoredItemsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<DeleteMonitoredItemsRequest, DeleteMonitoredItemsResponse>(
                    "DeleteMonitoredItems",
                    request,
                    ct);

            public Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
                CreateSubscriptionRequest request,
                CancellationToken ct = default)
                => CompleteAsync<CreateSubscriptionRequest, CreateSubscriptionResponse>(
                    "CreateSubscription",
                    request,
                    ct);

            public Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
                ModifySubscriptionRequest request,
                CancellationToken ct = default)
                => CompleteAsync<ModifySubscriptionRequest, ModifySubscriptionResponse>(
                    "ModifySubscription",
                    request,
                    ct);

            public Task<SetPublishingModeResponse> SetPublishingModeAsync(
                SetPublishingModeRequest request,
                CancellationToken ct = default)
                => CompleteAsync<SetPublishingModeRequest, SetPublishingModeResponse>(
                    "SetPublishingMode",
                    request,
                    ct);

            public Task<PublishResponse> PublishAsync(PublishRequest request, CancellationToken ct = default)
            {
                AddCall("Publish");
                return PublishHandler != null
                    ? PublishHandler(request, ct)
                    : Task.FromResult(CreatePublishResponse(1, 1, request));
            }

            public Task<RepublishResponse> RepublishAsync(RepublishRequest request, CancellationToken ct = default)
                => CompleteAsync<RepublishRequest, RepublishResponse>("Republish", request, ct);

            public Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
                TransferSubscriptionsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<TransferSubscriptionsRequest, TransferSubscriptionsResponse>(
                    "TransferSubscriptions",
                    request,
                    ct);

            public Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
                DeleteSubscriptionsRequest request,
                CancellationToken ct = default)
                => CompleteAsync<DeleteSubscriptionsRequest, DeleteSubscriptionsResponse>(
                    "DeleteSubscriptions",
                    request,
                    ct);

            public static PublishResponse CreatePublishResponse(uint subscriptionId, uint sequenceNumber)
            {
                return CreatePublishResponse(subscriptionId, sequenceNumber, new PublishRequest());
            }

            private static PublishResponse CreatePublishResponse(
                uint subscriptionId,
                uint sequenceNumber,
                PublishRequest request)
            {
                return new PublishResponse
                {
                    ResponseHeader = CreateResponseHeader(request),
                    SubscriptionId = subscriptionId,
                    AvailableSequenceNumbers = [],
                    MoreNotifications = false,
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = sequenceNumber,
                        PublishTime = DateTime.UtcNow,
                        NotificationData = []
                    },
                    Results = [],
                    DiagnosticInfos = []
                };
            }

            private Task<TResponse> CompleteAsync<TRequest, TResponse>(
                string operation,
                TRequest request,
                CancellationToken ct)
                where TRequest : IServiceRequest
                where TResponse : IServiceResponse, new()
            {
                ct.ThrowIfCancellationRequested();
                AddCall(operation);
                var response = new TResponse();
                typeof(TResponse)
                    .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                    .SetValue(response, CreateResponseHeader(request));
                return Task.FromResult(response);
            }

            private static ResponseHeader CreateResponseHeader(IServiceRequest request)
            {
                return new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = StatusCodes.Good,
                    StringTable = [],
                    AdditionalHeader = new ExtensionObject()
                };
            }

            private void AddCall(string operation)
            {
                lock (m_lock)
                {
                    m_calls.Add(operation);
                }
            }
        }
    }
}
