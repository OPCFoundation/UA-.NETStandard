/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Hosts common generated capabilities on a real single-owner UA-TCP server.
    /// </summary>
    internal sealed class CommonRuntimeServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates an isolated server fixture without starting a listener.
        /// </summary>
        internal CommonRuntimeServerFixture()
        {
            m_root = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "TestResults", "CommonRuntime-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Gets the real Machinery/DI owner.
        /// </summary>
        internal MachineryNodeManager Manager => m_server!.Server.Manager;

        /// <summary>
        /// Gets the real session used by generated client proxies.
        /// </summary>
        internal Opc.Ua.Client.ISession Session { get; private set; } = null!;

        /// <summary>
        /// Gets the parent's actual application-namespace test root.
        /// </summary>
        internal BaseObjectState Root { get; private set; } = null!;

        /// <summary>
        /// Starts the host, configures native capabilities, then opens a real session.
        /// </summary>
        internal async Task StartAsync(Func<CommonRuntimeServerFixture, ValueTask> configure)
        {
            m_server = new ServerFixture<CommonRuntimeTestServer>(telemetry => new CommonRuntimeTestServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.StartAsync(Path.Combine(m_root, "server")).ConfigureAwait(false);
            NodeState machines = Manager.FindPredefinedNode(
                ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, Manager.SystemContext.NamespaceUris)) ??
                throw new InvalidOperationException("The Machinery Machines folder is missing.");
            Root = Manager.SystemContext.CreateInstanceOfBaseObjectType(machines,
                new QualifiedName("CommonCapabilities", Manager.MachineryInstanceNamespaceIndex));
            Root.NodeId = new NodeId("CommonCapabilities", Manager.MachineryInstanceNamespaceIndex);
            machines.AddChild(Root);
            await Manager.AddPredefinedNodeAsync(Root).ConfigureAwait(false);
            await configure(this).ConfigureAwait(false);
            m_client = new ClientFixture(m_server.Server.CurrentInstance.Telemetry);
            m_client.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_root, "client"), "CommonRuntimeClient")
                .ConfigureAwait(false);
            Session = await m_client.ConnectAsync(
                new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]), SecurityPolicies.None)
                .ConfigureAwait(false);
            Session.MessageContext.Factory.Builder.AddOpcUaPlasticsRubber().Commit();
        }

        /// <summary>
        /// Attaches a configured native capability under the test root and indexes its real children.
        /// </summary>
        internal async ValueTask RegisterAsync(BaseObjectState node, IAsyncDisposable binding)
        {
            Manager.RegisterMachineryResource(binding);
            Root.AddChild(node);
            await Manager.AddPredefinedNodeAsync(node).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Session != null)
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                }
                if (m_client != null)
                {
                    await m_client.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (m_server != null)
                {
                    await m_server.StopAsync().ConfigureAwait(false);
                }
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, true);
                }
            }
        }

        /// <summary>
        /// The exact fixture-owned temporary directory.
        /// </summary>
        private readonly string m_root;

        /// <summary>
        /// The server host and listener.
        /// </summary>
        private ServerFixture<CommonRuntimeTestServer>? m_server;

        /// <summary>
        /// The real client-session factory.
        /// </summary>
        private ClientFixture? m_client;
    }

    /// <summary>
    /// Uses one existing Machinery manager with the common model provider in real service dispatch.
    /// </summary>
    internal sealed class CommonRuntimeTestServer : StandardServer
    {
        /// <summary>
        /// Creates the server with the fixture's telemetry.
        /// </summary>
        internal CommonRuntimeTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new MachineryNodeManagerFactory(
                [new MachineryModelProvider(MachineryParts.BuildingBlocks), new PlasticsRubberModelProvider()],
                new MachineryServerOptions
                {
                    Parts = MachineryParts.BuildingBlocks,
                    InstanceNamespaceUri = "urn:plastics-runtime-tests:instances"
                }));
        }

        /// <summary>
        /// Gets the actual model and instance owner.
        /// </summary>
        internal MachineryNodeManager Manager { get; private set; } = null!;

        /// <inheritdoc/>
        protected override async ValueTask OnNodeManagerStartedAsync(
            IServerInternal server,
            CancellationToken cancellationToken = default)
        {
            Manager = server.NodeManager.AsyncNodeManagers.OfType<MachineryNodeManager>().Single();
            await base.OnNodeManagerStartedAsync(server, cancellationToken).ConfigureAwait(false);
        }
    }
}
