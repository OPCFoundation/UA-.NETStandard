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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Unit tests for <see cref="WebApiWssTransportChannel"/> — the
    /// WSS <c>opcua+openapi</c> sub-protocol channel (OPC UA Part 6
    /// §7.5.2; OPC Foundation
    /// <see href="https://profiles.opcfoundation.org/profile/2339">profile/2339</see>).
    /// Verifies wire-shape round-trip, sub-protocol negotiation
    /// (plain <c>opcua+openapi</c> and bearer variant
    /// <c>opcua+openapi+&lt;accesstoken&gt;</c>), URI normalisation,
    /// the reconnect feature flag, and lifecycle (close / dispose /
    /// reconnect) contract.
    /// </summary>
    /// <remarks>
    /// The stub server is a minimal Kestrel host listening on
    /// <c>http://127.0.0.1:0</c> with <see cref="WebSocketOptions"/>
    /// enabled — plain <c>ws://</c> sidesteps the TLS / certificate
    /// management overhead unit tests don't need. The same wire
    /// envelope (<c>{TypeId, Body}</c> standard OPC UA JSON message)
    /// is reused, so the channel exercises the same encode/decode path
    /// it does against the real <c>HttpsTransportListener.AcceptWebSocketOpenApiAsync</c>.
    /// </remarks>
    [TestFixture]
    [Category("WebApiWssTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WebApiWssTransportChannelTests
    {
        private IHost? m_host;
        private Uri m_baseUri = null!;
        private ServiceMessageContext? m_messageContext;
        private string? m_lastNegotiatedSubProtocol;
        private IServiceRequest? m_lastRequest;
        private Func<IServiceRequest, IServiceResponse>? m_responder;
        private Func<WebSocket, CancellationToken, Task>? m_socketScript;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            // Populate the encodeable factory with the standard
            // OPC UA Core types so the client-side
            // JsonDecoder.DecodeMessage<IServiceResponse> can resolve
            // ReadResponse / BrowseResponse / etc. from the wire
            // envelope's TypeId.
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();
            m_lastNegotiatedSubProtocol = null;
            m_lastRequest = null;
            m_responder = DefaultResponder;
            m_socketScript = null;

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts => opts.Listen(IPAddress.Loopback, 0))
                        .ConfigureServices(s => { });
                    webHost.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(HandleWebSocketAsync);
                    });
                });

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            IServer server = m_host.Services.GetRequiredService<IServer>();
            string baseAddress = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            m_baseUri = new Uri(baseAddress.Replace("http://", "ws://", StringComparison.Ordinal));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_host != null)
            {
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
                m_host = null;
            }
        }

        [Test]
        public async Task SendRequestAsyncRoundTripsReadRequestAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 4242 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
            Assert.That(m_lastRequest, Is.InstanceOf<ReadRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(4242u));
        }

        [Test]
        public async Task SendRequestAsyncRoundTripsBrowseRequestAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            var request = new BrowseRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 17 },
                View = new ViewDescription(),
                NodesToBrowse = new ArrayOf<BrowseDescription>()
            };

            IServiceResponse response = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<BrowseResponse>());
            Assert.That(m_lastRequest, Is.InstanceOf<BrowseRequest>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(17u));
        }

        [Test]
        public async Task OpenAsyncNegotiatesPlainSubProtocolWhenNoBearerAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            // Send any request to force the handshake to land server-side.
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };
            _ = await channel
                .SendRequestAsync(request, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(m_lastNegotiatedSubProtocol, Is.EqualTo(Profiles.OpcUaWsSubProtocolOpenApi),
                "Without a bearer token, the channel must negotiate plain " +
                "'opcua+openapi'.");
        }

        [Test]
        public async Task OpenAsyncRejectsBearerTokenOverPlainWebSocketAsync()
        {
            // The bearer-prefix sub-protocol leaks the token through every
            // TCP intermediary in the 101 handshake. The client refuses to
            // send the token over plain ws://. Over wss:// (real TLS) the
            // credential is at least hidden from network observers; that
            // path is covered by the integration tests against the
            // reference server.
            const string token = "abc.def.ghi";
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                using WebApiWssTransportChannel channel = await OpenChannelAsync(
                    new WebApiClientOptions { BearerToken = token })
                    .ConfigureAwait(false);
            })!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed),
                "Bearer-prefix sub-protocol over plain ws:// must be rejected with " +
                "BadSecurityChecksFailed.");
        }

        [Test]
        public void SupportedFeaturesIncludesReconnect()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            Assert.That(channel.SupportedFeatures, Is.EqualTo(TransportChannelFeatures.Reconnect));
        }

        [Test]
        public void UriSchemeIsOpcWssOpenApi()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            Assert.That(channel.UriScheme, Is.EqualTo(Utils.UriSchemeOpcWssOpenApi));
        }

        [Test]
        public async Task ReconnectAsyncReopensChannelForFurtherRequestsAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            await channel.ReconnectAsync(connection: null, CancellationToken.None).ConfigureAwait(false);
            IServiceResponse response = await channel.SendRequestAsync(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 42 } }).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42));
            Assert.That(response, Is.TypeOf<ReadResponse>());
        }

        [Test]
        public void SendRequestAsyncThrowsBadNotConnectedBeforeOpen()
        {
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());

            var request = new ReadRequest { RequestHeader = new RequestHeader() };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
        }

        [Test]
        public async Task DisposedChannelRejectsSendAsync()
        {
            WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);
            channel.Dispose();

            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public void OpenAsyncRejectsBadUriScheme()
        {
            // Smoke check: an unrecognised URI scheme is allowed through
            // NormalizeUrl unchanged but ClientWebSocket itself rejects
            // anything outside ws/wss. The channel surfaces the
            // underlying connect failure rather than swallowing it.
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = "http://127.0.0.1:1/",
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable()
            };
            // ClientWebSocket rejects 'http' scheme outright.
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await channel
                    .OpenAsync(new Uri("http://127.0.0.1:1/"), settings, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task CloseAsyncReleasesWebSocketAsync()
        {
            WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);

            // After Close the channel reports as not connected on next send.
            var request = new ReadRequest { RequestHeader = new RequestHeader() };
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));

            channel.Dispose();
        }

        [Test]
        public async Task ServerClosedMidRequestMapsToBadConnectionClosedAsync()
        {
            // Replace the responder with one that closes the socket
            // before sending a response.
            m_responder = req => throw new CloseConnectionSentinel();

            using WebApiWssTransportChannel channel = await OpenChannelAsync()
                .ConfigureAwait(false);

            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 99 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await channel
                    .SendRequestAsync(request, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed),
                "When the server closes the WebSocket while a request is " +
                "in flight, the channel surfaces BadConnectionClosed.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WebSocketLateReplyDoesNotDisconnectOrCompleteAnotherRequestAsync(bool reuseCallerHandle)
        {
            var oldReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var currentReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sendLate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCurrent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_socketScript = async (socket, ct) =>
            {
                IServiceRequest old = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                oldReceived.TrySetResult(old);
                IServiceRequest current = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                currentReceived.TrySetResult(current);
                await sendLate.Task.WaitAsync(ct).ConfigureAwait(false);
                await SendResponseAsync(socket, DefaultResponder(old), ct).ConfigureAwait(false);

                // The probe reply follows the late reply on the same wire. Receiving it proves
                // the client has consumed the old response while the current reply remains held.
                IServiceRequest probe = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                await SendResponseAsync(socket, new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = probe.RequestHeader.RequestHandle },
                    Results = [new DataValue(new Variant(303))]
                }, ct).ConfigureAwait(false);
                await releaseCurrent.Task.WaitAsync(ct).ConfigureAwait(false);
                await SendResponseAsync(socket, new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = current.RequestHeader.RequestHandle },
                    Results = [new DataValue(new Variant(202))]
                }, ct).ConfigureAwait(false);
                await releaseServer.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            using WebApiWssTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var originalHeader = new RequestHeader
            {
                RequestHandle = 101,
                AuditEntryId = "original-publish",
                TimeoutHint = 10000
            };
            var original = new PublishRequest
            {
                RequestHeader = originalHeader,
                SubscriptionAcknowledgements =
                [
                    new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 11 }
                ]
            };
            PublishRequest originalSnapshot = CoreUtils.Clone(original)!;
            uint currentHandle = reuseCallerHandle ? 101u : 202u;
            var currentHeader = new RequestHeader
            {
                RequestHandle = currentHandle,
                AuditEntryId = "current-read",
                TimeoutHint = 10000
            };
            var currentPayload = new ReadRequest
            {
                RequestHeader = currentHeader,
                MaxAge = 25,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead =
                [
                    new ReadValueId { NodeId = new NodeId("immutable-read", 1), AttributeId = Attributes.Value }
                ]
            };
            ReadRequest currentSnapshot = CoreUtils.Clone(currentPayload)!;
            Task<IServiceResponse> oldRequest = channel.SendRequestAsync(original, cancellation.Token).AsTask();
            Task<IServiceResponse>? currentRequest = null;
            try
            {
                IServiceRequest oldWire = await oldReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                AssertRequestUnchanged(original, originalSnapshot, originalHeader);
                cancellation.Cancel();
                Assert.CatchAsync<OperationCanceledException>(
                    async () => await oldRequest.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                currentRequest = channel.SendRequestAsync(currentPayload).AsTask();
                Task observed = await Task.WhenAny(currentReceived.Task, currentRequest)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (ReferenceEquals(observed, currentRequest))
                {
                    await currentRequest.ConfigureAwait(false);
                }
                IServiceRequest currentWire = await currentReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(currentWire.RequestHeader.RequestHandle,
                    Is.Not.EqualTo(oldWire.RequestHeader.RequestHandle));
                AssertRequestUnchanged(original, originalSnapshot, originalHeader);
                AssertRequestUnchanged(currentPayload, currentSnapshot, currentHeader);
                ServiceResultException duplicate = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await channel.SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = currentHandle } })
                        .ConfigureAwait(false))!;
                Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

                sendLate.TrySetResult(true);
                IServiceResponse probe = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 303 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(probe, Is.TypeOf<ReadResponse>());
                var probeRead = (ReadResponse)probe;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(probeRead.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(probeRead.ResponseHeader.RequestHandle, Is.EqualTo(303));
                    Assert.That(probeRead.Results, Has.Count.EqualTo(1));
                    Assert.That(probeRead.Results[0].WrappedValue, Is.EqualTo(new Variant(303)));
                    Assert.That(currentRequest.IsCompleted, Is.False);
                    Assert.That(releaseCurrent.Task.IsCompleted, Is.False);
                    Assert.That(oldRequest.IsCanceled, Is.True);
                }

                releaseCurrent.TrySetResult(true);
                IServiceResponse current = await currentRequest.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(current, Is.TypeOf<ReadResponse>());
                var currentRead = (ReadResponse)current;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(currentRead.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(currentRead.ResponseHeader.RequestHandle, Is.EqualTo(currentHandle));
                    Assert.That(currentRead.Results, Has.Count.EqualTo(1));
                    Assert.That(currentRead.Results[0].WrappedValue, Is.EqualTo(new Variant(202)));
                }
                AssertRequestUnchanged(original, originalSnapshot, originalHeader);
                AssertRequestUnchanged(currentPayload, currentSnapshot, currentHeader);
            }
            finally
            {
                cancellation.Cancel();
                sendLate.TrySetResult(true);
                releaseCurrent.TrySetResult(true);
                releaseServer.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.CloseAsync(cleanup.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in
                    new[] { oldRequest, currentRequest }.OfType<Task<IServiceResponse>>())
                {
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadConnectionClosed)
                    {
                    }
                }
            }
        }

        [Test]
        public async Task CancelRequestTranslatesOutstandingTargetWithoutMutatingCallerAsync()
        {
            var warmupReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var targetReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCancel = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseTarget = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_socketScript = async (socket, ct) =>
            {
                IServiceRequest warmup = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                warmupReceived.TrySetResult(warmup);
                await SendResponseAsync(socket, DefaultResponder(warmup), ct).ConfigureAwait(false);
                IServiceRequest target = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                targetReceived.TrySetResult(target);
                IServiceRequest cancel = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                cancelReceived.TrySetResult(cancel);
                await releaseCancel.Task.WaitAsync(ct).ConfigureAwait(false);
                await SendResponseAsync(socket, new CancelResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = cancel.RequestHeader.RequestHandle },
                    CancelCount = 1
                }, ct).ConfigureAwait(false);
                await releaseTarget.Task.WaitAsync(ct).ConfigureAwait(false);
                await SendResponseAsync(socket, new ServiceFault
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = target.RequestHeader.RequestHandle,
                        ServiceResult = StatusCodes.BadRequestCancelledByClient
                    }
                }, ct).ConfigureAwait(false);
                await releaseServer.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            using WebApiWssTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            Task<IServiceResponse>? targetRequest = null;
            Task<IServiceResponse>? cancelRequest = null;
            try
            {
                IServiceResponse warmup = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 9001 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(warmup, Is.TypeOf<ReadResponse>());
                Assert.That(warmup.ResponseHeader.RequestHandle, Is.EqualTo(9001));
                IServiceRequest warmupWire = await warmupReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                // A previously issued wire handle cannot be issued again, even when the caller
                // deliberately chooses that number. This guarantees a non-identity Cancel target.
                uint targetHandle = warmupWire.RequestHeader.RequestHandle;
                uint cancelHandle = targetHandle == 202 ? 203u : 202u;
                var targetHeader = new RequestHeader { RequestHandle = targetHandle, AuditEntryId = "cancel-target" };
                var target = new PublishRequest
                {
                    RequestHeader = targetHeader,
                    SubscriptionAcknowledgements =
                    [
                        new SubscriptionAcknowledgement { SubscriptionId = 17, SequenceNumber = 23 }
                    ]
                };
                PublishRequest targetSnapshot = CoreUtils.Clone(target)!;
                var cancelHeader = new RequestHeader { RequestHandle = cancelHandle, AuditEntryId = "cancel-service" };
                var cancel = new CancelRequest { RequestHeader = cancelHeader, RequestHandle = targetHandle };
                CancelRequest cancelSnapshot = CoreUtils.Clone(cancel)!;
                targetRequest = channel.SendRequestAsync(target).AsTask();
                IServiceRequest targetWire = await targetReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                cancelRequest = channel.SendRequestAsync(cancel).AsTask();
                IServiceRequest received = await cancelReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(received, Is.TypeOf<CancelRequest>());
                var cancelWire = (CancelRequest)received;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(targetWire.RequestHeader.RequestHandle, Is.Not.EqualTo(targetHandle));
                    Assert.That(cancelWire.RequestHandle, Is.EqualTo(targetWire.RequestHeader.RequestHandle));
                    Assert.That(cancelWire.RequestHandle, Is.Not.EqualTo(targetHandle));
                    Assert.That(cancelWire.RequestHeader.RequestHandle,
                        Is.Not.EqualTo(targetWire.RequestHeader.RequestHandle));
                    Assert.That(targetRequest.IsCompleted, Is.False);
                    AssertRequestUnchanged(target, targetSnapshot, targetHeader);
                    AssertRequestUnchanged(cancel, cancelSnapshot, cancelHeader);
                }

                releaseCancel.TrySetResult(true);
                IServiceResponse response = await cancelRequest.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<CancelResponse>());
                var cancelled = (CancelResponse)response;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(cancelled.CancelCount, Is.EqualTo(1));
                    Assert.That(cancelled.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(cancelled.ResponseHeader.RequestHandle, Is.EqualTo(cancelHandle));
                    Assert.That(targetRequest.IsCompleted, Is.False);
                }
                releaseTarget.TrySetResult(true);
                IServiceResponse targetResponse = await targetRequest.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(targetResponse, Is.TypeOf<ServiceFault>());
                    Assert.That(targetResponse.ResponseHeader.RequestHandle, Is.EqualTo(targetHandle));
                    Assert.That(targetResponse.ResponseHeader.ServiceResult,
                        Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                    AssertRequestUnchanged(target, targetSnapshot, targetHeader);
                    AssertRequestUnchanged(cancel, cancelSnapshot, cancelHeader);
                }
            }
            finally
            {
                releaseCancel.TrySetResult(true);
                releaseTarget.TrySetResult(true);
                releaseServer.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.CloseAsync(cleanup.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in
                    new[] { targetRequest, cancelRequest }.OfType<Task<IServiceResponse>>())
                {
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadConnectionClosed)
                    {
                    }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelForRetiredOrUnknownCallerDoesNotCancelActiveWireRequestAsync(bool retiredTarget)
        {
            uint retiredWireHandle = 0;
            var activeReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_socketScript = async (socket, ct) =>
            {
                IServiceRequest active = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                activeReceived.TrySetResult(active);
                if (retiredTarget)
                {
                    IServiceRequest warmup = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                    retiredWireHandle = warmup.RequestHeader.RequestHandle;
                    await SendResponseAsync(socket, DefaultResponder(warmup), ct).ConfigureAwait(false);
                }
                IServiceRequest request = await ReceiveServiceRequestAsync(socket, ct).ConfigureAwait(false);
                cancelReceived.TrySetResult(request);
                var cancel = (CancelRequest)request;
                bool cancelledActive = cancel.RequestHandle == active.RequestHeader.RequestHandle;
                if (cancelledActive)
                {
                    await SendResponseAsync(socket, new ServiceFault
                    {
                        ResponseHeader = new ResponseHeader
                        {
                            RequestHandle = active.RequestHeader.RequestHandle,
                            ServiceResult = StatusCodes.BadRequestCancelledByClient
                        }
                    }, ct).ConfigureAwait(false);
                }
                await SendResponseAsync(socket, new CancelResponse
                {
                    ResponseHeader = new ResponseHeader { RequestHandle = cancel.RequestHeader.RequestHandle },
                    CancelCount = cancelledActive ? 1u : 0u
                }, ct).ConfigureAwait(false);
                await releaseActive.Task.WaitAsync(ct).ConfigureAwait(false);
                if (!cancelledActive)
                {
                    await SendResponseAsync(socket, new PublishResponse
                    {
                        ResponseHeader = new ResponseHeader { RequestHandle = active.RequestHeader.RequestHandle },
                        SubscriptionId = 17
                    }, ct).ConfigureAwait(false);
                }
                await releaseServer.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            using WebApiWssTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            Task<IServiceResponse>? activeRequest = null;
            Task<IServiceResponse>? cancelRequest = null;
            try
            {
                var activeHeader = new RequestHeader { RequestHandle = 9001, AuditEntryId = "unrelated-active" };
                var activePayload = new PublishRequest { RequestHeader = activeHeader };
                PublishRequest activeSnapshot = CoreUtils.Clone(activePayload)!;
                activeRequest = channel.SendRequestAsync(activePayload).AsTask();
                IServiceRequest activeWire = await activeReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                uint unknownCaller = activeWire.RequestHeader.RequestHandle;
                Assert.That(unknownCaller, Is.Not.EqualTo(activeHeader.RequestHandle));
                if (retiredTarget)
                {
                    IServiceResponse warmup = await channel.SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = unknownCaller } })
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(warmup, Is.TypeOf<ReadResponse>());
                    Assert.That(warmup.ResponseHeader.RequestHandle, Is.EqualTo(unknownCaller));
                    Assert.That(activeRequest.IsCompleted, Is.False);
                }
                var cancelHeader = new RequestHeader { RequestHandle = 9002, AuditEntryId = "no-target" };
                var cancel = new CancelRequest { RequestHeader = cancelHeader, RequestHandle = unknownCaller };
                CancelRequest cancelSnapshot = CoreUtils.Clone(cancel)!;
                cancelRequest = channel.SendRequestAsync(cancel).AsTask();
                IServiceRequest received = await cancelReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                IServiceResponse response = await cancelRequest.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(received, Is.TypeOf<CancelRequest>());
                Assert.That(response, Is.TypeOf<CancelResponse>());
                var wireCancel = (CancelRequest)received;
                var cancelled = (CancelResponse)response;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(wireCancel.RequestHandle, Is.Not.Zero);
                    Assert.That(wireCancel.RequestHandle, Is.Not.EqualTo(activeWire.RequestHeader.RequestHandle));
                    Assert.That(wireCancel.RequestHandle, Is.Not.EqualTo(wireCancel.RequestHeader.RequestHandle));
                    if (retiredTarget)
                    {
                        Assert.That(retiredWireHandle, Is.Not.Zero);
                        Assert.That(wireCancel.RequestHandle, Is.Not.EqualTo(retiredWireHandle));
                    }
                    Assert.That(cancelled.CancelCount, Is.Zero);
                    Assert.That(cancelled.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(cancelled.ResponseHeader.RequestHandle, Is.EqualTo(9002));
                    Assert.That(activeRequest.IsCompleted, Is.False);
                    Assert.That(releaseActive.Task.IsCompleted, Is.False);
                    AssertRequestUnchanged(activePayload, activeSnapshot, activeHeader);
                    AssertRequestUnchanged(cancel, cancelSnapshot, cancelHeader);
                }

                releaseActive.TrySetResult(true);
                IServiceResponse activeResponse = await activeRequest.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(activeResponse, Is.TypeOf<PublishResponse>());
                var publish = (PublishResponse)activeResponse;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(publish.SubscriptionId, Is.EqualTo(17));
                    Assert.That(publish.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(publish.ResponseHeader.RequestHandle, Is.EqualTo(9001));
                }
            }
            finally
            {
                releaseActive.TrySetResult(true);
                releaseServer.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.CloseAsync(cleanup.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in
                    new[] { activeRequest, cancelRequest }.OfType<Task<IServiceResponse>>())
                {
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadConnectionClosed)
                    {
                    }
                }
            }
        }

        [TestCase("zero")]
        [TestCase("reserved")]
        [TestCase("neverIssued")]
        public async Task UnallocatedResponseHandleClosesConnectionAsync(string responseHandleKind)
        {
            uint reservedHandle = 0;
            uint cancelWireHandle = 0;
            m_responder = request =>
            {
                if (request is CancelRequest cancel)
                {
                    reservedHandle = cancel.RequestHandle;
                    cancelWireHandle = cancel.RequestHeader.RequestHandle;
                    return new CancelResponse
                    {
                        ResponseHeader = new ResponseHeader { RequestHandle = cancelWireHandle }
                    };
                }
                return new ReadResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        RequestHandle = responseHandleKind switch
                        {
                            "zero" => 0,
                            "reserved" => reservedHandle,
                            "neverIssued" => request.RequestHeader.RequestHandle + 1,
                            _ => throw new InvalidOperationException("Unknown response-handle test partition.")
                        }
                    }
                };
            };
            using WebApiWssTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            IServiceResponse probe = await channel.SendRequestAsync(new CancelRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 100 },
                RequestHandle = uint.MaxValue
            }).AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(probe, Is.TypeOf<CancelResponse>());
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reservedHandle, Is.Not.Zero);
                Assert.That(reservedHandle, Is.Not.EqualTo(cancelWireHandle));
                Assert.That(probe.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(probe.ResponseHeader.RequestHandle, Is.EqualTo(100));
                Assert.That(((CancelResponse)probe).CancelCount, Is.Zero);
            }
            ServiceResultException invalid = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 101 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadUnknownResponse));
            ServiceResultException closed = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 102 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
            Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            await channel.CloseAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SendRequestWithDefaultHeaderPreservesRequestAsync()
        {
            using WebApiWssTransportChannel channel = await OpenChannelAsync().ConfigureAwait(false);
            var request = new ReadRequest
            {
                RequestHeader = null!,
                MaxAge = 17,
                TimestampsToReturn = TimestampsToReturn.Both
            };
            RequestHeader originalHeader = request.RequestHeader;
            Assert.That(originalHeader, Is.Not.Null, "The generated request normalizes null to a default header.");
            ReadRequest snapshot = CoreUtils.Clone(request)!;

            IServiceResponse response = await channel.SendRequestAsync(request)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(response, Is.TypeOf<ReadResponse>());
            Assert.That(response.ResponseHeader.RequestHandle, Is.Zero);
            Assert.That(originalHeader.RequestHandle, Is.Zero);
            AssertRequestUnchanged(request, snapshot, originalHeader);
        }

        private static void AssertRequestUnchanged(
            IServiceRequest request,
            IServiceRequest snapshot,
            RequestHeader originalHeader)
        {
            Assert.That(request.RequestHeader, Is.SameAs(originalHeader));
            Assert.That(request.IsEqual(snapshot), Is.True,
                "Wire-handle translation must not mutate the caller request.");
        }

        private async Task<WebApiWssTransportChannel> OpenChannelAsync(
            WebApiClientOptions? options = null)
        {
            var channel = new WebApiWssTransportChannel(new TelemetryStub(), options);
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    // Non-empty server cert short-circuits
                    // HydrateEndpointFromServerAsync so the unit tests
                    // don't have to stub GetEndpoints.
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable()
            };
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);
            return channel;
        }

        private async Task HandleWebSocketAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }

            string? subProtocol = context.WebSockets.WebSocketRequestedProtocols
                .FirstOrDefault();
            if (subProtocol == null ||
                (!string.Equals(subProtocol, Profiles.OpcUaWsSubProtocolOpenApi, StringComparison.Ordinal) &&
                    !subProtocol.StartsWith(Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix, StringComparison.Ordinal)))
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                return;
            }
            m_lastNegotiatedSubProtocol = subProtocol;

            using WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(subProtocol)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;
            ServiceMessageContext messageContext = m_messageContext!;
            if (m_socketScript != null)
            {
                await m_socketScript(ws, ct).ConfigureAwait(false);
                return;
            }
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                byte[]? requestBytes;
                try
                {
                    requestBytes = await ReceiveMessageAsync(ws, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (requestBytes == null)
                {
                    return;
                }

                IServiceRequest decoded = JsonDecoder.DecodeMessage<IServiceRequest>(
                    requestBytes,
                    messageContext);
                m_lastRequest = decoded;

                IServiceResponse response;
                try
                {
                    response = m_responder!(decoded);
                }
                catch (CloseConnectionSentinel)
                {
                    // Abort the socket without an OPC UA response —
                    // exercises the BadConnectionClosed path in the
                    // client receive loop.
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "test-close",
                        ct).ConfigureAwait(false);
                    return;
                }

                await SendResponseAsync(ws, response, ct).ConfigureAwait(false);
            }
        }

        private async Task<IServiceRequest> ReceiveServiceRequestAsync(WebSocket socket, CancellationToken ct)
        {
            byte[] bytes = await ReceiveMessageAsync(socket, ct).ConfigureAwait(false) ??
                throw new InvalidOperationException("The connection closed before the scripted request.");
            return JsonDecoder.DecodeMessage<IServiceRequest>(bytes, m_messageContext!);
        }

        private async Task SendResponseAsync(WebSocket socket, IServiceResponse response, CancellationToken ct)
        {
            byte[] responseBytes;
            using (var stream = new MemoryStream())
            {
                using (var encoder = new JsonEncoder(stream, m_messageContext!, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(response, response.TypeId);
                }
                responseBytes = stream.ToArray();
            }
            await socket.SendAsync(
                new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, endOfMessage: true, ct)
                .ConfigureAwait(false);
        }

        private static async Task<byte[]?> ReceiveMessageAsync(
            WebSocket ws,
            CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            byte[] receive = new byte[8192];
            while (true)
            {
                WebSocketReceiveResult result = await ws
                    .ReceiveAsync(new ArraySegment<byte>(receive), ct)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "client-close",
                        ct).ConfigureAwait(false);
                    return null;
                }
                buffer.Write(receive, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return buffer.ToArray();
                }
            }
        }

        private static IServiceResponse DefaultResponder(IServiceRequest request)
        {
            if (!WebApiServiceRoutes.TryGetByRequestType(request.GetType(), out WebApiServiceRoute route))
            {
                throw new InvalidOperationException(
                    $"No matching route for {request.GetType().Name}.");
            }
            var response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
            var header = new ResponseHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                ServiceResult = StatusCodes.Good,
                StringTable = new ArrayOf<string>(),
                AdditionalHeader = new ExtensionObject()
            };
            route.ResponseType
                .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                .SetValue(response, header);
            return response;
        }

        private sealed class CloseConnectionSentinel : Exception
        {
            public CloseConnectionSentinel()
            {
            }

            public CloseConnectionSentinel(string message)
                : base(message)
            {
            }

            public CloseConnectionSentinel(string message, Exception innerException)
                : base(message, innerException)
            {
            }
        }

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
