/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Bindings.WebApi.Endpoints;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises listener-bound REST dispatch with the real endpoint role evaluator.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class WebApiListenerIsolationSecurityTests
    {
        [Test]
        public async Task SecurityR02ListenerACannotUseListenerBRolesAsync()
        {
            await using var fixture = new ListenerFixture();
            IServiceResponse read = await fixture.InvokeAsync(fixture.A, new ReadRequest()).ConfigureAwait(false);
            IServiceResponse call = await fixture.InvokeAsync(fixture.A, new CallRequest()).ConfigureAwait(false);
            Assert.That(read.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(call.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.ProviderCalls, Is.Zero);
            Assert.That(fixture.AcceptedEndpoints, Is.All.EqualTo(ListenerFixture.UrlA));
        }

        [Test]
        public async Task SecurityR02ListenerBRetainsEndpointRestrictedRoleAsync()
        {
            await using var fixture = new ListenerFixture();
            foreach (IServiceRequest request in new IServiceRequest[]
                { new CreateSessionRequest(), new ActivateSessionRequest(), new ReadRequest(), new CallRequest() })
            {
                IServiceResponse response = await fixture.InvokeAsync(fixture.B, request).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                if (response is ReadResponse read)
                {
                    Assert.That(read.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(73));
                }
                if (response is CallResponse call)
                {
                    Assert.That(call.Results[0].OutputArguments[0].TryGetValue(out int value), Is.True);
                    Assert.That(value, Is.EqualTo(73));
                }
            }
            Assert.That(fixture.ChannelIds, Is.All.EqualTo(fixture.B.ListenerId));
            Assert.That(fixture.AcceptedEndpoints, Is.All.EqualTo(ListenerFixture.UrlB));
            Assert.That(fixture.ProviderCalls, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SecurityR02ListenerContextIgnoresStartupOrderAndCallerEndpointClaimsAsync(bool reverse)
        {
            await using var fixture = new ListenerFixture(reverse);
            for (int i = 0; i < 4; i++)
            {
                WebApiServer server = i % 2 == 0 ? fixture.A : fixture.B;
                IServiceResponse response = await fixture.InvokeAsync(server, new ReadRequest()).ConfigureAwait(false);
                Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(
                    i % 2 == 0 ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
            }
            Assert.That(fixture.A.ListenerId, Is.Not.EqualTo(fixture.B.ListenerId));
            using var scope = fixture.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Features.Set(fixture.A);
            context.Request.Host = new HostString("listener-b.test");
            context.Request.ContentType = "application/json";
            using var input = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes("{\"RequestHeader\":{\"RequestHandle\":73}}"));
            using var output = new MemoryStream();
            context.Request.Body = input;
            context.Response.Body = output;
            await WebApiEndpointDispatcher.HandleAsync<ReadRequest, ReadResponse>(context).ConfigureAwait(false);
            Assert.That(context.Response.StatusCode, Is.EqualTo(200));
            Assert.That(System.Text.Encoding.UTF8.GetString(output.ToArray()),
                Does.Contain(((uint)StatusCodes.BadUserAccessDenied)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(fixture.AcceptedEndpoints[^1], Is.EqualTo(ListenerFixture.UrlA));
        }

        [Test]
        public async Task SecurityR02IncompatibleListenerCannotBorrowDefaultAuthorityAsync()
        {
            await using var fixture = new ListenerFixture();
            await using HttpsTransportListener listener = fixture.CreateListener(
                ListenerFixture.UrlA, Profiles.UaTcpTransport);
            var adapter = new WebApiServer(listener);
            IServiceResponse response = await fixture.InvokeAsync(adapter, new ReadRequest()).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
            Assert.That(fixture.AcceptedEndpoints, Is.Empty);
            Assert.That(fixture.ProviderCalls, Is.Zero);
        }

        private sealed class ListenerFixture : IAsyncDisposable
        {
            internal ListenerFixture(bool reverse = false)
            {
                m_telemetry = NUnitTelemetryContext.Create();
                Assert.That(ServiceResult.IsGood(m_roles.AddIdentity(ObjectIds.WellKnownRole_Operator,
                    new IdentityMappingRuleType { CriteriaType = IdentityCriteriaType.AuthenticatedUser })), Is.True);
                Assert.That(ServiceResult.IsGood(m_roles.AddEndpoint(ObjectIds.WellKnownRole_Operator,
                    new EndpointType { EndpointUrl = UrlB })), Is.True);
                m_identity = new Mock<IUserIdentity>();
                m_identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                m_identity.SetupGet(value => value.DisplayName).Returns("operator");
                m_identity.SetupGet(value => value.GrantedRoleIds).Returns([]);
                m_callback = new Mock<ITransportListenerCallback>();
                m_callback.Setup(value => value.ProcessRequestAsync(
                        It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                    .Returns<SecureChannelContext, IServiceRequest, CancellationToken>(DispatchAsync);
                if (reverse)
                {
                    m_b = CreateListener(UrlB);
                    m_a = CreateListener(UrlA);
                }
                else
                {
                    m_a = CreateListener(UrlA);
                    m_b = CreateListener(UrlB);
                }
                A = new WebApiServer(m_a);
                B = new WebApiServer(m_b);
                var shared = new WebApiServer(m_a.MessageContext!, "shared-default");
                shared.Attach(m_callback.Object);
                shared.UpdateDefaultEndpoint(new EndpointDescription { EndpointUrl = UrlB });
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<IWebApiServer>(shared);
                Services = services.BuildServiceProvider();
            }

            internal WebApiServer A { get; }
            internal WebApiServer B { get; }
            internal ServiceProvider Services { get; }
            internal int ProviderCalls { get; private set; }
            internal List<string> AcceptedEndpoints { get; } = [];
            internal List<string> ChannelIds { get; } = [];

            internal HttpsTransportListener CreateListener(string url, string profile = Profiles.HttpsOpenApiTransport)
            {
                var listener = new HttpsTransportListener(Utils.UriSchemeHttps, m_telemetry);
                listener.Initialize(new Uri(url), new TransportListenerSettings
                {
                    Configuration = EndpointConfiguration.Create(),
                    HttpsMutualTls = false,
                    Descriptions =
                    [
                        new EndpointDescription
                        {
                            EndpointUrl = url,
                            TransportProfileUri = profile,
                            SecurityMode = MessageSecurityMode.None,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                }, m_callback.Object);
                return listener;
            }

            internal ValueTask<IServiceResponse> InvokeAsync(WebApiServer server, IServiceRequest request)
            {
                request.RequestHeader = new RequestHeader { RequestHandle = 73 };
                return server.InvokeAsync(request, new WebApiInvocationContext
                {
                    SecureChannelId = Guid.NewGuid().ToString(),
                    Endpoint = new EndpointDescription { EndpointUrl = UrlB },
                    Identity = m_identity.Object
                }, CancellationToken.None);
            }

            public async ValueTask DisposeAsync()
            {
                await m_a.DisposeAsync().ConfigureAwait(false);
                await m_b.DisposeAsync().ConfigureAwait(false);
                await Services.DisposeAsync().ConfigureAwait(false);
                m_roles.Dispose();
            }

            private ValueTask<IServiceResponse> DispatchAsync(
                SecureChannelContext channel, IServiceRequest request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                AcceptedEndpoints.Add(channel.EndpointDescription!.EndpointUrl!);
                ChannelIds.Add(channel.SecureChannelId);
                IList<NodeId> roles = m_roles.ResolveGrantedRoles(
                    channel.UpstreamIdentity ?? m_identity.Object, null, channel.EndpointDescription);
                if (!roles.Contains(ObjectIds.WellKnownRole_Operator))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                IServiceResponse response = request switch
                {
                    CreateSessionRequest => new CreateSessionResponse(),
                    ActivateSessionRequest => new ActivateSessionResponse(),
                    CallRequest => new CallResponse
                    {
                        Results = [new CallMethodResult { OutputArguments = [Variant.From(73)] }]
                    },
                    _ => new ReadResponse { Results = [new DataValue(Variant.From(73))] }
                };
                if (request is ReadRequest or CallRequest)
                {
                    ProviderCalls++;
                }
                response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                return new ValueTask<IServiceResponse>(response);
            }

            internal const string UrlA = "https://listener-a.test/ua";
            internal const string UrlB = "https://listener-b.test/ua";
            private readonly ITelemetryContext m_telemetry;
            private readonly RoleManager m_roles = new();
            private readonly Mock<IUserIdentity> m_identity;
            private readonly Mock<ITransportListenerCallback> m_callback;
            private readonly HttpsTransportListener m_a;
            private readonly HttpsTransportListener m_b;
        }
    }
}
#endif
