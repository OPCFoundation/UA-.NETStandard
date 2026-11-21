// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Builds a mocked <see cref="IServerInternal"/> backed by a real
    /// <see cref="DiagnosticsNodeManager"/> address space so startup tasks that touch live nodes
    /// (e.g. <c>Server.ServiceLevel</c> or <c>Server.ServerRedundancy</c>) can be exercised
    /// deterministically without a hosted server.
    /// </summary>
    internal sealed class DiagnosticsServerHarness : IDisposable
    {
        private DiagnosticsServerHarness(Mock<IServerInternal> server, DiagnosticsNodeManager manager)
        {
            Server = server;
            Manager = manager;
        }

        public Mock<IServerInternal> Server { get; }

        public DiagnosticsNodeManager Manager { get; }

        public ServerObjectState ServerObject => Manager.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);

        public static async Task<DiagnosticsServerHarness> CreateAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://opcfoundation.org/UA/");
            var serverUris = new StringTable();
            var typeTree = new TypeTable(namespaceUris);
            var messageContext = new ServiceMessageContext(telemetry, EncodeableFactory.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };

            var server = new Mock<IServerInternal>();
            var coreNodeManager = new Mock<ICoreNodeManager>();
            coreNodeManager.Setup(m => m.ImportNodesAsync(
                It.IsAny<ISystemContext>(),
                It.IsAny<IList<NodeState>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();

            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(typeTree);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.CoreNodeManager).Returns(coreNodeManager.Object);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.ConfigurationNodeManager).Returns(configurationNodeManager.Object);

            var context = new ServerSystemContext(server.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(context);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            var manager = new DiagnosticsNodeManager(server.Object, configuration, NullLogger.Instance);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ServerObjectState serverObject = manager.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            server.Setup(s => s.ServerObject).Returns(serverObject);
            server.Setup(s => s.DiagnosticsNodeManager).Returns(manager);
            server.Setup(s => s.FindNodeManagers<IPredefinedNodeSubtypeReplacer>()).Returns([manager]);
            server.Setup(s => s.FindNodeManagers<IConfigurationNodeManager>())
                .Returns([configurationNodeManager.Object]);
            server.Setup(s => s.FindPredefinedNode<RequestServerStateChangeMethodState>(
                It.IsAny<NodeId>()))
                .Returns((NodeId nodeId) => manager.FindPredefinedNode<RequestServerStateChangeMethodState>(nodeId));
            return new DiagnosticsServerHarness(server, manager);
        }

        public void Dispose()
        {
            Manager.Dispose();
        }
    }
}
