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
    /// <summary>
    /// Verifies bounded HTTPS body reads, response ownership, cancellation, and transport error translation.
    /// </summary>
    [TestFixture]
    [Category("HttpsTransportChannel")]
    public sealed class HttpsResponseQuotaRegressionTests
    {
        /// <summary>
        /// Verifies that actual binary and JSON body bytes enforce the quota regardless of the declared length.
        /// </summary>
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

        /// <summary>
        /// Verifies that an unknown-length oversized body reads only the quota plus one detection byte.
        /// </summary>
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

        /// <summary>
        /// Verifies that busy HTTP responses are disposed without reading or buffering their bodies.
        /// </summary>
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

        /// <summary>
        /// Verifies that the request deadline and caller cancellation remain effective while streaming the response.
        /// </summary>
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

        /// <summary>
        /// Verifies socket failures map to connection or timeout service results while preserving diagnostics.
        /// </summary>
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

        /// <summary>
        /// Verifies an HTTP failure without a recognized transport cause reports an unknown response.
        /// </summary>
        [Test]
        public Task HttpFailureWithoutLegacyInnerExceptionUsesBadUnknownResponseAsync()
        {
            return AssertHttpFailureAsync(
                new HttpRequestException("HTTP transport failed"), StatusCodes.BadUnknownResponse);
        }

        /// <summary>
        /// Verifies legacy WebException causes retain their service-status mappings and original exception.
        /// </summary>
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

        /// <summary>
        /// Sends a request through a failing handler and checks the exact status and retained diagnostic.
        /// </summary>
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

        /// <summary>
        /// Signals body-read entry and suspends until the supplied request token is canceled.
        /// </summary>
        private static async Task<int> WaitForCancellationAsync(
            TaskCompletionSource<bool> started,
            CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Opens an HTTPS channel with the injected client, message quota, encoding, and optional clock.
        /// </summary>
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

        /// <summary>
        /// Encodes a known read response with an adjustable string payload for quota boundary tests.
        /// </summary>
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

        /// <summary>
        /// Returns the controlled response body and status without network activity.
        /// </summary>
        private sealed class SingleResponseHandler : HttpMessageHandler
        {
            /// <summary>
            /// Captures the body and HTTP status to return for the next request.
            /// </summary>
            public SingleResponseHandler(TrackingContent content, HttpStatusCode status = HttpStatusCode.OK)
            {
                m_content = content;
                m_status = status;
            }

            /// <summary>
            /// Captures the request token and returns the configured response with a retry hint.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                m_content.RequestToken = cancellationToken;
                var response = new HttpResponseMessage(m_status) { Content = m_content };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
                return Task.FromResult(response);
            }

            /// <summary>
            /// Tracks body reads and disposal for the returned response.
            /// </summary>
            private readonly TrackingContent m_content;

            /// <summary>
            /// Selects successful or busy-response handling in the channel.
            /// </summary>
            private readonly HttpStatusCode m_status;
        }

        /// <summary>
        /// Fails HTTP requests with a controlled exception for service-status translation tests.
        /// </summary>
        private sealed class FailureHandler : HttpMessageHandler
        {
            /// <summary>
            /// Captures the original diagnostic that request dispatch must preserve.
            /// </summary>
            public FailureHandler(Exception failure)
            {
                m_failure = failure;
            }

            /// <summary>
            /// Completes the HTTP request with the configured failure.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromException<HttpResponseMessage>(m_failure);
            }

            /// <summary>
            /// Supplies the exception returned by each request.
            /// </summary>
            private readonly Exception m_failure;
        }

        /// <summary>
        /// Exposes a response stream while recording serialization, bytes consumed, and disposal.
        /// </summary>
        private sealed class TrackingContent : HttpContent
        {
            /// <summary>
            /// Wraps a fixed payload with a possibly misleading declared content length.
            /// </summary>
            public TrackingContent(byte[] payload, long contentLength)
                : this(new MemoryStream(payload, writable: false), contentLength)
            {
            }

            /// <summary>
            /// Takes ownership of the controlled body stream and optional content-length header.
            /// </summary>
            public TrackingContent(Stream body, long contentLength)
            {
                m_body = body;
                if (contentLength >= 0)
                {
                    Headers.ContentLength = contentLength;
                }
            }

            /// <summary>
            /// Gets how often buffered serialization was requested instead of direct stream access.
            /// </summary>
            public int SerializeCalls { get; private set; }

            /// <summary>
            /// Gets whether response ownership has released the content.
            /// </summary>
            public bool IsDisposed { get; private set; }

            /// <summary>
            /// Gets the seekable body's consumed byte count, retained after disposal.
            /// </summary>
            public long BytesRead => IsDisposed ? m_bytesRead : m_body.CanSeek ? m_body.Position : 0;

            /// <summary>
            /// Gets or sets the token received by HTTP dispatch.
            /// </summary>
            public CancellationToken RequestToken { get; set; }

            /// <summary>
            /// Records serialization and copies the body with the captured request cancellation token.
            /// </summary>
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                SerializeCalls++;
                return m_body.CopyToAsync(stream, 81920, RequestToken);
            }

            /// <summary>
            /// Exposes the existing body stream without buffering it.
            /// </summary>
            protected override Task<Stream> CreateContentReadStreamAsync()
            {
                return Task.FromResult<Stream>(m_body);
            }

#if NET5_0_OR_GREATER
            /// <summary>
            /// Exposes the existing stream through the cancellation-aware content API without buffering.
            /// </summary>
            protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(m_body);
            }
#endif

            /// <summary>
            /// Leaves the body length unknown to force stream-based quota enforcement.
            /// </summary>
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            /// <summary>
            /// Captures the final read position and disposes the owned body exactly once.
            /// </summary>
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

            /// <summary>
            /// Supplies response bytes or a controlled pending read.
            /// </summary>
            private readonly Stream m_body;

            /// <summary>
            /// Retains the read position after the body can no longer be inspected.
            /// </summary>
            private long m_bytesRead;
        }
    }
}
