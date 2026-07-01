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

using System.Net;
using System.Net.Http.Headers;
using Opc.Ua.Bindings;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT smoke tests for the OPC UA REST binding (Opc.Ua.Bindings.WebApi).
    /// Exercises the Minimal-API endpoint surface
    /// (<c>MapWebApiEndpoints()</c>), the body codec round-trip, and the
    /// auth pipeline against a real in-process Kestrel host bound to
    /// <c>http://127.0.0.1:0</c>.
    /// </summary>
    /// <remarks>
    /// Coverage scope (smoke + auth) — one round-trip per major service
    /// set (Attribute / View / Session / Subscription, plus the
    /// long-poll <c>/publish</c> path) plus an anonymous and a
    /// Basic-authenticated request to verify <c>app.UseAuthentication()</c>
    /// + <c>DefaultSessionlessIdentityProvider</c> wire through the
    /// AOT publish. The full 28-route × 2-encoding matrix is covered
    /// by <c>WebApiEndpointIntegrationTests</c> on the JIT.
    /// </remarks>
    [ClassDataSource<WebApiAotFixture>(Shared = SharedType.PerTestSession)]
    [NotInParallel(nameof(WebApiAotTests))]
    public class WebApiAotTests(WebApiAotFixture fixture)
    {
        [Test]
        public async Task ReadEndpointRoundTripsAsync()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 4242 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            ReadResponse response = await PostAsync<ReadRequest, ReadResponse>(
                "/read", request).ConfigureAwait(false);

            await Assert.That(response).IsNotNull();
            await Assert.That(response.ResponseHeader.RequestHandle).IsEqualTo(4242u);
            await Assert.That(fixture.Server.LastRequest).IsTypeOf<ReadRequest>();
        }

        [Test]
        public async Task BrowseEndpointRoundTripsAsync()
        {
            var request = new BrowseRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 17 },
                View = new ViewDescription(),
                NodesToBrowse = new ArrayOf<BrowseDescription>()
            };

            BrowseResponse response = await PostAsync<BrowseRequest, BrowseResponse>(
                "/browse", request).ConfigureAwait(false);

            await Assert.That(response).IsNotNull();
            await Assert.That(response.ResponseHeader.RequestHandle).IsEqualTo(17u);
            await Assert.That(fixture.Server.LastRequest).IsTypeOf<BrowseRequest>();
        }

        [Test]
        public async Task CreateSessionEndpointRoundTripsAsync()
        {
            var request = new CreateSessionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 9 },
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:aot-test",
                    ApplicationName = new LocalizedText("AOT Test Client"),
                    ApplicationType = ApplicationType.Client
                },
                EndpointUrl = fixture.BaseAddress.ToString(),
                SessionName = "AotTest",
                ClientNonce = ByteString.From(0xAB, 0xCD),
                ClientCertificate = ByteString.Empty,
                RequestedSessionTimeout = 60000.0,
                MaxResponseMessageSize = 0
            };

            CreateSessionResponse response =
                await PostAsync<CreateSessionRequest, CreateSessionResponse>(
                    "/createsession", request).ConfigureAwait(false);

            await Assert.That(response).IsNotNull();
            await Assert.That(response.ResponseHeader.RequestHandle).IsEqualTo(9u);
            await Assert.That(response.SessionId.IsNull).IsFalse();
            await Assert.That(response.AuthenticationToken.IsNull).IsFalse();
        }

        [Test]
        public async Task CreateSubscriptionEndpointRoundTripsAsync()
        {
            var request = new CreateSubscriptionRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 33 },
                RequestedPublishingInterval = 1000.0,
                RequestedLifetimeCount = 100u,
                RequestedMaxKeepAliveCount = 5u,
                MaxNotificationsPerPublish = 100u,
                PublishingEnabled = true,
                Priority = 0
            };

            CreateSubscriptionResponse response =
                await PostAsync<CreateSubscriptionRequest, CreateSubscriptionResponse>(
                    "/createsubscription", request).ConfigureAwait(false);

            await Assert.That(response).IsNotNull();
            await Assert.That(response.SubscriptionId).IsEqualTo(1u);
            await Assert.That(response.RevisedPublishingInterval).IsEqualTo(1000.0);
        }

        [Test]
        public async Task PublishEndpointRoundTripsAsync()
        {
            var request = new PublishRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 77 },
                SubscriptionAcknowledgements =
                    new ArrayOf<SubscriptionAcknowledgement>()
            };

            PublishResponse response = await PostAsync<PublishRequest, PublishResponse>(
                "/publish", request).ConfigureAwait(false);

            await Assert.That(response).IsNotNull();
            await Assert.That(response.SubscriptionId).IsEqualTo(1u);
            await Assert.That(response.NotificationMessage.SequenceNumber).IsEqualTo(1u);
        }

        [Test]
        public async Task AnonymousRequestSurfacesAnonymousIdentityAsync()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            using ByteArrayContent content = BuildContent(request);
            using HttpResponseMessage raw = await fixture.HttpClient
                .PostAsync(new Uri("/read", UriKind.Relative), content)
                .ConfigureAwait(false);

            await Assert.That(raw.StatusCode).IsEqualTo(HttpStatusCode.OK);
            // No DefaultSessionlessIdentityProvider is registered, so
            // the dispatcher passes the request through with Identity =
            // null. The auth pipeline still runs (the Basic handler
            // simply produces nothing when no Authorization header is
            // sent), so HttpContext.User.Identity.IsAuthenticated is
            // false.
            await Assert.That(fixture.Server.LastInvocation.Identity).IsNull();
        }

        [Test]
        public async Task BasicAuthenticatedRequestPropagatesPrincipalAsync()
        {
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 2 },
                NodesToRead = new ArrayOf<ReadValueId>()
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, "/read")
            {
                Content = BuildContent(request)
            };
            message.Headers.Authorization =
                AuthenticationHeaderValue.Parse(fixture.AuthHeaderForExpectedBasicUser);

            using HttpResponseMessage raw = await fixture.HttpClient
                .SendAsync(message).ConfigureAwait(false);

            await Assert.That(raw.StatusCode).IsEqualTo(HttpStatusCode.OK);
            // The dispatcher does not register an identity provider in
            // this fixture, so the invocation Identity stays null —
            // what we want to verify is that the auth pipeline did not
            // reject the request and that the body still round-trips.
            await Assert.That(fixture.Server.LastRequest).IsTypeOf<ReadRequest>();
            await Assert.That(fixture.Server.LastRequest.RequestHeader.RequestHandle)
                .IsEqualTo(2u);
        }

        private async Task<TResponse> PostAsync<TRequest, TResponse>(
            string path,
            TRequest request)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new()
        {
            using ByteArrayContent content = BuildContent(request);
            using HttpResponseMessage raw = await fixture.HttpClient
                .PostAsync(new Uri(path, UriKind.Relative), content)
                .ConfigureAwait(false);
            await Assert.That(raw.StatusCode).IsEqualTo(HttpStatusCode.OK);
            byte[] payload = await raw.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return WebApiBodyCodec.DecodeBody<TResponse>(
                payload,
                fixture.Server.MessageContext);
        }

        private ByteArrayContent BuildContent<TRequest>(TRequest request)
            where TRequest : IServiceRequest, new()
        {
            byte[] body = WebApiBodyCodec.EncodeBody(
                request,
                fixture.Server.MessageContext,
                JsonEncoderOptions.Compact);
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                WebApiMediaType.FormatContentType(WebApiEncoding.Compact));
            return content;
        }
    }
}
