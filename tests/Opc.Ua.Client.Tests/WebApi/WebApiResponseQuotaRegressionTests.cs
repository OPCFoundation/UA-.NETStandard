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
    [TestFixture]
    [Category("WebApi")]
    public sealed class WebApiResponseQuotaRegressionTests
    {
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

        [Test]
        public async Task RequestTimeoutRemainsActiveWhileReadingTheOpenApiBodyAsync()
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
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/ua/") };
            using var client = new WebApiClient(http, new WebApiClientOptions
            {
                MessageContext = ServiceMessageContext.Create(NUnitTelemetryContext.Create()),
                RequestTimeout = TimeSpan.FromSeconds(1)
            });
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

        private static async Task<int> WaitForCancellationAsync(
            TaskCompletionSource<bool> started,
            CancellationToken cancellationToken)
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        private sealed class ResponseHandler : HttpMessageHandler
        {
            public ResponseHandler(ProbeContent content)
            {
                m_content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                m_content.RequestToken = cancellationToken;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = m_content });
            }

            private readonly ProbeContent m_content;
        }

        private sealed class ProbeContent : HttpContent
        {
            public ProbeContent(byte[] payload, long hint)
                : this(new MemoryStream(payload, writable: false), hint)
            {
            }

            public ProbeContent(Stream body, long hint)
            {
                m_body = body;
                if (hint >= 0)
                {
                    Headers.ContentLength = hint;
                }
            }

            public int SerializeCalls { get; private set; }
            public bool IsDisposed { get; private set; }
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
                if (disposing)
                {
                    IsDisposed = true;
                    m_body.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly Stream m_body;
        }
    }
}
