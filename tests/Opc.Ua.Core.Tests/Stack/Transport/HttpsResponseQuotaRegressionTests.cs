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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [Category("HttpsTransportChannel")]
    public sealed class HttpsResponseQuotaRegressionTests
    {
        [TestCase(false, -1)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(true, -1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        public async Task ActualBodySizeControlsQuotaRegardlessOfContentLengthAsync(bool json, int excess)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            byte[] payload = EncodeResponse(context, json, 1024);
            int limit = payload.Length - excess;
            foreach (long declaredLength in new long[] { -1, 0, 1, payload.Length })
            {
                using var content = new TrackingContent(payload, declaredLength);
                using var handler = new SingleResponseHandler(content);
                using var http = new HttpClient(handler);
                using HttpsTransportChannel channel = await CreateChannelAsync(
                    telemetry, context, http, json, limit).ConfigureAwait(false);
                if (excess > 0)
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    {
                        await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None).ConfigureAwait(false);
                    });
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                }
                else
                {
                    IServiceResponse response = await channel.SendRequestAsync(
                        new ReadRequest(), CancellationToken.None).ConfigureAwait(false);
                    Assert.That(response, Is.TypeOf<ReadResponse>());
                    var read = (ReadResponse)response;
                    Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(731));
                    Assert.That(read.Results, Has.Count.EqualTo(1));
                    Assert.That(read.Results[0].WrappedValue.TryGetValue(out string text), Is.True);
                    Assert.That(text, Is.EqualTo(new string('x', 1024)));
                }
                Assert.That(content.SerializeCalls, Is.Zero, $"Content-Length={declaredLength}");
                Assert.That(content.IsDisposed, Is.True);
                Assert.That(content.BytesRead, Is.LessThanOrEqualTo((long)limit + 1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OversizedUnknownLengthBodyStopsAtLimitPlusOneAsync(bool json)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            byte[] payload = EncodeResponse(context, json, 32 * 1024);
            using var content = new TrackingContent(payload, -1);
            using var handler = new SingleResponseHandler(content);
            using var http = new HttpClient(handler);
            using HttpsTransportChannel channel = await CreateChannelAsync(
                telemetry, context, http, json, 2048).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None).ConfigureAwait(false);
            });
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
            Assert.That(content.BytesRead, Is.EqualTo(2049));
            Assert.That(content.SerializeCalls, Is.Zero);
            Assert.That(content.IsDisposed, Is.True);
        }

        [TestCase(429)]
        [TestCase(503)]
        public async Task BusyResponseDoesNotBufferItsBodyAndIsDisposedAsync(int status)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            using var content = new TrackingContent(new byte[4096], 4096);
            using var handler = new SingleResponseHandler(content, (HttpStatusCode)status);
            using var http = new HttpClient(handler);
            using HttpsTransportChannel channel = await CreateChannelAsync(
                telemetry, context, http, false, 1024).ConfigureAwait(false);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None).ConfigureAwait(false);
            });
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(content.BytesRead, Is.Zero);
            Assert.That(content.SerializeCalls, Is.Zero);
            Assert.That(content.IsDisposed, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResponseBodyKeepsTheRequestDeadlineAndCallerCancellationAsync(bool callerCancellation)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var time = new FakeTimeProvider();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var body = new Mock<Stream> { CallBase = true };
            body.SetupGet(value => value.CanRead).Returns(true);
            body.Setup(value => value.ReadAsync(
                    It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((byte[] _, int _, int _, CancellationToken token) => WaitForCancellationAsync(started, token));
#if NET5_0_OR_GREATER
            body.Setup(value => value.ReadAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((Memory<byte> _, CancellationToken token) =>
                    new ValueTask<int>(WaitForCancellationAsync(started, token)));
#endif
            using var content = new TrackingContent(body.Object, -1);
            using var handler = new SingleResponseHandler(content);
            using var http = new HttpClient(handler);
            using var cancellation = new CancellationTokenSource();
            using HttpsTransportChannel channel = await CreateChannelAsync(
                telemetry, context, http, false, 1024, time).ConfigureAwait(false);
            channel.OperationTimeout = 1000;
            Task<IServiceResponse> pending = channel.SendRequestAsync(new ReadRequest(), cancellation.Token).AsTask();
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (callerCancellation)
                {
                    cancellation.Cancel();
                    Task completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)))
                        .ConfigureAwait(false);
                    Assert.That(completed, Is.SameAs(pending));
                    Assert.CatchAsync<OperationCanceledException>(() => pending);
                }
                else
                {
                    time.Advance(TimeSpan.FromSeconds(1));
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    {
                        await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    });
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
                }
                Assert.That(content.IsDisposed, Is.True);
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadRequestTimeout)
                {
                }
            }
        }

        [TestCase(SocketError.HostNotFound)]
        [TestCase(SocketError.ConnectionRefused)]
        [TestCase(SocketError.ConnectionReset)]
        [TestCase(SocketError.NetworkUnreachable)]
        [TestCase(SocketError.TimedOut)]
        public Task SocketFailureUsesTheTransportServiceResultAsync(SocketError socketError)
        {
            StatusCode expected = socketError == SocketError.TimedOut
                ? StatusCodes.BadRequestTimeout
                : StatusCodes.BadNotConnected;
            return AssertHttpFailureAsync(
                new HttpRequestException("HTTP transport failed", new SocketException((int)socketError)), expected);
        }

        [Test]
        public Task HttpFailureWithoutLegacyInnerExceptionUsesBadUnknownResponseAsync()
        {
            return AssertHttpFailureAsync(
                new HttpRequestException("HTTP transport failed"), StatusCodes.BadUnknownResponse);
        }

        [TestCase(WebExceptionStatus.Timeout)]
        [TestCase(WebExceptionStatus.ConnectFailure)]
        [TestCase(WebExceptionStatus.ConnectionClosed)]
        [TestCase(WebExceptionStatus.ReceiveFailure)]
        public Task LegacyHttpFailureKeepsItsStatusAndOriginalDiagnosticAsync(WebExceptionStatus status)
        {
            StatusCode expected = status switch
            {
                WebExceptionStatus.Timeout => StatusCodes.BadRequestTimeout,
                WebExceptionStatus.ConnectFailure or WebExceptionStatus.ConnectionClosed => StatusCodes.BadNotConnected,
                _ => StatusCodes.BadUnknownResponse
            };
            return AssertHttpFailureAsync(
                new HttpRequestException("HTTP transport failed", new WebException("legacy failure", null, status, null)),
                expected);
        }

        private static async Task AssertHttpFailureAsync(HttpRequestException failure, StatusCode expected)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            using var handler = new FailureHandler(failure);
            using var http = new HttpClient(handler);
            using HttpsTransportChannel channel = await CreateChannelAsync(
                telemetry, context, http, false, 1024).ConfigureAwait(false);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await channel.SendRequestAsync(new ReadRequest(), CancellationToken.None).ConfigureAwait(false);
            });
            Assert.That(error.StatusCode, Is.EqualTo(expected));
            Assert.That(error.InnerException, Is.SameAs(failure));
        }

        private static async Task<int> WaitForCancellationAsync(
            TaskCompletionSource<bool> started,
            CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private static async ValueTask<HttpsTransportChannel> CreateChannelAsync(
            ITelemetryContext telemetry,
            IServiceMessageContext context,
            HttpClient http,
            bool json,
            int limit,
            TimeProvider timeProvider = null)
        {
            var factory = new Mock<IOpcUaHttpClientFactory>();
            factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(http);
            var channel = new HttpsTransportChannel(
                Utils.UriSchemeHttps, telemetry, timeProvider, httpClientFactory: factory.Object);
            try
            {
                var configuration = EndpointConfiguration.Create();
                configuration.MaxMessageSize = limit;
                var url = new Uri("https://localhost/ua");
                await channel.OpenAsync(url, new TransportChannelSettings
                {
                    Description = new EndpointDescription
                    {
                        EndpointUrl = url.AbsoluteUri,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = json ? Profiles.HttpsJsonTransport : Profiles.HttpsBinaryTransport
                    },
                    Configuration = configuration,
                    Factory = context.Factory,
                    NamespaceUris = context.NamespaceUris
                }, CancellationToken.None).ConfigureAwait(false);
                return channel;
            }
            catch
            {
                channel.Dispose();
                throw;
            }
        }

        private static byte[] EncodeResponse(IServiceMessageContext context, bool json, int textLength)
        {
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 731, ServiceResult = StatusCodes.Good },
                Results = [new DataValue(new Variant(new string('x', textLength)))]
            };
            if (json)
            {
                using var memory = new MemoryStream();
                using (var encoder = new JsonEncoder(memory, context, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(response, response.TypeId);
                }
                return memory.ToArray();
            }
            return BinaryEncoder.EncodeMessage(response, context);
        }

        private sealed class SingleResponseHandler : HttpMessageHandler
        {
            public SingleResponseHandler(TrackingContent content, HttpStatusCode status = HttpStatusCode.OK)
            {
                m_content = content;
                m_status = status;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                m_content.RequestToken = cancellationToken;
                var response = new HttpResponseMessage(m_status) { Content = m_content };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
                return Task.FromResult(response);
            }

            private readonly TrackingContent m_content;
            private readonly HttpStatusCode m_status;
        }

        private sealed class FailureHandler : HttpMessageHandler
        {
            public FailureHandler(Exception failure)
            {
                m_failure = failure;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromException<HttpResponseMessage>(m_failure);
            }

            private readonly Exception m_failure;
        }

        private sealed class TrackingContent : HttpContent
        {
            public TrackingContent(byte[] payload, long contentLength)
                : this(new MemoryStream(payload, writable: false), contentLength)
            {
            }

            public TrackingContent(Stream body, long contentLength)
            {
                m_body = body;
                if (contentLength >= 0)
                {
                    Headers.ContentLength = contentLength;
                }
            }

            public int SerializeCalls { get; private set; }
            public bool IsDisposed { get; private set; }
            public long BytesRead => IsDisposed ? m_bytesRead : m_body.CanSeek ? m_body.Position : 0;
            public CancellationToken RequestToken { get; set; }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                SerializeCalls++;
                return m_body.CopyToAsync(stream, 81920, RequestToken);
            }

            protected override Task<Stream> CreateContentReadStreamAsync()
            {
                return Task.FromResult<Stream>(m_body);
            }

#if NET5_0_OR_GREATER
            protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(m_body);
            }
#endif

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !IsDisposed)
                {
                    m_bytesRead = m_body.CanSeek ? m_body.Position : 0;
                    IsDisposed = true;
                    m_body.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly Stream m_body;
            private long m_bytesRead;
        }
    }
}
