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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// How <see cref="WebApiClient"/> reports HTTP failures (as
    /// <see cref="ServiceResultException"/>s).
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

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            public TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
