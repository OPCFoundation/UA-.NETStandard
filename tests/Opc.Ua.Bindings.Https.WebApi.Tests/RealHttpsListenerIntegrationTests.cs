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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// End-to-end REST binding tests through a real
    /// <see cref="HttpsTransportListener"/> and Kestrel TLS endpoint.
    /// </summary>
    [TestFixture]
    [Category("RealWebApiIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RealHttpsListenerIntegrationTests
    {
        private static readonly MethodInfo s_decodeBodyMethod = typeof(WebApiBodyCodec)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == nameof(WebApiBodyCodec.DecodeBody) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters() is { Length: 3 } parameters &&
                parameters[0].ParameterType == typeof(byte[]));

        private ITelemetryContext? m_telemetry;
        private InMemoryCertificateRegistry? m_certificateRegistry;
        private StubTransportListenerCallback? m_callback;
        private WebApiServer? m_restServer;
        private HttpsTransportListener? m_listener;
        private HttpClient? m_client;
        private HttpClientHandler? m_clientHandler;

        /// <summary>
        /// All REST service route and encoding combinations.
        /// </summary>
        public static IEnumerable<TestCaseData> RouteEncodingCases()
        {
            foreach (WebApiServiceRoute route in WebApiServiceRoutes.Routes)
            {
                yield return new TestCaseData(route, WebApiEncoding.Compact)
                    .SetName($"{route.OperationId}CompactOverRealHttpsListener");
                yield return new TestCaseData(route, WebApiEncoding.Verbose)
                    .SetName($"{route.OperationId}VerboseOverRealHttpsListener");
            }
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            m_telemetry = new TestTelemetryContext();
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_callback = new StubTransportListenerCallback();
            m_restServer = new WebApiServer(messageContext, "real-rest-api");

            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(new WebApiHttpsStartupContributor(m_restServer));
            m_listener = (HttpsTransportListener)factory.Create(m_telemetry);

            Certificate certificate = CreateServerCertificate();
            try
            {
                m_certificateRegistry = new InMemoryCertificateRegistry(certificate);
            }
            finally
            {
                certificate.Dispose();
            }

            int port = FindAvailableTcpPort();
            await m_listener.OpenAsync(
                new Uri($"https://localhost:{port}/"),
                CreateListenerSettings(m_certificateRegistry, port),
                m_callback).ConfigureAwait(false);
            await WaitForListenerReadyAsync(port).ConfigureAwait(false);

            m_clientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            m_client = new HttpClient(m_clientHandler)
            {
                BaseAddress = new Uri($"https://localhost:{port}/")
            };
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_client?.Dispose();
            m_client = null;
            m_clientHandler?.Dispose();
            m_clientHandler = null;
            if (m_listener != null)
            {
                await m_listener.CloseAsync().ConfigureAwait(false);
                await m_listener.DisposeAsync().ConfigureAwait(false);
                m_listener = null;
            }

            m_certificateRegistry?.Dispose();
            m_certificateRegistry = null;
        }

        [TestCaseSource(nameof(RouteEncodingCases))]
        public async Task ServiceRouteRoundTripsThroughRealHttpsListener(
            WebApiServiceRoute route,
            WebApiEncoding encoding)
        {
            IServiceRequest request = CreateRequest(route, 100);

            using HttpResponseMessage response = await PostAsync(
                route.Path,
                request,
                encoding)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            AssertContentType(response, encoding);

            byte[] payload = await response.Content.ReadAsByteArrayAsync()
                .ConfigureAwait(false);
            IServiceResponse decoded = DecodeResponse(route, payload);

            Assert.That(decoded, Is.InstanceOf(route.ResponseType));
            Assert.That(m_callback!.LastRequest, Is.InstanceOf(route.RequestType));
        }

        [TestCase("https-binary")]
        [TestCase("https-json")]
        [TestCase("wss-binary")]
        [TestCase("wss-json")]
        [TestCase("webapi-https")]
        [TestCase("webapi-wss")]
        public async Task AllBindingsForwardObservedPeerAddressAsync(string binding)
        {
            string profile = binding switch
            {
                "https-json" => Profiles.HttpsJsonTransport,
                "wss-binary" => Profiles.UaWssTransport,
                "wss-json" => Profiles.UaWssJsonTransport,
                "webapi-wss" => Profiles.WssOpenApiTransport,
                _ => Profiles.HttpsBinaryTransport
            };
            var uri = new UriBuilder(m_listener!.EndpointUrl)
            {
                Host = "127.0.0.1",
                Scheme = binding.Contains("wss", StringComparison.Ordinal) ? "wss" : "https"
            };
            using CertificateEntry entry = m_certificateRegistry!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.None)!;
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = uri.Uri.AbsoluteUri,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    TransportProfileUri = profile,
                    ServerCertificate = entry.Certificate.RawData.ToByteString()
                },
                Configuration = EndpointConfiguration.Create(),
                CertificateValidator = new AcceptAllCertificateValidator(),
                Factory = messageContext.Factory,
                NamespaceUris = messageContext.NamespaceUris
            };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            for (int connection = 0; connection < 2; connection++)
            {
                using ITransportChannel channel = binding switch
                {
                    "https-binary" or "https-json" => new HttpsTransportChannel(Utils.UriSchemeHttps, m_telemetry!),
                    "wss-binary" => new WssTransportChannel(m_telemetry!),
                    "wss-json" => new WssJsonTransportChannel(m_telemetry!),
                    "webapi-https" => new WebApiTransportChannel(m_telemetry!),
                    "webapi-wss" => new WebApiWssTransportChannel(m_telemetry!),
                    _ => throw new ArgumentOutOfRangeException(nameof(binding))
                };
                await ((ISecureChannel)channel).OpenAsync(uri.Uri, settings, deadline.Token).ConfigureAwait(false);
                IServiceResponse response = await channel.SendRequestAsync(
                    new ReadRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = (uint)(connection + 1) }
                    }, deadline.Token).ConfigureAwait(false);

                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                Assert.That(m_callback!.LastChannelContext, Is.Not.Null);
                Assert.That(m_callback.LastChannelContext!.PeerAddress, Is.Not.Null);
                Assert.That(m_callback.LastChannelContext.PeerAddress!.MapToIPv4(), Is.EqualTo(IPAddress.Loopback));
                await channel.CloseAsync(deadline.Token).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishLongPollAwaitsServerResponse()
        {
            m_callback!.Delay = TimeSpan.FromMilliseconds(500);
            var request = new PublishRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 200,
                    TimeoutHint = 10_000
                },
                SubscriptionAcknowledgements = new ArrayOf<SubscriptionAcknowledgement>()
            };

            var stopwatch = Stopwatch.StartNew();
            using HttpResponseMessage response = await PostAsync(
                "/publish",
                request,
                WebApiEncoding.Compact)
                .ConfigureAwait(false);
            stopwatch.Stop();
            TestContext.Out.WriteLine($"Publish long poll elapsed: {stopwatch.ElapsedMilliseconds} ms");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            byte[] payload = await response.Content.ReadAsByteArrayAsync()
                .ConfigureAwait(false);
            IServiceResponse decoded = DecodeResponse(
                WebApiServiceRoutes.Routes.Single(r => r.RequestType == typeof(PublishRequest)),
                payload);

            Assert.That(decoded, Is.InstanceOf<PublishResponse>());
            Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500)));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WebSocketReadOvertakesPendingPublishAndSurvivesCancellationAsync(bool cancelPublish)
        {
            m_callback!.HoldPublish = true;
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<IServiceResponse> publish = channel.SendRequestAsync(
                new PublishRequest { RequestHeader = new RequestHeader { RequestHandle = 101 } },
                cancellation.Token).AsTask();
            try
            {
                await m_callback.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (cancelPublish)
                {
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(() => publish);
                    IServiceResponse reused = await channel.SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 101 } })
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(reused, Is.TypeOf<ReadResponse>());
                    Assert.That(reused.ResponseHeader.RequestHandle, Is.EqualTo(101));
                }
                else
                {
                    ServiceResultException duplicate = Assert.ThrowsAsync<ServiceResultException>(
                        async () => await channel.SendRequestAsync(
                            new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 101 } })
                            .ConfigureAwait(false))!;
                    Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                }

                IServiceResponse read = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 202 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Assert.That(read, Is.TypeOf<ReadResponse>());
                Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(202));
                Assert.That(m_callback.PublishRelease.Task.IsCompleted, Is.False);
                m_callback.PublishRelease.TrySetResult(true);
                if (!cancelPublish)
                {
                    IServiceResponse response = await publish.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(response, Is.TypeOf<PublishResponse>());
                    Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(101));
                }
                IServiceResponse next = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 303 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(next.ResponseHeader.RequestHandle, Is.EqualTo(303));
            }
            finally
            {
                m_callback.PublishRelease.TrySetResult(true);
                cancellation.Cancel();
                try
                {
                    await publish.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task WebSocketRequestDeadlineDoesNotAbortOtherRequestsAsync()
        {
            var clock = new FakeTimeProvider();
            m_callback!.HoldPublish = true;
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync(clock).ConfigureAwait(false);
            channel.OperationTimeout = 1000;
            Task<IServiceResponse> publish = channel.SendRequestAsync(
                new PublishRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } }).AsTask();
            try
            {
                await m_callback.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                ServiceResultException timeout = Assert.ThrowsAsync<ServiceResultException>(() => publish)!;
                Assert.That(timeout.StatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
                IServiceResponse response = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 2 } })
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<ReadResponse>());
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(2));
            }
            finally
            {
                m_callback.PublishRelease.TrySetResult(true);
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task WebSocketTerminalRequestsDoNotRetainPendingStateAsync(bool expireDeadline)
        {
            var clock = new FakeTimeProvider();
            var held = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
            var releases = new List<TaskCompletionSource<bool>>();
            var requests = new List<Task<IServiceResponse>>();
            m_callback!.BeforeResponseAsync = async (request, ct) =>
            {
                if (request is PublishRequest)
                {
                    var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    await held.Writer.WriteAsync(release, ct).ConfigureAwait(false);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
            };
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync(clock).ConfigureAwait(false);
            channel.OperationTimeout = 1000;
            var retainedCounts = new List<int>();
            try
            {
                for (uint handle = 1; handle <= 24; handle++)
                {
                    using var cancellation = new CancellationTokenSource();
                    Task<IServiceResponse> publish = channel.SendRequestAsync(
                        new PublishRequest { RequestHeader = new RequestHeader { RequestHandle = handle } },
                        cancellation.Token).AsTask();
                    requests.Add(publish);
                    try
                    {
                        TaskCompletionSource<bool> release = await held.Reader.ReadAsync().AsTask()
                            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        releases.Add(release);
                        if (expireDeadline)
                        {
                            clock.Advance(TimeSpan.FromSeconds(1));
                            ServiceResultException timeout = Assert.ThrowsAsync<ServiceResultException>(
                                async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                            Assert.That(timeout.StatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
                        }
                        else
                        {
                            cancellation.Cancel();
                            Assert.CatchAsync<OperationCanceledException>(
                                async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                        }
                        IServiceResponse read = await channel.SendRequestAsync(
                            new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1000 + handle } })
                            .AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(read, Is.TypeOf<ReadResponse>());
                            Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                            Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(1000 + handle));
                            Assert.That(release.Task.IsCompleted, Is.False);
                        }
                        retainedCounts.Add(GetPendingWebSocketRequestCount(channel));
                    }
                    finally
                    {
                        cancellation.Cancel();
                    }
                }
                Assert.That(retainedCounts, Is.All.EqualTo(0),
                    "Completed cancellations and deadlines must not retain per-request state on a healthy socket.");
                Assert.That(releases, Has.Count.EqualTo(24));
                Assert.That(releases.All(release => !release.Task.IsCompleted), Is.True);
            }
            finally
            {
                foreach (TaskCompletionSource<bool> release in releases)
                {
                    release.TrySetResult(true);
                }
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.CloseAsync(cleanup.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in requests)
                {
                    try
                    {
                        await request.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadRequestTimeout ||
                        exception.StatusCode == StatusCodes.BadConnectionClosed)
                    {
                    }
                }
            }
        }

        [Test]
        public async Task ConcurrentReceiverShutdownDoesNotOrphanUncancellableCloseAsync()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requestsEntered = 0;
            m_callback!.BeforeResponseAsync = async (_, ct) =>
            {
                if (Interlocked.Increment(ref requestsEntered) == 2)
                {
                    entered.TrySetResult(true);
                }
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync().ConfigureAwait(false);
            object connection = GetWebSocketConnection(channel);
            var socket = (ClientWebSocket)connection.GetType().GetProperty("Socket")!.GetValue(connection)!;
            var receiver = (Task)connection.GetType().GetProperty("Receiver")!.GetValue(connection)!;
            var closeStarted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CertificateEntry entry = m_certificateRegistry!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.None)!;
            using X509Certificate2 certificate = entry.Certificate.AsX509Certificate2();
            using var disposal = new DisposeNotificationCertificate(
                certificate, () => closeStarted.TrySetResult(channel.CloseAsync(CancellationToken.None).AsTask()));
            socket.Options.ClientCertificates.Add(disposal);

            Task<IServiceResponse> publish = channel.SendRequestAsync(
                new PublishRequest { RequestHeader = new RequestHeader { RequestHandle = 81 } }).AsTask();
            Task<IServiceResponse> read = channel.SendRequestAsync(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 82 } }).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(read.IsCompleted, Is.False);
                Assert.That(closeStarted.Task.IsCompleted, Is.False);

                connection.GetType().GetMethod("Stop")!.Invoke(
                    connection, [new ServiceResultException(StatusCodes.BadConnectionClosed)]);
                ServiceResultException publishFailure = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await publish.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                ServiceResultException readFailure = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await read.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(publishFailure.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                    Assert.That(readFailure.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                }

                // Certificate disposal runs inside the receiver's resource cleanup. Its callback
                // starts the real uncancellable close before that cleanup has returned.
                Task close = await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await receiver.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await close.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(close.IsCompletedSuccessfully, Is.True);
                ServiceResultException disconnected = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await channel.SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 83 } })
                        .ConfigureAwait(false))!;
                Assert.That(disconnected.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            }
            finally
            {
                release.TrySetResult(true);
                channel.Dispose();
                await receiver.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in new[] { publish, read })
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

        [Test]
        public async Task WebSocketRequestHandleExhaustionClosesWithoutWrappingAndAllowsReopenAsync()
        {
            var lastReceived = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var unexpected = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var reopened = new TaskCompletionSource<IServiceRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int requestsReceived = 0;
            int reopening = 0;
            m_callback!.BeforeResponseAsync = async (request, ct) =>
            {
                if (Volatile.Read(ref reopening) != 0)
                {
                    reopened.TrySetResult(request);
                    return;
                }
                if (Interlocked.Increment(ref requestsReceived) == 1)
                {
                    lastReceived.TrySetResult(request);
                }
                else
                {
                    unexpected.TrySetResult(request);
                }
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync().ConfigureAwait(false);
            object connection = GetWebSocketConnection(channel);
            FieldInfo counter = connection.GetType()
                .GetField("m_lastRequestHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? initialCounter = counter.GetValue(connection);
            var pending = (IDictionary)connection.GetType()
                .GetField("m_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
            var callers = (IDictionary)connection.GetType()
                .GetField("m_callers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
            var receiver = (Task)connection.GetType().GetProperty("Receiver")!.GetValue(connection)!;
            counter.SetValue(connection, uint.MaxValue - 1);
            var lastHeader = new RequestHeader { RequestHandle = 41 };
            var exhaustedHeader = new RequestHeader { RequestHandle = 42 };
            Task<IServiceResponse>? last = null;
            Task<IServiceResponse>? exhausted = null;
            try
            {
                last = channel.SendRequestAsync(new PublishRequest { RequestHeader = lastHeader }).AsTask();
                IServiceRequest wire = await lastReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(wire.RequestHeader.RequestHandle, Is.EqualTo(uint.MaxValue));
                    Assert.That(lastHeader.RequestHandle, Is.EqualTo(41));
                    Assert.That(last.IsCompleted, Is.False);
                    Assert.That(pending, Has.Count.EqualTo(1));
                    Assert.That(callers, Has.Count.EqualTo(1));
                }

                exhausted = channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = exhaustedHeader }).AsTask();
                Task observed = await Task.WhenAny(exhausted, unexpected.Task).WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(observed, Is.SameAs(exhausted),
                    "Exhaustion must reject without emitting a wrapped request.");
                ServiceResultException rejected = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await exhausted.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                ServiceResultException stopped = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await last.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;
                await receiver.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                    Assert.That(stopped.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                    Assert.That(counter.GetValue(connection), Is.EqualTo(uint.MaxValue));
                    Assert.That(pending, Is.Empty);
                    Assert.That(callers, Is.Empty);
                    Assert.That(Volatile.Read(ref requestsReceived), Is.EqualTo(1));
                    Assert.That(unexpected.Task.IsCompleted, Is.False);
                    Assert.That(exhaustedHeader.RequestHandle, Is.EqualTo(42));
                }

                Volatile.Write(ref reopening, 1);
                using var reconnect = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.ReconnectAsync(ct: reconnect.Token).ConfigureAwait(false);
                object newConnection = GetWebSocketConnection(channel);
                Assert.That(newConnection, Is.Not.SameAs(connection));
                Assert.That(counter.GetValue(newConnection), Is.EqualTo(initialCounter));
                IServiceResponse response = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 41 } }, reconnect.Token)
                    .ConfigureAwait(false);
                IServiceRequest newWire = await reopened.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(newWire.RequestHeader.RequestHandle, Is.GreaterThan(0u).And.LessThan(uint.MaxValue));
                    Assert.That(counter.GetValue(newConnection), Is.EqualTo(newWire.RequestHeader.RequestHandle));
                    Assert.That(response, Is.TypeOf<ReadResponse>());
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(41));
                    Assert.That(GetPendingWebSocketRequestCount(channel), Is.Zero);
                }
            }
            finally
            {
                release.TrySetResult(true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.CloseAsync(cleanup.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                foreach (Task<IServiceResponse> request in new[] { last, exhausted }.OfType<Task<IServiceResponse>>())
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

        [Test]
        public async Task WebSocketReconnectFencesOutstandingResponsesAsync()
        {
            m_callback!.HoldPublish = true;
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync().ConfigureAwait(false);
            Task<IServiceResponse> publish = channel.SendRequestAsync(
                new PublishRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } }).AsTask();
            try
            {
                await m_callback.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await channel.ReconnectAsync(ct: deadline.Token).ConfigureAwait(false);
                ServiceResultException closed = Assert.ThrowsAsync<ServiceResultException>(() => publish)!;
                Assert.That(closed.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
                m_callback.PublishRelease.TrySetResult(true);
                IServiceResponse response = await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } }, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(response, Is.TypeOf<ReadResponse>());
                Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(1));
            }
            finally
            {
                m_callback.PublishRelease.TrySetResult(true);
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RejectedSecondWebSocketOpenPreservesActiveSettingsAsync()
        {
            using WebApiWssTransportChannel channel = await OpenWebSocketChannelAsync().ConfigureAwait(false);
            EndpointDescription original = channel.EndpointDescription;
            var context = ServiceMessageContext.Create(m_telemetry);
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription { EndpointUrl = "ws://unused.invalid" },
                Configuration = EndpointConfiguration.Create(),
                NamespaceUris = context.NamespaceUris,
                Factory = context.Factory
            };
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await channel.OpenAsync(
                    new Uri("ws://unused.invalid"), settings, CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(ReferenceEquals(channel.EndpointDescription, original), Is.True);
        }

        private static int GetPendingWebSocketRequestCount(WebApiWssTransportChannel channel)
        {
            object connection = GetWebSocketConnection(channel);
            var pending = (IDictionary)connection.GetType()
                .GetField("m_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
            return pending.Count;
        }

        private static object GetWebSocketConnection(WebApiWssTransportChannel channel)
        {
            return typeof(WebApiWssTransportChannel)
                .GetField("m_connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(channel)!;
        }

        private async Task<WebApiWssTransportChannel> OpenWebSocketChannelAsync(TimeProvider? timeProvider = null)
        {
            var uri = new UriBuilder(m_listener!.EndpointUrl) { Host = "127.0.0.1", Scheme = "wss" };
            using CertificateEntry entry = m_certificateRegistry!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.None)!;
            var context = ServiceMessageContext.Create(m_telemetry);
            var settings = new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = uri.Uri.AbsoluteUri,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    TransportProfileUri = Profiles.WssOpenApiTransport,
                    ServerCertificate = entry.Certificate.RawData.ToByteString()
                },
                Configuration = EndpointConfiguration.Create(),
                CertificateValidator = new AcceptAllCertificateValidator(),
                Factory = context.Factory,
                NamespaceUris = context.NamespaceUris
            };
            var channel = new WebApiWssTransportChannel(m_telemetry!, timeProvider: timeProvider);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await channel.OpenAsync(uri.Uri, settings, timeout.Token).ConfigureAwait(false);
                return channel;
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }

        private static Certificate CreateServerCertificate()
        {
            return DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:Opc.Ua.Bindings.WebApi.Tests",
                    "Opc.Ua.Bindings.WebApi.Tests",
                    "CN=localhost",
                    ["localhost", "127.0.0.1"])
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
        }

        private static TransportListenerSettings CreateListenerSettings(
            ICertificateRegistry certificateRegistry,
            int port)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = $"https://localhost:{port}/",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.HttpsBinaryTransport,
                Server = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Opc.Ua.Bindings.WebApi.Tests"),
                    ApplicationType = ApplicationType.Server,
                    ApplicationUri = "urn:localhost:Opc.Ua.Bindings.WebApi.Tests",
                    ProductUri = "urn:opcfoundation.org:Opc.Ua.Bindings.WebApi.Tests"
                },
                UserIdentityTokens = new ArrayOf<UserTokenPolicy>()
            };

            return new TransportListenerSettings
            {
                Descriptions = [.. s_peerProfiles.Select(profile =>
                {
                    var description = (EndpointDescription)endpoint.Clone();
                    description.TransportProfileUri = profile;
                    if (profile is Profiles.UaWssTransport or Profiles.UaWssJsonTransport or Profiles.WssOpenApiTransport)
                    {
                        description.EndpointUrl = $"wss://127.0.0.1:{port}/";
                    }
                    return description;
                })],
                Configuration = EndpointConfiguration.Create(),
                ServerCertificates = certificateRegistry,
                CertificateValidator = new AcceptAllCertificateValidator(),
                NamespaceUris = new NamespaceTable(),
                Factory = ServiceMessageContext.Create(new TestTelemetryContext()).Factory,
                HttpsMutualTls = false
            };
        }

        private static int FindAvailableTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task WaitForListenerReadyAsync(int port)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (true)
            {
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                    return;
                }
                catch (SocketException) when (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    throw new TimeoutException(
                        $"HTTPS listener did not accept TCP connections on loopback port {port} within 10 seconds.",
                        ex);
                }
            }
        }

        private static IServiceRequest CreateRequest(WebApiServiceRoute route, uint requestHandle)
        {
            var request = (IServiceRequest)Activator.CreateInstance(route.RequestType)!;
            route.RequestType
                .GetProperty(nameof(IServiceRequest.RequestHeader))!
                .SetValue(
                    request,
                    new RequestHeader
                    {
                        Timestamp = DateTime.UtcNow,
                        RequestHandle = requestHandle,
                        TimeoutHint = 10_000
                    });
            return request;
        }

        private async Task<HttpResponseMessage> PostAsync(
            string path,
            IServiceRequest request,
            WebApiEncoding encoding)
        {
            byte[] body = WebApiBodyCodec.EncodeBody(
                (IEncodeable)request,
                m_restServer!.MessageContext,
                WebApiMediaType.ToEncoderOptions(encoding));
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding));

            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = content
            };
            message.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(
                WebApiMediaType.FormatContentType(encoding)));

            return await m_client!.SendAsync(message, HttpCompletionOption.ResponseContentRead)
                .ConfigureAwait(false);
        }

        private IServiceResponse DecodeResponse(WebApiServiceRoute route, byte[] payload)
        {
            return (IServiceResponse)s_decodeBodyMethod
                .MakeGenericMethod(route.ResponseType)
                .Invoke(null, [payload, m_restServer!.MessageContext, null])!;
        }

        private static void AssertContentType(HttpResponseMessage response, WebApiEncoding encoding)
        {
            MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
            Assert.That(contentType?.MediaType, Is.EqualTo(WebApiMediaType.ContentType));

            string expectedEncoding = encoding == WebApiEncoding.Verbose
                ? WebApiMediaType.EncodingVerbose
                : WebApiMediaType.EncodingCompact;
            Assert.That(
                contentType?.Parameters,
                Has.Some.Matches<NameValueHeaderValue>(p =>
                    p.Name == WebApiMediaType.EncodingParameter &&
                    p.Value == expectedEncoding));
        }

        private sealed class DisposeNotificationCertificate : X509Certificate
        {
            public DisposeNotificationCertificate(X509Certificate certificate, Action onDispose)
                : base(certificate)
            {
                m_onDispose = onDispose;
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing && Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        m_onDispose();
                    }
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }

            private readonly Action m_onDispose;
            private int m_disposed;
        }

        private sealed class StubTransportListenerCallback : ITransportListenerCallback
        {
            public IServiceRequest? LastRequest { get; private set; }
            public SecureChannelContext? LastChannelContext { get; private set; }
            public uint NextFault { get; set; }
            public TimeSpan Delay { get; set; }
            public bool HoldPublish { get; set; }
            public Func<IServiceRequest, CancellationToken, ValueTask>? BeforeResponseAsync { get; set; }

            public TaskCompletionSource<bool> PublishEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> PublishRelease { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                LastRequest = request;
                LastChannelContext = secureChannelContext;

                if (BeforeResponseAsync != null)
                {
                    await BeforeResponseAsync(request, cancellationToken).ConfigureAwait(false);
                }

                if (HoldPublish && request is PublishRequest)
                {
                    PublishEntered.TrySetResult(true);
                    await PublishRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
                }

                if (!WebApiServiceRoutes.TryGetByRequestType(
                    request.GetType(),
                    out WebApiServiceRoute route))
                {
                    throw new InvalidOperationException(
                        $"No matching route for {request.GetType().Name}.");
                }

                StatusCode serviceResult = NextFault != 0
                    ? new StatusCode(NextFault)
                    : StatusCodes.Good;

                var response = (IServiceResponse)Activator.CreateInstance(route.ResponseType)!;
                var responseHeader = new ResponseHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                    ServiceResult = serviceResult,
                    StringTable = new ArrayOf<string>(),
                    AdditionalHeader = new ExtensionObject()
                };
                route.ResponseType
                    .GetProperty(nameof(IServiceResponse.ResponseHeader))!
                    .SetValue(response, responseHeader);
                return response;
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(
                NodeId authenticationToken,
                out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }
        }

        private sealed class InMemoryCertificateRegistry : ICertificateRegistry, IDisposable
        {
            private readonly CertificateEntry m_entry;
            private readonly CertificateEntry[] m_entries;

            public InMemoryCertificateRegistry(Certificate certificate)
            {
                using var issuerChain = new CertificateCollection();
                m_entry = new CertificateEntry(
                    certificate,
                    issuerChain,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType);
                m_entries = [m_entry];
            }

            public bool SendCertificateChain => false;

            public CertificateEntryCollection SnapshotApplicationCertificates()
            {
                return new CertificateEntryCollection(m_entries);
            }

            public CertificateEntry? AcquireApplicationCertificateByType(NodeId certificateType)
            {
                return m_entry.AddRef();
            }

            public CertificateEntry? AcquireApplicationCertificateBySecurityPolicy(string securityPolicyUri)
            {
                return m_entry.AddRef();
            }

            public Task<bool> GetIssuersAsync(
                Certificate certificate,
                IList<CertificateIssuerReference> issuers,
                CancellationToken ct = default)
            {
                return Task.FromResult(false);
            }

            public void Dispose()
            {
                m_entry.Dispose();
            }
        }

        private sealed class AcceptAllCertificateValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(CertificateValidationResult.Success);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(CertificateValidationResult.Success);
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }

        private static readonly string[] s_peerProfiles =
        [
            Profiles.HttpsBinaryTransport,
            Profiles.HttpsJsonTransport,
            Profiles.UaWssTransport,
            Profiles.UaWssJsonTransport,
            Profiles.WssOpenApiTransport
        ];
    }
}
