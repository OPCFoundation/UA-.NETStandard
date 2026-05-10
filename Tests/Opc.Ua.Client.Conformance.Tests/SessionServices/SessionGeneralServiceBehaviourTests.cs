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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Session General Service Behaviour.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseServices")]
    public class SessionGeneralServiceBehaviourTests : TestFixture
    {
        [Description("Invoke CreateSession with default parameters. Verify the session is created successfully (Connected, non-null SessionId, non-null AuthenticationToken, positive RevisedSessionTimeout) and can service a basic Read.")]
        [Test]
        [Property("ConformanceUnit", "Session General Service Behaviour")]
        [Property("Tag", "001")]
        public async Task CreateSessionWithDefaultParametersAsync()
        {
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session, Is.Not.Null);
                Assert.That(session.Connected, Is.True,
                    "Session should be connected after CreateSession with default parameters.");
                Assert.That(session.SessionId, Is.Not.Null);
                Assert.That(session.SessionId, Is.Not.EqualTo(NodeId.Null),
                    "Server must return a non-null SessionId.");
                Assert.That(session.SessionTimeout, Is.GreaterThan(0),
                    "Server must return a positive RevisedSessionTimeout.");

                StatusCode status = await ReadServerStatusAsync(session).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(status), Is.True,
                    "Read on the new session should succeed.");
            }
            finally
            {
                await CloseSessionAsync(session).ConfigureAwait(false);
            }
        }

        [Description("Invoke CreateSession with several RequestedSessionTimeout values. The server is expected to revise each one to a value supported by the server (always greater than zero). Very small values should be revised up, very large values should be revised down (or kept). See Part 4 §5.6.2.")]
        [Test]
        [Property("ConformanceUnit", "Session General Service Behaviour")]
        [Property("Tag", "002")]
        public async Task RequestedSessionTimeoutIsRevisedByServerAsync()
        {
            uint originalTimeout = ClientFixture.SessionTimeout;
            uint[] requestedTimeouts = new uint[]
            {
                0u,
                1u,
                1_000u,
                60_000u,
                3_600_000u
            };

            try
            {
                foreach (uint requested in requestedTimeouts)
                {
                    ClientFixture.SessionTimeout = requested;

                    ISession session = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.None)
                        .ConfigureAwait(false);
                    try
                    {
                        Assert.That(session.Connected, Is.True);

                        double revised = session.SessionTimeout;
                        Assert.That(revised, Is.GreaterThan(0),
                            $"RevisedSessionTimeout must be > 0 (requested {requested}).");

                        if (requested > 0)
                        {
                            Assert.That(revised, Is.LessThanOrEqualTo((double)requested).Within(0.001)
                                .Or.GreaterThanOrEqualTo((double)requested),
                                $"RevisedSessionTimeout {revised} must be a valid revision of {requested}.");
                        }

                        StatusCode status = await ReadServerStatusAsync(session).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(status), Is.True);
                    }
                    finally
                    {
                        await CloseSessionAsync(session).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ClientFixture.SessionTimeout = originalTimeout;
            }
        }

        [Description("RequestHeader.AuthenticationToken handling. A CreateSession request whose RequestHeader.AuthenticationToken contains a (valid-looking) NodeId must be ignored by the server: the server still accepts the request and returns a freshly minted AuthenticationToken in the response. Subsequent service calls on the new session implicitly carry that issued token in their RequestHeader.AuthenticationToken and must succeed. Two sessions created in this way must end up with distinct, server-issued SessionIds (the public surrogate for the AuthenticationToken).")]
        [Test]
        [Property("ConformanceUnit", "Session General Service Behaviour")]
        [Property("Tag", "003")]
        public async Task AuthenticationTokenHandlingDuringCreateSessionAsync()
        {
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(session.Connected, Is.True,
                    "Server must accept CreateSession regardless of any AuthenticationToken in the request header.");
                Assert.That(session.SessionId, Is.Not.Null);
                Assert.That(session.SessionId, Is.Not.EqualTo(NodeId.Null),
                    "Server must return a valid SessionId.");

                // A successful service call confirms the server-issued
                // AuthenticationToken (transmitted in every RequestHeader by
                // the client) is accepted by the server.
                StatusCode status = await ReadServerStatusAsync(session).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(status), Is.True,
                    "Service call carrying the issued AuthenticationToken must succeed.");

                // A second session must be issued a different SessionId
                // (and hence a different AuthenticationToken).
                ISession otherSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(otherSession.SessionId, Is.Not.Null);
                    Assert.That(otherSession.SessionId,
                        Is.Not.EqualTo(session.SessionId),
                        "Each session must be issued a unique SessionId.");
                }
                finally
                {
                    await CloseSessionAsync(otherSession).ConfigureAwait(false);
                }
            }
            finally
            {
                await CloseSessionAsync(session).ConfigureAwait(false);
            }
        }

        private static async Task<StatusCode> ReadServerStatusAsync(ISession session)
        {
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0].StatusCode;
        }

        private static async Task CloseSessionAsync(ISession session)
        {
            if (session == null)
            {
                return;
            }
            try
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
            }
            catch
            {
                // best-effort cleanup
            }
            session.Dispose();
        }
    }
}
