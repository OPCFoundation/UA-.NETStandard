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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.WebApi
{
    /// <summary>
    /// Covers bounded OpenAPI response streaming and timeouts that remain effective while reading response bodies.
    /// </summary>
    [TestFixture]
    [Category("WebApi")]
    public sealed class WebApiResponseQuotaRegressionTests
    {
        /// <summary>
        /// Verifies actual response length enforces the quota without buffering through HttpContent serialization.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task OpenApiResponseQuotaIsCheckedBeforeBufferingAsync(int excess)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            var response = new ReadResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = 29, ServiceResult = StatusCodes.Good },
                Results = [new DataValue(new Variant(new string('x', 1024)))]
            };
            byte[] payload = WebApiBodyCodec.EncodeBody(response, context, JsonEncoderOptions.Compact);
            context.MaxMessageSize = payload.Length - excess;
            foreach (long hint in new long[] { -1, 0, payload.Length })
            {
                using var content = new ProbeContent(payload, hint);
                using var handler = new ResponseHandler(content);
                using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/ua/") };
                using var client = new WebApiClient(http, new WebApiClientOptions { MessageContext = context });
                if (excess > 0)
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    {
                        await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);
                    });
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                }
                else
                {
                    ReadResponse decoded = await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);
                    Assert.That(decoded.ResponseHeader.RequestHandle, Is.EqualTo(29));
                    Assert.That(decoded.Results, Has.Count.EqualTo(1));
                    Assert.That(decoded.Results[0].WrappedValue.TryGetValue(out string text), Is.True);
                    Assert.That(text, Is.EqualTo(new string('x', 1024)));
                }
                Assert.That(content.SerializeCalls, Is.Zero);
                Assert.That(content.IsDisposed, Is.True);
            }
        }

        /// <summary>
        /// Verifies body reads respect the shorter timeout without mutating a shared HTTP client's settings.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RequestTimeoutRemainsActiveWhileReadingTheOpenApiBodyAsync(
            bool sharedClient,
            bool clientTimeoutIsShorter)
        {
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
            using var content = new ProbeContent(body.Object, -1);
            using var handler = new ResponseHandler(content);
            TimeSpan clientTimeout = clientTimeoutIsShorter ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost/ua/"),
                Timeout = clientTimeout
            };
            if (sharedClient)
            {
                using HttpResponseMessage warmup = await http.GetAsync(new Uri("https://localhost/warmup"))
                    .ConfigureAwait(false);
            }
            var options = new WebApiClientOptions
            {
                MessageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create()),
                RequestTimeout = clientTimeoutIsShorter ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1)
            };
            using var client = sharedClient
                ? new WebApiClient(http, new Uri("https://localhost/ua/"), options)
                : new WebApiClient(http, options);
            using var cancellation = new CancellationTokenSource();
            Task<ReadResponse> pending = client.ReadAsync(new ReadRequest(), cancellation.Token).AsTask();
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Task completed = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(pending));
                Assert.CatchAsync<OperationCanceledException>(() => pending);
                Assert.That(content.IsDisposed, Is.True);
                Assert.That(content.SerializeCalls, Is.Zero);
                if (sharedClient)
                {
                    Assert.That(http.Timeout, Is.EqualTo(clientTimeout));
                    Assert.That(http.DefaultRequestHeaders.Accept, Is.Empty);
                }
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
            }
        }

        /// <summary>
        /// Signals that a simulated body read began, then keeps it pending until its request token is cancelled.
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
        /// Serves probe content without network I/O and captures the token passed to the HTTP request.
        /// </summary>
        private sealed class ResponseHandler : HttpMessageHandler
        {
            /// <summary>
            /// Selects the probe body returned for requests other than the shared-client warmup.
            /// </summary>
            public ResponseHandler(ProbeContent content)
            {
                m_content = content;
            }

            /// <summary>
            /// Returns an empty warmup response or the probe body with the request cancellation token recorded.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.RequestUri?.AbsolutePath == "/warmup")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent([])
                    });
                }
                m_content.RequestToken = cancellationToken;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = m_content });
            }

            /// <summary>
            /// Holds the instrumented response body supplied to the Web API client.
            /// </summary>
            private readonly ProbeContent m_content;
        }

        /// <summary>
        /// Exposes a response stream while recording serialization attempts and disposal.
        /// </summary>
        private sealed class ProbeContent : HttpContent
        {
            /// <summary>
            /// Wraps fixed payload bytes in a read-only response stream with an optional length hint.
            /// </summary>
            public ProbeContent(byte[] payload, long hint)
                : this(new MemoryStream(payload, writable: false), hint)
            {
            }

            /// <summary>
            /// Uses the supplied stream and advertises a content length only when the hint is nonnegative.
            /// </summary>
            public ProbeContent(Stream body, long hint)
            {
                m_body = body;
                if (hint >= 0)
                {
                    Headers.ContentLength = hint;
                }
            }

            /// <summary>
            /// Gets how often HttpContent requested serialization instead of direct stream access.
            /// </summary>
            public int SerializeCalls { get; private set; }

            /// <summary>
            /// Gets whether disposing the response also disposed its content.
            /// </summary>
            public bool IsDisposed { get; private set; }

            /// <summary>
            /// Gets or sets the HTTP request token used if serialization copies the body.
            /// </summary>
            public CancellationToken RequestToken { get; set; }

            /// <summary>
            /// Records a buffering attempt and copies the body using the captured request token.
            /// </summary>
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                SerializeCalls++;
                return m_body.CopyToAsync(stream, 81920, RequestToken);
            }

            /// <summary>
            /// Returns the original body stream without serializing it into an intermediate buffer.
            /// </summary>
            protected override Task<Stream> CreateContentReadStreamAsync()
            {
                return Task.FromResult<Stream>(m_body);
            }

#if NET5_0_OR_GREATER
            /// <summary>
            /// Supplies the original stream through the cancellation-aware content API.
            /// </summary>
            protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult<Stream>(m_body);
            }
#endif

            /// <summary>
            /// Leaves content length unknown unless an explicit header hint was supplied.
            /// </summary>
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            /// <summary>
            /// Records content disposal and releases the stream used by the simulated response.
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    IsDisposed = true;
                    m_body.Dispose();
                }
                base.Dispose(disposing);
            }

            /// <summary>
            /// Holds the payload or blocking stream consumed by the response reader.
            /// </summary>
            private readonly Stream m_body;
        }
    }
}
