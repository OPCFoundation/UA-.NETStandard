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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// How <see cref="WebApiClient"/> reports HTTP failures (as
    /// <see cref="ServiceResultException"/>s) and handles gzip compressed
    /// responses (OPC 10000-6 §7.4.5).
    /// </summary>
    [TestFixture]
    [Category("WebApiClient")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebApiClientErrorAndCompressionTests
    {
        private static readonly Uri s_baseAddress = new("https://server.example/");

        public static IEnumerable<TestCaseData> HttpStatusCases()
        {
            yield return new TestCaseData(HttpStatusCode.BadRequest, false, StatusCodes.BadDecodingError);
            yield return new TestCaseData(HttpStatusCode.Unauthorized, false, StatusCodes.BadIdentityTokenInvalid);
            yield return new TestCaseData(HttpStatusCode.Unauthorized, true, StatusCodes.BadUserAccessDenied);
            yield return new TestCaseData(HttpStatusCode.Forbidden, true, StatusCodes.BadUserAccessDenied);
            yield return new TestCaseData(HttpStatusCode.NotFound, false, StatusCodes.BadServiceUnsupported);
            yield return new TestCaseData(HttpStatusCode.MethodNotAllowed, false, StatusCodes.BadServiceUnsupported);
            yield return new TestCaseData(HttpStatusCode.NotImplemented, false, StatusCodes.BadServiceUnsupported);
            yield return new TestCaseData(HttpStatusCode.RequestTimeout, false, StatusCodes.BadTimeout);
            yield return new TestCaseData(HttpStatusCode.GatewayTimeout, false, StatusCodes.BadTimeout);
            yield return new TestCaseData(HttpStatusCode.RequestEntityTooLarge, false, StatusCodes.BadRequestTooLarge);
            yield return new TestCaseData(
                HttpStatusCode.UnsupportedMediaType, false, StatusCodes.BadDataEncodingUnsupported);
            yield return new TestCaseData(HttpStatusCode.InternalServerError, false, StatusCodes.BadInternalError);
            yield return new TestCaseData(HttpStatusCode.BadGateway, false, StatusCodes.BadCommunicationError);
            yield return new TestCaseData(HttpStatusCode.Conflict, false, StatusCodes.BadCommunicationError);
        }

        [TestCaseSource(nameof(HttpStatusCases))]
        public void HttpErrorStatusSurfacesAsServiceResultException(
            HttpStatusCode status,
            bool sendCredentials,
            StatusCode expected)
        {
            using var handler = new StubHandler(_ => new HttpResponseMessage(status) { ReasonPhrase = "Test reason" });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions
            {
                BearerToken = sendCredentials ? "token" : null
            });

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(error.Message, Does.Contain(((int)status).ToString(CultureInfo.InvariantCulture)));
            Assert.That(error.Message, Does.Contain("Test reason"));
            Assert.That(error.Message, Does.Contain("/read"));
            var inner = error.InnerException as HttpRequestException;
            Assert.That(inner, Is.Not.Null);
            Assert.That(inner!.StatusCode, Is.EqualTo(status));
        }

        /// <summary>
        /// A throttled request (429, or 503 as a rate limiter answers by
        /// default) stays BadServerTooBusy with the Retry-After hint, the
        /// same answer the HTTPS binary channel gives.
        /// </summary>
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void ThrottledRequestSurfacesAsServerTooBusyWithRetryAfter(HttpStatusCode status)
        {
            using var handler = new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(status);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return response;
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions());

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(error.Result.AdditionalInfo, Is.EqualTo("RetryAfterMs=2000"));
        }

        [Test]
        public void ConnectionFailureMapsLikeTheTransportChannel()
        {
            var failure = new HttpRequestException(
                "Connection refused",
                new SocketException((int)SocketError.ConnectionRefused));
            using var handler = new StubHandler(_ => throw failure);
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions());

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(error.StatusCode, Is.EqualTo(HttpsTransportChannel.MapRequestFailure(failure)));
            Assert.That(error.InnerException, Is.SameAs(failure));
            Assert.That(error.Message, Does.Contain("/read"));
        }

        [Test]
        public void TlsHandshakeFailureMapsLikeTheTransportChannel()
        {
            var failure = new HttpRequestException(
                "The SSL connection could not be established.",
                new System.Security.Authentication.AuthenticationException("The remote certificate was rejected."));
            using var handler = new StubHandler(_ => throw failure);
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions());

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(HttpsTransportChannel.MapRequestFailure(failure)));
            Assert.That(error.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void ElapsedRequestTimeoutSurfacesAsBadRequestTimeout()
        {
            using var handler = new StubHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(100)
            });

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadRequestTimeout));
            Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void CallerCancellationStaysOperationCanceled()
        {
            using var handler = new StubHandler(async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions());
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            Assert.CatchAsync<OperationCanceledException>(
                async () => await client.ReadAsync(new ReadRequest(), cts.Token).ConfigureAwait(false));
        }

        [Test]
        public async Task AcceptCompressedResponsesAdvertisesGzipAndDecompressesAsync()
        {
            IServiceMessageContext context = CreateContext();
            var expected = new ReadResponse { ResponseHeader = new ResponseHeader { RequestHandle = 42 } };
            HttpRequestMessage? seen = null;
            using var handler = new StubHandler(request =>
            {
                seen = request;
                return GzipResponse(WebApiBodyCodec.EncodeBody(expected, context));
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions
            {
                MessageContext = context,
                AcceptCompressedResponses = true
            });

            ReadResponse response = await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);

            Assert.That(response.ResponseHeader.RequestHandle, Is.EqualTo(42u));
            Assert.That(seen!.Headers.AcceptEncoding.Select(e => e.Value), Does.Contain("gzip"));
        }

        [Test]
        public async Task SharedClientCarriesAcceptEncodingPerRequestAsync()
        {
            IServiceMessageContext context = CreateContext();
            HttpRequestMessage? seen = null;
            using var handler = new StubHandler(request =>
            {
                seen = request;
                return GzipResponse(WebApiBodyCodec.EncodeBody(new ReadResponse(), context));
            });
            using var http = new HttpClient(handler, disposeHandler: false);
            using var client = new WebApiClient(http, s_baseAddress, new WebApiClientOptions
            {
                MessageContext = context,
                AcceptCompressedResponses = true
            });

            await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);

            Assert.That(seen!.Headers.AcceptEncoding.Select(e => e.Value), Does.Contain("gzip"));
            Assert.That(http.DefaultRequestHeaders.AcceptEncoding, Is.Empty, "a shared client is never mutated");
        }

        [Test]
        public async Task GzipResponseIsDecompressedEvenWhenNotAskedForAsync()
        {
            IServiceMessageContext context = CreateContext();
            HttpRequestMessage? seen = null;
            using var handler = new StubHandler(request =>
            {
                seen = request;
                return GzipResponse(WebApiBodyCodec.EncodeBody(new ReadResponse(), context));
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions { MessageContext = context });

            ReadResponse response = await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(seen!.Headers.AcceptEncoding, Is.Empty);
        }

        [Test]
        public void GzipBodyIsBoundedByMaxMessageSizeAfterInflating()
        {
            // 1 MiB of zeros compresses to about 1 KiB; the budget applies
            // to the inflated body.
            ServiceMessageContext context = CreateContext();
            context.MaxMessageSize = 64 * 1024;
            using var handler = new StubHandler(_ => GzipResponse(new byte[1024 * 1024]));
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions { MessageContext = context });

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
        }

        /// <summary>
        /// The declared Content-Length of a gzip body is its compressed size,
        /// which the gzip header and trailer can make larger than the inflated
        /// body; only the inflated size counts against MaxMessageSize.
        /// </summary>
        [Test]
        public async Task GzipBodyLargerThanTheLimitButInflatingWithinItIsReadAsync()
        {
            byte[] body = Encoding.UTF8.GetBytes("{}");
            byte[] compressed = Gzip(body);
            const int maxMessageSize = 16;
            Assert.That(compressed, Has.Length.GreaterThan(maxMessageSize));
            using var content = new ByteArrayContent(compressed);
            content.Headers.ContentEncoding.Add("gzip");
            content.Headers.ContentLength = compressed.Length;

            byte[] read = await HttpResponseBodyReader.ReadAsync(content, maxMessageSize, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(read, Is.EqualTo(body));
        }

        [Test]
        public void PlainBodyDeclaringMoreThanTheLimitIsRejectedUpFront()
        {
            byte[] body = new byte[32];
            using var content = new ByteArrayContent(body);
            content.Headers.ContentLength = body.Length;

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await HttpResponseBodyReader.ReadAsync(content, 16, CancellationToken.None)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
        }

        [Test]
        public void CorruptGzipIsADecodingError()
        {
            using var handler = new StubHandler(_ =>
            {
                // A complete gzip header followed by a deflate block of the
                // reserved block type 3, which no inflater accepts.
                var content = new ByteArrayContent(
                    [0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0x07, 0x00]);
                content.Headers.ContentEncoding.Add("gzip");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions { MessageContext = CreateContext() });

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(error.InnerException, Is.InstanceOf<InvalidDataException>());
        }

        [Test]
        public void UnsupportedContentEncodingIsRejected()
        {
            using var handler = new StubHandler(_ =>
            {
                var content = new ByteArrayContent([1, 2, 3]);
                content.Headers.ContentEncoding.Add("br");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions { MessageContext = CreateContext() });

            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.ReadAsync(new ReadRequest()).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(error.Message, Does.Contain("br"));
        }

        [Test]
        public async Task IdentityContentEncodingIsReadAsIsAsync()
        {
            IServiceMessageContext context = CreateContext();
            using var handler = new StubHandler(_ =>
            {
                var content = new ByteArrayContent(WebApiBodyCodec.EncodeBody(new ReadResponse(), context));
                content.Headers.ContentEncoding.Add("identity");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });
            using WebApiClient client = CreateClient(handler, new WebApiClientOptions { MessageContext = context });

            ReadResponse response = await client.ReadAsync(new ReadRequest()).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
        }

        /// <summary>
        /// The response stream belongs to the <see cref="HttpContent"/>, which
        /// the caller disposes with the response; the reader must not dispose
        /// it, with or without gzip.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResponseStreamIsLeftToItsContentAsync(bool gzip)
        {
            byte[] body = Encoding.UTF8.GetBytes("{\"Value\":1}");
            using var stream = new TrackingStream(gzip ? Gzip(body) : body);
            using var content = new StreamOwningContent(stream);
            if (gzip)
            {
                content.Headers.ContentEncoding.Add("gzip");
            }

            byte[] read = await HttpResponseBodyReader.ReadAsync(content, 1024, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(read, Is.EqualTo(body));
            Assert.That(stream.IsDisposed, Is.False, "The reader disposed the stream of the content.");
            content.Dispose();
            Assert.That(stream.IsDisposed, Is.True);
        }

        private static HttpResponseMessage GzipResponse(byte[] body)
        {
            var content = new ByteArrayContent(Gzip(body));
            content.Headers.ContentType = new MediaTypeHeaderValue(WebApiMediaType.ContentType);
            content.Headers.ContentEncoding.Add("gzip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        private static byte[] Gzip(byte[] body)
        {
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(body, 0, body.Length);
            }
            return buffer.ToArray();
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(new TestTelemetryContext());
        }

        private static WebApiClient CreateClient(HttpMessageHandler handler, WebApiClientOptions options)
        {
            options.HttpMessageHandler = handler;
            options.MessageContext ??= CreateContext();
            return WebApiClient.Create(s_baseAddress, options);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                m_respond = (request, _) => Task.FromResult(respond(request));
            }

            public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            {
                m_respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return m_respond(request, cancellationToken);
            }

            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> m_respond;
        }

        /// <summary>
        /// Content that hands out the stream it owns, as the content of an
        /// HTTP response does, and disposes it with itself.
        /// </summary>
        private sealed class StreamOwningContent : HttpContent
        {
            public StreamOwningContent(Stream stream)
            {
                m_stream = stream;
            }

            protected override Task<Stream> CreateContentReadStreamAsync()
            {
                return Task.FromResult(m_stream);
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return m_stream.CopyToAsync(stream);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_stream.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly Stream m_stream;
        }

        /// <summary>
        /// A memory stream that records whether it was disposed.
        /// </summary>
        private sealed class TrackingStream : MemoryStream
        {
            public TrackingStream(byte[] buffer)
                : base(buffer, writable: false)
            {
            }

            public bool IsDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
