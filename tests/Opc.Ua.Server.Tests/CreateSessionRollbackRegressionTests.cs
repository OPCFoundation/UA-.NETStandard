// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that a cancelled CreateSession cannot leave a registered session behind.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public sealed class CreateSessionRollbackRegressionTests
    {
        [Test]
        public async Task CancelledCreateSessionRollsBackRegisteredSessionAsync()
        {
            using var lifetime = new RequestLifetime();
            var fixture = new ServerFixture<CancelAfterCreationServer>(
                telemetry => new CancelAfterCreationServer(telemetry, lifetime))
            {
                SecurityNone = true
            };
            try
            {
                CancelAfterCreationServer server = await fixture.StartAsync().ConfigureAwait(false);
                EndpointDescription endpoint = server.GetEndpoints().Find(
                    candidate => candidate.SecurityPolicyUri == SecurityPolicies.None);
                Assert.That(endpoint, Is.Not.Null);
                var channel = new SecureChannelContext(
                    "cancelled-create", endpoint, RequestEncoding.Binary, null, null);

                Exception error = await Assert.CatchAsync<Exception>(async () =>
                    await server.CreateSessionAsync(
                        channel, new RequestHeader(), null, null, null, "cancelled-create",
                        default, default, 60000, 0, lifetime).ConfigureAwait(false));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(server.CreatedSessionId.IsNull, Is.False);
                    Assert.That(server.CurrentInstance.SessionManager.GetSessions(), Is.Empty);
                    Assert.That(error, Is.InstanceOf<ServiceResultException>());
                    server.CurrentInstance.UpdateServerDiagnostics(diagnostics =>
                    {
                        Assert.That(diagnostics.CurrentSessionCount, Is.Zero);
                        // Part 5 12.9: a rolled back session was never established.
                        Assert.That(diagnostics.CumulatedSessionCount, Is.Zero);
                        Assert.That(diagnostics.RejectedSessionCount, Is.EqualTo(1));
                        Assert.That(diagnostics.RejectedRequestsCount, Is.EqualTo(1));
                    });
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class CancelAfterCreationServer : StandardServer
        {
            public CancelAfterCreationServer(ITelemetryContext telemetry, RequestLifetime lifetime)
                : base(telemetry)
            {
                m_lifetime = lifetime;
            }

            public NodeId CreatedSessionId { get; private set; }

            protected override AdditionalParametersType CreateSessionProcessAdditionalParameters(
                ISession session,
                ExtensionObject additionalHeader)
            {
                CreatedSessionId = session.Id;
                m_lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient);
                return base.CreateSessionProcessAdditionalParameters(session, additionalHeader)!;
            }

            private readonly RequestLifetime m_lifetime;
        }
    }
}
