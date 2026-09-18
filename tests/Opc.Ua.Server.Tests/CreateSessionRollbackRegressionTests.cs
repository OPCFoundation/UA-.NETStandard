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

                Exception error = Assert.CatchAsync<Exception>(async () =>
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
                return base.CreateSessionProcessAdditionalParameters(session, additionalHeader);
            }

            private readonly RequestLifetime m_lifetime;
        }
    }
}
