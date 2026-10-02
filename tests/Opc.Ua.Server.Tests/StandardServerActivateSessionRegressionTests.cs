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
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies how <see cref="StandardServer.ActivateSessionAsync"/> reports
    /// failures before and after the activation committed.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class StandardServerActivateSessionRegressionTests
    {
        /// <summary>
        /// AS-2: a failing ephemeral-key step after the activation committed must not
        /// fault the response; the client needs the serverNonce the session now
        /// expects (Part 4 5.7.3.1).
        /// </summary>
        [Test]
        public async Task PostCommitFailureStillReturnsTheServerNonceAsync()
        {
            var fixture = new ServerFixture<ActivateTestServer>(
                telemetry => new ActivateTestServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                ActivateTestServer server = await fixture.StartAsync().ConfigureAwait(false);
                SecureChannelContext channel = CreateChannel(server, "post-commit");
                NodeId token = await CreateSessionAsync(server, channel).ConfigureAwait(false);
                server.ThrowFromAdditionalParameters = true;

                using var lifetime = new RequestLifetime();
                ActivateSessionResponse response = await server.ActivateSessionAsync(
                    channel, new RequestHeader { AuthenticationToken = token }, null, default, default,
                    default, null, lifetime).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
                Assert.That(response.ServerNonce.IsEmpty, Is.False);
                Assert.That(server.CurrentInstance.SessionManager.GetSession(token)!.Activated, Is.True);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// AS-2: a failure that is not a ServiceResultException (here a cancelled
        /// authentication) is still audited and counted as a rejected session.
        /// </summary>
        [Test]
        public async Task CancelledActivationIsCountedAsRejectedAsync()
        {
            var fixture = new ServerFixture<ActivateTestServer>(
                telemetry => new ActivateTestServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                ActivateTestServer server = await fixture.StartAsync().ConfigureAwait(false);
                SecureChannelContext channel = CreateChannel(server, "cancelled-activate");
                NodeId token = await CreateSessionAsync(server, channel).ConfigureAwait(false);
                uint rejectedBefore = 0;
                server.CurrentInstance.UpdateServerDiagnostics(
                    diagnostics => rejectedBefore = diagnostics.RejectedSessionCount);
                server.CancelAuthentication = true;

                using var lifetime = new RequestLifetime();
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await server.ActivateSessionAsync(
                        channel, new RequestHeader { AuthenticationToken = token }, null, default, default,
                        default, null, lifetime).ConfigureAwait(false));

                server.CurrentInstance.UpdateServerDiagnostics(diagnostics =>
                    Assert.That(diagnostics.RejectedSessionCount, Is.EqualTo(rejectedBefore + 1)));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// K15: the post-commit activation callback (e.g. the distributed session
        /// mirror write) cannot fail the activation, so the request's cancellation
        /// (TimeoutHint, Cancel) must not silently abort it while the client gets Good.
        /// </summary>
        [Test]
        public async Task PostCommitCallbackIsNotBoundToTheRequestCancellationAsync()
        {
            var fixture = new ServerFixture<ActivateTestServer>(
                telemetry => new ActivateTestServer(telemetry))
            {
                SecurityNone = true
            };
            try
            {
                ActivateTestServer server = await fixture.StartAsync().ConfigureAwait(false);
                SecureChannelContext channel = CreateChannel(server, "post-commit-token");
                NodeId token = await CreateSessionAsync(server, channel).ConfigureAwait(false);

                using var lifetime = new RequestLifetime();
                Assert.That(lifetime.CancellationToken.CanBeCanceled, Is.True);
                ActivateSessionResponse response = await server.ActivateSessionAsync(
                    channel, new RequestHeader { AuthenticationToken = token }, null, default, default,
                    default, null, lifetime).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
                Assert.That(server.ActivatedCallbackToken, Is.Not.Null);
                Assert.That(server.ActivatedCallbackToken.Value.CanBeCanceled, Is.False);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static SecureChannelContext CreateChannel(StandardServer server, string channelId)
        {
            EndpointDescription endpoint = server.GetEndpoints().Find(
                candidate => candidate.SecurityPolicyUri == SecurityPolicies.None);
            Assert.That(endpoint, Is.Not.Null);
            return new SecureChannelContext(channelId, endpoint, RequestEncoding.Binary, null, null);
        }

        private static async Task<NodeId> CreateSessionAsync(StandardServer server, SecureChannelContext channel)
        {
            using var lifetime = new RequestLifetime();
            CreateSessionResponse response = await server.CreateSessionAsync(
                channel, new RequestHeader(), null, null, null, "activate-regression",
                default, default, 60000, 0, lifetime).ConfigureAwait(false);
            return response.AuthenticationToken;
        }

        private sealed class ActivateTestServer : StandardServer
        {
            public ActivateTestServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public bool ThrowFromAdditionalParameters { get; set; }

            public bool CancelAuthentication { get; set; }

            public CancellationToken? ActivatedCallbackToken { get; set; }

            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new CancellingSessionManager(server, configuration, this);
            }

            protected override AdditionalParametersType ActivateSessionProcessAdditionalParameters(
                ISession session,
                ExtensionObject additionalHeader)
            {
                if (ThrowFromAdditionalParameters)
                {
                    throw new ObjectDisposedException(nameof(Session));
                }
                return base.ActivateSessionProcessAdditionalParameters(session, additionalHeader);
            }
        }

        /// <summary>
        /// Fails authentication with a cancellation, which SessionManager propagates
        /// as an OperationCanceledException rather than a ServiceResultException.
        /// </summary>
        private sealed class CancellingSessionManager : SessionManager
        {
            public CancellingSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ActivateTestServer owner)
                : base(server, configuration)
            {
                m_owner = owner;
            }

            protected override ValueTask<(
                IUserIdentity Identity,
                IUserIdentity EffectiveIdentity,
                ServiceResult Error)> AuthenticateUserIdentityAsync(
                    ISession session,
                    IUserIdentityTokenHandler newIdentity,
                    UserTokenPolicy userTokenPolicy,
                    EndpointDescription endpointDescription,
                    CancellationToken cancellationToken)
            {
                if (m_owner.CancelAuthentication)
                {
                    throw new OperationCanceledException("Injected authentication cancellation.");
                }
                return base.AuthenticateUserIdentityAsync(
                    session, newIdentity, userTokenPolicy, endpointDescription, cancellationToken);
            }

            protected override ValueTask OnSessionActivatedAsync(
                NodeId authenticationToken,
                ISession session,
                ByteString serverNonce,
                UserTokenType clientUserTokenType,
                string clientUserId,
                long activationSequence,
                CancellationToken cancellationToken)
            {
                m_owner.ActivatedCallbackToken = cancellationToken;
                return base.OnSessionActivatedAsync(
                    authenticationToken,
                    session,
                    serverNonce,
                    clientUserTokenType,
                    clientUserId,
                    activationSequence,
                    cancellationToken);
            }

            private readonly ActivateTestServer m_owner;
        }
    }
}
