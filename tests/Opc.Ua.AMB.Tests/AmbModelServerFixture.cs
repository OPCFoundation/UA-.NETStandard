// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.AMB.Tests
{
    /// <summary>
    /// Starts a server whose only addition is a node manager that loads the
    /// generated AMB model, so the generated nodes can be checked in a real
    /// address space - with the references into the base namespace resolved
    /// by the master node manager - without the AMB server package.
    /// </summary>
    internal sealed class AmbModelServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public IServerInternal Server { get; private set; } = null!;

        public ModelOnlyNodeManager Manager { get; private set; } = null!;

        public async Task StartAsync()
        {
            var factory = new ModelOnlyNodeManagerFactory();
            m_fixture = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(factory);
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };

            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Server = server.CurrentInstance;
            Manager = factory.Created ??
                throw new InvalidOperationException("The AMB model node manager was not created.");
        }

        public NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris);
        }

        public async Task<IReadOnlyList<ReferenceDescription>> BrowseAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction = BrowseDirection.Forward)
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];

            using var context = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.Browse,
                RequestLifetime.None,
                new UserIdentity());

            (ArrayOf<BrowseResult> results, _) = await Server.NodeManager.BrowseAsync(
                context,
                new ViewDescription(),
                0,
                nodesToBrowse,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(results[0].StatusCode), Is.True, results[0].StatusCode.ToString());
            var references = new List<ReferenceDescription>();
            foreach (ReferenceDescription reference in results[0].References)
            {
                references.Add(reference);
            }
            return references;
        }

        public async ValueTask DisposeAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Loads nothing but <c>AddOpcUaAMB</c>.
        /// </summary>
        internal sealed class ModelOnlyNodeManager : AsyncCustomNodeManager
        {
            public ModelOnlyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, Namespaces.AMB)
            {
            }

            public NodeState? Find(ExpandedNodeId nodeId)
            {
                return FindPredefinedNode<NodeState>(
                    ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris));
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaAMB(context));
            }
        }

        private sealed class ModelOnlyNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public ModelOnlyNodeManager? Created { get; private set; }

            public ArrayOf<string> NamespacesUris => [Namespaces.AMB];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                Created = new ModelOnlyNodeManager(server, configuration);
                return new ValueTask<IAsyncNodeManager>(Created);
            }
        }
    }
}
