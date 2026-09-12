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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    public sealed class SessionExpiryRegressionTests
    {
        [Test]
        public async Task ExpiredActivationClosesOutsideTheGlobalSessionGateAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var clock = new FakeTimeProvider();
            var server = new Mock<IServerInternal>();
            server.Setup(value => value.Telemetry).Returns(telemetry);
            server.Setup(value => value.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(value => value.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
            server.Setup(value => value.IdentityRegistry)
                .Returns(new ServerIdentityRegistry(new AnonymousAuthenticator()));
            server.Setup(value => value.DiagnosticsNodeManager).Returns(Mock.Of<IDiagnosticsNodeManager>());
            server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1_000,
                    MaxSessionTimeout = 60_000,
                    MaxSessionCount = 10,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:opcfoundation:test:session-expiry",
                    "SessionExpiry",
                    "CN=SessionExpiry",
                    ["localhost"])
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var manager = new SessionManager(server.Object, configuration, clock);
            server.Setup(value => value.SessionManager).Returns(manager);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            server.Setup(value => value.CloseSessionAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns<OperationContext, NodeId, bool, CancellationToken>(
                    (_, id, _, _) => manager.CloseSessionAsync(id, deadline.Token));
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost/expiry",
                SecurityPolicyUri = SecurityPolicies.None,
                SecurityMode = MessageSecurityMode.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            var context = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext("expiry-channel", endpoint, RequestEncoding.Binary),
                RequestType.ActivateSession,
                RequestLifetime.None);
            CreateSessionResult created = await CreateSessionAsync(
                manager, context, certificate, endpoint.EndpointUrl).ConfigureAwait(false);
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.That(created.Session.HasExpired, Is.True);

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await manager.ActivateSessionAsync(
                    context,
                    created.AuthenticationToken,
                    new SignatureData(),
                    default,
                    new SignatureData(),
                    [],
                    deadline.Token).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(deadline.IsCancellationRequested, Is.False);
            Assert.That(manager.GetSession(created.AuthenticationToken), Is.Null);

            CreateSessionResult next = await CreateSessionAsync(
                manager, context, certificate, endpoint.EndpointUrl).ConfigureAwait(false);
            await manager.ActivateSessionAsync(
                context, next.AuthenticationToken, new SignatureData(), default, new SignatureData(), [], deadline.Token)
                .ConfigureAwait(false);
            await manager.CloseSessionAsync(next.SessionId, deadline.Token).ConfigureAwait(false);
            Assert.That(manager.GetSessions(), Is.Empty);
        }

        private static ValueTask<CreateSessionResult> CreateSessionAsync(
            SessionManager manager,
            OperationContext context,
            Certificate certificate,
            string endpointUrl)
        {
            return manager.CreateSessionAsync(
                context,
                certificate,
                "expiry-regression",
                ByteString.From(new byte[32]),
                new ApplicationDescription
                {
                    ApplicationUri = "urn:opcfoundation:test:session-expiry"
                },
                endpointUrl,
                certificate.AddRef(),
                [],
                1_000,
                64 * 1024,
                CancellationToken.None);
        }
    }
}
