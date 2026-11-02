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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.LADS.Client;
using Opc.Ua.LADS.Tests.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.LADS.Tests.Integration
{
    /// <summary>
    /// A hosted LADS server together with a managed session and a LADS client
    /// connected to it over opc.tcp.
    /// </summary>
    internal sealed class LadsClientConnection : IAsyncDisposable
    {
        private LadsClientConnection(
            LadsHostedServer server,
            ClientFixture fixture,
            ManagedSession session,
            ITelemetryContext telemetry)
        {
            Server = server;
            m_fixture = fixture;
            Session = session;
            Telemetry = telemetry;
            Client = session.GetLadsClient(telemetry);
        }

        /// <summary>
        /// Gets the hosted server.
        /// </summary>
        public LadsHostedServer Server { get; }

        /// <summary>
        /// Gets the managed session.
        /// </summary>
        public ManagedSession Session { get; }

        /// <summary>
        /// Gets the telemetry context of the client.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the LADS client on the session.
        /// </summary>
        public LadsClient Client { get; }

        /// <summary>
        /// Starts the standalone incubator server and connects to it.
        /// </summary>
        public static async Task<LadsClientConnection> StartIncubatorAsync(string name)
        {
            LadsHostedServer server = await LadsHostedServer.StartIncubatorAsync(name).ConfigureAwait(false);
            return await ConnectAsync(server, name).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts a server composed by the test and connects to it.
        /// </summary>
        public static async Task<LadsClientConnection> StartAsync(
            string name,
            Action<IOpcUaServerBuilder> compose,
            string deviceName = LadsIncubator.DeviceName)
        {
            LadsHostedServer server = await LadsHostedServer.StartAsync(name, compose, deviceName)
                .ConfigureAwait(false);
            return await ConnectAsync(server, name).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the functional unit of the incubator through the client.
        /// </summary>
        public async Task<LadsFunctionalUnit> ChamberAsync()
        {
            LadsDevice device = await Client.GetDeviceAsync(LadsIncubator.DeviceName).ConfigureAwait(false);
            return await device.GetFunctionalUnitAsync(LadsIncubator.UnitName).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Session.CloseAsync().ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The server may already be gone.
            }
            await Session.DisposeAsync().ConfigureAwait(false);
            await m_fixture.DisposeAsync().ConfigureAwait(false);
            await Server.DisposeAsync().ConfigureAwait(false);
        }

        private static async Task<LadsClientConnection> ConnectAsync(LadsHostedServer server, string name)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ClientFixture(telemetry);
            try
            {
                string shortName = name.Length > 24 ? name[..24] : name;
                await fixture.LoadClientConfigurationAsync(
                    Path.Combine(
                        TestContext.CurrentContext.WorkDirectory,
                        "ladsc",
                        Guid.NewGuid().ToString("N")[..8]),
                    shortName).ConfigureAwait(false);
                ApplicationConfiguration configuration = fixture.Config;
                string endpointUrl = server.Server.EndpointAddresses.First().ToString();
                EndpointDescription? endpoint = await CoreClientUtils.SelectEndpointAsync(
                    configuration,
                    endpointUrl,
                    useSecurity: false,
                    discoverTimeout: 15000,
                    telemetry).ConfigureAwait(false);
                Assert.That(endpoint, Is.Not.Null);
                ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                    .UseEndpoint(new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)))
                    .WithSessionName(shortName)
                    .ConnectAsync().ConfigureAwait(false);
                return new LadsClientConnection(server, fixture, session, telemetry);
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private readonly ClientFixture m_fixture;
    }
}
