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

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.AuthorizationService
{
    /// <summary>
    /// Runs refresh-token dispatch on real captured stack sessions without a network.
    /// </summary>
    [TestFixture]
    [Category("SecurityRegression")]
    public sealed class AuthorizationServiceConfidentialDispatchTests
    {
        [TestCase("raw")]
        [TestCase("managed")]
        [TestCase("borrowed")]
        [TestCase("metadata")]
        public async Task SecurityR18RefreshTokenNeverDispatchesOnPhysicalSignChannelAsync(string shape)
        {
            await using var fixture = new SessionRequestContextTestContext(securityMode: MessageSecurityMode.Sign);
            if (shape == "metadata")
            {
                EndpointDescription actual = CoreUtils.Clone(fixture.InnerSession.ConfiguredEndpoint.Description)!;
                fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(actual);
                fixture.InnerSession.ConfiguredEndpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            }
            await using SessionRequestContext borrowed = await SessionRequestContext
                .CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ISession session = shape switch
            {
                "raw" => fixture.InnerSession,
                "borrowed" => borrowed.Session,
                _ => fixture.ManagedSession
            };
            var client = new AuthorizationServiceClient(session, s_serviceId);

            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.RefreshTokenAsync("urn:resource:A", "unused-refresh-A").ConfigureAwait(false));

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
            Assert.That(fixture.Requests, Is.Empty, "Neither resolution nor secret-bearing Call may reach Sign.");
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
        }

        [Test]
        public async Task SecurityR18UnsupportedCustomSessionCannotReceiveRefreshCredentialAsync()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.MessageContext)
                .Returns(ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            var client = new AuthorizationServiceClient(session.Object, s_serviceId);
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.RefreshTokenAsync("urn:resource:A", "unused-refresh-A").ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            session.Verify(value => value.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("raw")]
        [TestCase("managed")]
        [TestCase("borrowed")]
        public async Task SecurityR18CapturedEncryptedRefreshPreservesRotatedTokenOutputsAsync(string shape)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            fixture.Handler = (request, _) => new ValueTask<IServiceResponse>(Respond(request));
            await using SessionRequestContext borrowed = await SessionRequestContext
                .CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ISession session = shape switch
            {
                "raw" => fixture.InnerSession,
                "borrowed" => borrowed.Session,
                _ => fixture.ManagedSession
            };
            var client = new AuthorizationServiceClient(session, s_serviceId);
            (string access, DateTime expiry, string? refresh, DateTime refreshExpiry) =
                await client.RefreshTokenAsync("urn:resource:A", "unused-refresh-A").ConfigureAwait(false);

            Assert.That(access, Is.EqualTo("access-A"));
            Assert.That(expiry, Is.EqualTo(s_accessExpiry));
            Assert.That(refresh, Is.EqualTo("refresh-B"));
            Assert.That(refreshExpiry, Is.EqualTo(s_refreshExpiry));
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(fixture.Requests[0], Is.TypeOf<BrowseRequest>());
            var call = (CallRequest)fixture.Requests[1];
            Assert.That(call.RequestHeader.AuthenticationToken, Is.EqualTo(new NodeId("original-token", 0)));
            Assert.That(call.MethodsToCall.Count, Is.EqualTo(1));
            Assert.That(call.MethodsToCall[0].ObjectId, Is.EqualTo(s_serviceId));
            Assert.That(call.MethodsToCall[0].MethodId,
                Is.EqualTo(new NodeId(Methods.AuthorizationServiceType_RefreshToken, 1)));
            Assert.That(call.MethodsToCall[0].InputArguments[0].TryGetValue(out string resource), Is.True);
            Assert.That(call.MethodsToCall[0].InputArguments[1].TryGetValue(out string secret), Is.True);
            Assert.That(resource, Is.EqualTo("urn:resource:A"));
            Assert.That(secret, Is.EqualTo("unused-refresh-A"));
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
        }

        [TestCase("recreate")]
        [TestCase("transport")]
        [TestCase("namespace")]
        [TestCase("downgrade")]
        [TestCase("cancellation")]
        public async Task SecurityR18ReplacementCannotReceiveRefreshCredentialAsync(string mutation)
        {
            await using var fixture = new SessionRequestContextTestContext(
                securityMode: MessageSecurityMode.SignAndEncrypt);
            using var cancellation = new CancellationTokenSource();
            var replacement = new Mock<ITransportChannel>();
            fixture.Handler = (request, _) =>
            {
                Assert.That(request, Is.TypeOf<BrowseRequest>());
                switch (mutation)
                {
                    case "recreate":
                        fixture.Recreate();
                        break;
                    case "transport":
                        fixture.ReplaceTransport(replacement.Object);
                        break;
                    case "namespace":
                        fixture.InnerSession.NamespaceUris.Append("urn:changed");
                        break;
                    case "downgrade":
                        fixture.Channel.SetupGet(channel => channel.EndpointDescription).Returns(new EndpointDescription
                        {
                            EndpointUrl = "opc.tcp://localhost:4840",
                            SecurityMode = MessageSecurityMode.Sign,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        });
                        break;
                    default:
                        cancellation.Cancel();
                        break;
                }
                return new ValueTask<IServiceResponse>(Respond(request));
            };
            var client = new AuthorizationServiceClient(fixture.ManagedSession, s_serviceId);
            if (mutation == "cancellation")
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await client.RefreshTokenAsync("urn:resource:A", "unused-refresh-A", cancellation.Token)
                        .ConfigureAwait(false));
            }
            else
            {
                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await client.RefreshTokenAsync("urn:resource:A", "unused-refresh-A").ConfigureAwait(false));
                Assert.That(error.StatusCode, Is.EqualTo(mutation == "downgrade"
                    ? StatusCodes.BadSecurityModeInsufficient : StatusCodes.BadSessionIdInvalid));
            }
            Assert.That(fixture.Requests.OfType<CallRequest>(), Is.Empty);
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            replacement.Verify(value => value.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static IServiceResponse Respond(IServiceRequest request)
        {
            return request switch
            {
                BrowseRequest => new BrowseResponse { Results = [new BrowseResult { References = [] }] },
                CallRequest => new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            OutputArguments =
                            [
                                Variant.From("access-A"), Variant.From(s_accessExpiry),
                                Variant.From("refresh-B"), Variant.From(s_refreshExpiry)
                            ]
                        }
                    ]
                },
                _ => throw new InvalidOperationException("Unexpected confidential dispatch request.")
            };
        }

        private static readonly NodeId s_serviceId = new("authorization-service", 1);
        private static readonly DateTime s_accessExpiry = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_refreshExpiry = new(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    }
}
