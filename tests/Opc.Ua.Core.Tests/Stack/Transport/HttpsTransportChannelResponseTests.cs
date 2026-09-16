/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
 *
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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// How <see cref="HttpsTransportChannel"/> treats the response it gets back.
    /// </summary>
    /// <remarks>
    /// Two defects are covered. The whole body used to be buffered into memory
    /// before <c>MaxMessageSize</c> was applied, so the limit only ever decided
    /// what to do with an allocation that had already happened. And a failed
    /// request escaped as a raw <see cref="HttpRequestException"/> on .NET,
    /// where the inner exception is a <see cref="SocketException"/> rather than
    /// the <see cref="WebException"/> the mapping looked for - callers that
    /// expect a <see cref="ServiceResultException"/> saw a transport exception
    /// instead.
    /// </remarks>
    [TestFixture]
    [Category("TransportChannelDeterministic")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class HttpsTransportChannelResponseTests
    {
        private const int kMaxMessageSize = 4096;

        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// A response that declares a length above the limit is refused on the
        /// headers, before the body is read at all.
        /// </summary>
        [Test]
        public async Task ResponseWithContentLengthAboveTheLimitIsRefusedAsync()
        {
            var content = new ByteArrayContent(new byte[kMaxMessageSize * 4]);

            using HttpsTransportChannel channel = await CreateChannelAsync(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                })).ConfigureAwait(false);

            Assert.That(
                async () => await channel
                    .SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((uint)StatusCodes.BadResponseTooLarge));
        }

        /// <summary>
        /// A response that declares no length - a chunked one - is bounded as
        /// the body arrives instead.
        /// </summary>
        [Test]
        public async Task ResponseWithoutContentLengthIsBoundedWhileReadingAsync()
        {
            using HttpsTransportChannel channel = await CreateChannelAsync(
                new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(
                        new UnboundedStream(kMaxMessageSize * 4))
                })).ConfigureAwait(false);

            Assert.That(
                async () => await channel
                    .SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((uint)StatusCodes.BadResponseTooLarge));
        }

        /// <summary>
        /// A connect failure is reported as a transport problem the reconnect
        /// policy may retry.
        /// </summary>
        [Test]
        public async Task ConnectFailureIsReportedAsBadNotConnectedAsync()
        {
            using HttpsTransportChannel channel = await CreateChannelAsync(
                new StubHandler(_ => throw new HttpRequestException(
                    "refused",
                    new SocketException((int)SocketError.ConnectionRefused))))
                .ConfigureAwait(false);

            Assert.That(
                async () => await channel
                    .SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((uint)StatusCodes.BadNotConnected));
        }

        /// <summary>
        /// A rejected TLS handshake is permanent for this endpoint, so it is not
        /// reported as "not connected" - that would tell the reconnect policy to
        /// retry a request that fails the same way every time.
        /// </summary>
        [Test]
        public async Task TlsFailureIsReportedAsBadSecurityChecksFailedAsync()
        {
            using HttpsTransportChannel channel = await CreateChannelAsync(
                new StubHandler(_ => throw new HttpRequestException(
                    "handshake",
                    new AuthenticationException("certificate rejected"))))
                .ConfigureAwait(false);

            Assert.That(
                async () => await channel
                    .SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((uint)StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// An HTTP error status reached the server, so it is not a connect
        /// failure either.
        /// </summary>
        [Test]
        public async Task HttpErrorStatusIsReportedAsBadUnknownResponseAsync()
        {
            using HttpsTransportChannel channel = await CreateChannelAsync(
                new StubHandler(_ => new HttpResponseMessage(
                    HttpStatusCode.InternalServerError)
                {
                    Content = new ByteArrayContent([])
                })).ConfigureAwait(false);

            Assert.That(
                async () => await channel
                    .SendRequestAsync(new ReadRequest(), CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode")
                    .EqualTo((uint)StatusCodes.BadUnknownResponse));
        }

        private async Task<HttpsTransportChannel> CreateChannelAsync(StubHandler handler)
        {
            var channel = new HttpsTransportChannel(
                Utils.UriSchemeHttps,
                m_telemetry,
                timeProvider: null,
                httpClientFactory: new StubClientFactory(handler));

            var url = new Uri("https://localhost:4840/UA");
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 10000;
            configuration.MaxMessageSize = kMaxMessageSize;

            await channel.OpenAsync(
                url,
                new TransportChannelSettings
                {
                    Description = new EndpointDescription { EndpointUrl = url.ToString() },
                    Configuration = configuration,
                    Factory = ServiceMessageContext.CreateEmpty(m_telemetry).Factory,
                    NamespaceUris = new NamespaceTable()
                },
                CancellationToken.None).ConfigureAwait(false);

            return channel;
        }

        private sealed class StubClientFactory : IOpcUaHttpClientFactory
        {
            public StubClientFactory(StubHandler handler)
            {
                m_handler = handler;
            }

            public HttpClient CreateClient(string name)
            {
                return new HttpClient(m_handler, disposeHandler: false);
            }

            private readonly StubHandler m_handler;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                m_responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(m_responder(request));
            }

            private readonly Func<HttpRequestMessage, HttpResponseMessage> m_responder;
        }

        /// <summary>
        /// A body that reports no length and keeps producing bytes, which is
        /// what a chunked response looks like to the reader.
        /// </summary>
        private sealed class UnboundedStream : Stream
        {
            public UnboundedStream(int totalBytes)
            {
                m_remaining = totalBytes;
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = Math.Min(count, m_remaining);
                m_remaining -= read;
                return read;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            private int m_remaining;
        }
    }
}
