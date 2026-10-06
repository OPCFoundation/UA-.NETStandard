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
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// OPC 10000-12 Annex G.2: a server in the application setup state
    /// reports ServerState NoConfiguration and still serves requests so it can
    /// be provisioned.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class ApplicationSetupServerStateTests
    {
        [Test]
        public async Task ServerInApplicationSetupReportsNoConfigurationAndServesRequestsAsync()
        {
            var fixture = new ServerFixture<ApplicationSetupServer>(
                telemetry => new ApplicationSetupServer(telemetry))
            {
                SecurityNone = true
            };
            await fixture.LoadConfigurationAsync().ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                StandardServer server = fixture.Server;
                Assert.That(server.CurrentInstance.CurrentState, Is.EqualTo(ServerState.NoConfiguration));
                Assert.That(server.CurrentInstance.IsRunning, Is.True);

                Assert.That(await ReadServerStateAsync(server).ConfigureAwait(false),
                    Is.EqualTo(ServerState.NoConfiguration));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerStartsRunningByDefaultAsync()
        {
            var fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true
            };
            await fixture.LoadConfigurationAsync().ConfigureAwait(false);
            await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                Assert.That(fixture.Server.CurrentInstance.CurrentState, Is.EqualTo(ServerState.Running));
                Assert.That(await ReadServerStateAsync(fixture.Server).ConfigureAwait(false),
                    Is.EqualTo(ServerState.Running));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ServerState> ReadServerStateAsync(StandardServer server)
        {
            (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                await server.CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name)
                    .ConfigureAwait(false);
            try
            {
                ReadResponse response = await server.ReadAsync(
                    secureChannelContext,
                    requestHeader,
                    0,
                    TimestampsToReturn.Neither,
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    ],
                    RequestLifetime.None).ConfigureAwait(false);
                DataValue value = response.Results[0];
                Assert.That(StatusCode.IsGood(value.StatusCode), Is.True);
                Assert.That(value.WrappedValue.TryGetValue(out ServerState state), Is.True);
                return state;
            }
            finally
            {
                await server.CloseSessionAsync(secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A server that starts in the application setup state.
        /// </summary>
        public sealed class ApplicationSetupServer : StandardServer
        {
            public ApplicationSetupServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            protected override ServerState StartupServerState => ServerState.NoConfiguration;
        }
    }
}
