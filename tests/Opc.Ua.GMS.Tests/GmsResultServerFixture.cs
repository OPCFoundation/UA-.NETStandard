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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Hosts genuine GMS roots and results on one stock Machinery/DI owner and a real opc.tcp transport.
    /// This fixture deliberately does not claim to implement GMS equipment or production behavior.
    /// </summary>
    internal sealed class GmsResultServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Configures small resource bounds so leaked handles are observable over the transport.
        /// </summary>
        internal GmsResultServerFixture()
        {
            m_factory = new Factory();
            m_fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(m_factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
        }

        /// <summary>
        /// Gets the actual server-owned namespace manager.
        /// </summary>
        internal MachineryNodeManager Manager => m_factory.Manager!;

        /// <summary>
        /// Gets the application-owned instance namespace.
        /// </summary>
        internal const string InstanceNamespace = "urn:opcfoundation:gms:result-tests";

        /// <summary>
        /// Starts the real listener with repository-owned short certificate-store paths.
        /// </summary>
        internal async Task StartAsync()
        {
            await m_fixture.LoadConfigurationAsync(NewPkiPath()).ConfigureAwait(false);
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stages a canonical GMS root through the existing Machinery builder without registering it twice.
        /// </summary>
        internal IMachineBuilder<GMSState> Stage(string name)
        {
            IMachineryBuildContext context = Manager.CreateMachineryBuildContext(Options());
            GMSState state = context.Context.CreateInstanceOfGMSType(
                context.MachinesFolder, new QualifiedName(name, context.InstanceNamespaceIndex));
            return context.AddMachine(state).WithIdentification(state.Identification!, data =>
            {
                data.Manufacturer = new LocalizedText("GMS result tests");
                data.SerialNumber = name;
                data.ProductInstanceUri = "urn:gms:result-tests:" + name;
            });
        }

        /// <summary>
        /// Connects another independent client session using the standard client test framework.
        /// </summary>
        internal async Task<ISession> ConnectAsync(bool managed = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var fixture = new ClientFixture(telemetry);
            m_clients.Add(fixture);
            fixture.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            if (managed)
            {
                fixture.SessionFactory = new ManagedSessionFactory(telemetry)
                    .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);
            }
            await fixture.LoadClientConfigurationAsync(NewPkiPath(), "GmsResultClient").ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync(
                new Uri(m_fixture.Config.ServerConfiguration!.BaseAddresses[0]),
                SecurityPolicies.None).ConfigureAwait(false);
            session.MessageContext.Factory.Builder.AddGmsTypeSystem().Commit();
            m_sessions.Add(session);
            return session;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (ISession session in m_sessions)
            {
                if (session.Connected)
                {
                    await session.CloseAsync(1000, true).ConfigureAwait(false);
                }
                if (session is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    session.Dispose();
                }
            }
            foreach (ClientFixture client in m_clients)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            await m_fixture.StopAsync().ConfigureAwait(false);
            foreach (string path in m_pkiPaths)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        /// <summary>
        /// Keeps test PKI within the checkout even when build artifacts are external.
        /// </summary>
        private string NewPkiPath()
        {
            string root = Environment.GetEnvironmentVariable("OPCUA_GMS_TEST_ROOT") ?? Environment.CurrentDirectory;
            string path = Path.GetFullPath(Path.Combine(root, "artifacts", "gms-pki", Guid.NewGuid().ToString("N")[..8]));
            m_pkiPaths.Add(path);
            return path;
        }

        /// <summary>
        /// Selects only the machinery blocks needed for the results slice.
        /// </summary>
        private static MachineryServerOptions Options()
        {
            return new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks | MachineryParts.Result,
                InstanceNamespaceUri = InstanceNamespace,
                MaxConcurrentResultTransfers = 1,
                MaxPinnedResultHandles = 1
            };
        }

        private readonly Factory m_factory;
        private readonly ServerFixture<StandardServer> m_fixture;
        private readonly List<ClientFixture> m_clients = [];
        private readonly List<ISession> m_sessions = [];
        private readonly List<string> m_pkiPaths = [];

        /// <summary>
        /// Installs exactly one stock namespace owner and the canonical GMS model provider.
        /// </summary>
        private sealed class Factory : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => new GmsModelProvider().NamespaceUris;

            /// <summary>
            /// Gets the stock manager created by the server.
            /// </summary>
            internal MachineryNodeManager? Manager { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Manager = new MachineryNodeManager(server, configuration, [new GmsModelProvider()], Options());
                return new ValueTask<IAsyncNodeManager>(Manager);
            }
        }
    }
}
