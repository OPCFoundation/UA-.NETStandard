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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    [TestFixture]
    public sealed class ManagedSessionResourceOwnershipTests
    {
        [Test]
        public async Task BuilderDisposesOwnedHttpProviderAndChannelManagerAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint("https://ownership.test/");
            endpoint.Description.TransportProfileUri = Profiles.HttpsOpenApiTransport;
            ServiceMessageContext context = harness.Configuration.CreateMessageContext();
            context.NamespaceUris.GetIndexOrAppend("urn:test:ownership");
            using var handler = new ScriptedHttpHandler(
                harness.CreateOpenedStandaloneChannel(endpoint), endpoint, context);
            ManagedSessionBuilder builder = new ManagedSessionBuilder(harness.Configuration, harness.Telemetry)
                .UseEndpoint(endpoint)
                .WithHttpsResilience(_ => { })
                .WithWebApiAuthentication(options => options.HttpMessageHandler = handler);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using Client.ManagedSession session = await builder.ConnectAsync(deadline.Token)
                .ConfigureAwait(false);
            var manager = (ClientChannelManager)((IManagedTransportChannel)session.TransportChannel).Manager;
            var bindings = (ITransportChannelBindings)typeof(ClientChannelManager)
                .GetProperty("ChannelBindings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            using ITransportChannel probe = bindings.Create(Utils.UriSchemeHttps, harness.Telemetry)!;
            var factory = (IOpcUaHttpClientFactory)typeof(HttpsTransportChannel)
                .GetField("m_httpClientFactory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(probe)!;
            var provider = (IServiceProvider)factory.GetType()
                .GetField("m_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(factory)!;
            Assert.That(provider.GetService(typeof(IOpcUaHttpClientFactory)), Is.Not.Null);

            await session.DisposeAsync().ConfigureAwait(false);

            Assert.Throws<ObjectDisposedException>(() => provider.GetService(typeof(IOpcUaHttpClientFactory)));
            var participant = new Mock<IReconnectParticipant>();
            participant.SetupGet(value => value.Id).Returns("ownership-probe");
            participant.SetupGet(value => value.Endpoint).Returns(endpoint);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                using IManagedTransportChannel lease = await manager.GetAsync(
                    participant.Object, deadline.Token).ConfigureAwait(false);
            });
            Assert.That(handler.Disposed, Is.False, "An injected HTTP handler remains owned by its caller.");
        }

        [Test]
        public async Task BuilderDoesNotDisposeInjectedChannelManagerAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ManagedSessionBuilder builder = new ManagedSessionBuilder(harness.Configuration, harness.Telemetry)
                .UseEndpoint(endpoint)
                .WithChannelManager(harness.Manager)
                .WithHttpsResilience(_ => { });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using Client.ManagedSession session = await builder.ConnectAsync(deadline.Token)
                .ConfigureAwait(false);

            await session.DisposeAsync().ConfigureAwait(false);

            await using Session next = await harness.CreateSessionAsync(endpoint).ConfigureAwait(false);
            Assert.That(next.Connected, Is.True);
        }

        private sealed class ScriptedHttpHandler(
            ScriptedChannel channel,
            ConfiguredEndpoint endpoint,
            IServiceMessageContext context) : HttpMessageHandler
        {
            public bool Disposed { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage message,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
#if NET8_0_OR_GREATER
                byte[] body = await message.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
                byte[] body = await message.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
                var options = new JsonDecoderOptions { UpdateNamespaceTable = true };
                IServiceRequest request = message.RequestUri!.AbsolutePath switch
                {
                    "/getendpoints" => WebApiBodyCodec.DecodeBody<GetEndpointsRequest>(body, context, options),
                    "/createsession" => WebApiBodyCodec.DecodeBody<CreateSessionRequest>(body, context, options),
                    "/activatesession" => WebApiBodyCodec.DecodeBody<ActivateSessionRequest>(body, context, options),
                    "/read" => WebApiBodyCodec.DecodeBody<ReadRequest>(body, context, options),
                    "/closesession" => WebApiBodyCodec.DecodeBody<CloseSessionRequest>(body, context, options),
                    _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported)
                };
                IServiceResponse response = request is GetEndpointsRequest
                    ? new GetEndpointsResponse
                    {
                        ResponseHeader = channel.CreateGoodHeader(),
                        Endpoints = [endpoint.Description]
                    }
                    : channel.CreateResponse(request);
                response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                var content = new ByteArrayContent(
                    WebApiBodyCodec.EncodeBody(response, context, JsonEncoderOptions.Compact));
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    WebApiMediaType.FormatContentType(WebApiEncoding.Compact));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
