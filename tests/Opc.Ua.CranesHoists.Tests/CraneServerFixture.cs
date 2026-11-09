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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.CranesHoists.Server;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Hosts the real compiled crane model on a single stock namespace owner.
    /// </summary>
    internal sealed class CraneServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates a fixture using either existing stock owner.
        /// </summary>
        internal CraneServerFixture(
            bool roboticsOwner = false,
            bool countRegistrations = false,
            bool reverseNamespaces = false)
        {
            m_factory = new Factory(roboticsOwner, countRegistrations, reverseNamespaces);
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
        /// Gets the namespace shared by both build contexts.
        /// </summary>
        internal const string InstanceNamespace = "urn:opcfoundation:cranes:tests:instances";

        /// <summary>
        /// Gets the actual server-owned manager.
        /// </summary>
        internal DiNodeManager Manager => m_factory.Manager!;

        /// <summary>
        /// Gets the active server.
        /// </summary>
        internal StandardServer Server => m_fixture.Server;

        /// <summary>
        /// Gets its endpoint configuration.
        /// </summary>
        internal ApplicationConfiguration Configuration => m_fixture.Config;

        /// <summary>
        /// Gets outstanding instance allocations observed through the supported factory seam.
        /// </summary>
        internal int ReservedCount => m_trackingFactory?.Count ?? 0;

        /// <summary>
        /// Describes outstanding allocation identities for failed lifetime assertions.
        /// </summary>
        internal string ReservedNames => m_trackingFactory?.Names ?? string.Empty;

        /// <summary>
        /// Gets the number of actual indexing callbacks for a node on the counting owner.
        /// </summary>
        internal int RegistrationCount(NodeId nodeId)
        {
            return Manager is CountingManager counting && counting.Counts.TryGetValue(nodeId, out int count)
                ? count
                : 0;
        }

        /// <summary>
        /// Starts the real opc.tcp listener with test-owned certificate stores.
        /// </summary>
        internal async Task StartAsync()
        {
            await m_fixture.LoadConfigurationAsync(CraneTestPaths.NewPkiPath()).ConfigureAwait(false);
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_trackingFactory = new TrackingFactory(
                Manager.SystemContext.NodeIdFactory!,
                (ICompanionNodeIdReservationOwner)Manager);
            Manager.SystemContext.NodeIdFactory = m_trackingFactory;
        }

        /// <summary>
        /// Creates a Machinery context without registering another manager.
        /// </summary>
        internal IMachineryBuildContext MachineryContext()
        {
            return Manager.CreateMachineryBuildContext(new MachineryServerOptions
            {
                Parts = MachineryParts.BuildingBlocks,
                InstanceNamespaceUri = InstanceNamespace
            });
        }

        /// <summary>
        /// Creates a Robotics context on the same manager and namespace.
        /// </summary>
        internal IRoboticsBuildContext RoboticsContext()
        {
            return Manager.CreateRoboticsBuildContext(new RoboticsServerOptions
            {
                InstanceNamespaceUri = InstanceNamespace
            });
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Captures the stock manager actually installed in the server.
        /// </summary>
        private sealed class Factory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Chooses an existing namespace owner.
            /// </summary>
            internal Factory(bool roboticsOwner, bool countRegistrations, bool reverseNamespaces)
            {
                m_roboticsOwner = roboticsOwner;
                m_countRegistrations = countRegistrations;
                m_reverseNamespaces = reverseNamespaces;
            }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris
            {
                get
                {
                    var uris = new List<string>(new CranesHoistsModelProvider().NamespaceUris.ToArray()!)
                    {
                        InstanceNamespace,
                        "urn:crane:tests:vendor-before-models"
                    };
                    if (m_reverseNamespaces)
                    {
                        uris.Reverse();
                    }
                    return uris.ToArrayOf();
                }
            }

            /// <summary>
            /// Gets the server-owned manager.
            /// </summary>
            internal DiNodeManager? Manager { get; private set; }

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = new CranesHoistsModelProvider();
                Manager = m_countRegistrations
                    ? new CountingManager(server, configuration)
                    : m_roboticsOwner
                    ? new RoboticsNodeManager(server, configuration, [provider], new RoboticsServerOptions
                    {
                        InstanceNamespaceUri = InstanceNamespace
                    })
                    : new MachineryNodeManager(server, configuration, [provider], new MachineryServerOptions
                    {
                        Parts = MachineryParts.BuildingBlocks,
                        InstanceNamespaceUri = InstanceNamespace
                    });
                return new ValueTask<IAsyncNodeManager>(Manager);
            }

            private readonly bool m_roboticsOwner;
            private readonly bool m_countRegistrations;
            private readonly bool m_reverseNamespaces;
        }

        /// <summary>
        /// Observes, but does not replace, the stock allocator's reservation ownership.
        /// </summary>
        private sealed class TrackingFactory : ICompanionNodeIdFactory, ICompanionNodeIdReservationOwner
        {
            /// <summary>
            /// Wraps the stock allocator and its release capability.
            /// </summary>
            internal TrackingFactory(INodeIdFactory allocator, ICompanionNodeIdReservationOwner owner)
            {
                m_allocator = allocator;
                m_owner = owner;
            }

            /// <summary>
            /// Gets outstanding staged nodes.
            /// </summary>
            internal int Count => m_reserved.Count;

            /// <summary>
            /// Gets outstanding names for diagnostics.
            /// </summary>
            internal string Names => string.Join(", ", m_reserved.Select(node => node.BrowseName.ToString()));

            /// <inheritdoc/>
            public NodeId New(ISystemContext context, NodeState node)
            {
                if (node.NodeId.IsNull && !IsTransientEvent(node))
                {
                    m_reserved.Add(node);
                }
                return m_allocator.New(context, node);
            }

            /// <inheritdoc/>
            public void ReleaseNodeIdReservation(NodeState node)
            {
                m_owner.ReleaseNodeIdReservation(node);
                m_reserved.Remove(node);
            }

            /// <summary>
            /// Counts staged graph allocations, not unrelated server model-change event fields.
            /// </summary>
            private static bool IsTransientEvent(NodeState node)
            {
                NodeState? current = node;
                while (current != null)
                {
                    if (current is BaseEventState)
                    {
                        return true;
                    }
                    current = (current as BaseInstanceState)?.Parent;
                }
                return false;
            }

            private readonly INodeIdFactory m_allocator;
            private readonly ICompanionNodeIdReservationOwner m_owner;
            private readonly HashSet<NodeState> m_reserved = [];
        }

        /// <summary>
        /// Counts genuine registration callbacks without adding production-only instrumentation.
        /// </summary>
        private sealed class CountingManager : DiNodeManager,
            ICompanionNodeIdFactory,
            ICompanionNodeIdReservationOwner
        {
            /// <summary>
            /// Uses the same dependency closure as the stock owners.
            /// </summary>
            internal CountingManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration,
                    Opc.Ua.IA.Namespaces.IA,
                    Machinery.Namespaces.Machinery,
                    Robotics.Namespaces.Robotics,
                    CranesHoistsModel.NamespaceUri,
                    InstanceNamespace)
            {
                SystemContext.NodeIdFactory = this;
            }

            /// <summary>
            /// Gets callback counts for actual instance indexing.
            /// </summary>
            internal Dictionary<NodeId, int> Counts { get; } = [];

            /// <inheritdoc/>
            public override NodeId New(ISystemContext context, NodeState node)
            {
                return node.NodeId.IsNull
                    ? new NodeId((uint)Interlocked.Increment(ref m_nextId),
                        (ushort)context.NamespaceUris.GetIndex(InstanceNamespace))
                    : node.NodeId;
            }

            /// <inheritdoc/>
            public void ReleaseNodeIdReservation(NodeState node)
            {
                _ = node;
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nodes = new NodeStateCollection();
                new CranesHoistsModelProvider().AddPredefinedNodes(nodes, context);
                return new ValueTask<NodeStateCollection>(nodes);
            }

            /// <inheritdoc/>
            protected override ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
                ISystemContext context,
                NodeState predefinedNode,
                CancellationToken cancellationToken = default)
            {
                if (predefinedNode.NodeId.NamespaceIndex == context.NamespaceUris.GetIndex(InstanceNamespace))
                {
                    Counts.TryGetValue(predefinedNode.NodeId, out int count);
                    Counts[predefinedNode.NodeId] = count + 1;
                }
                return base.AddBehaviourToPredefinedNodeAsync(context, predefinedNode, cancellationToken);
            }

            private int m_nextId;
        }

        private readonly Factory m_factory;
        private readonly ServerFixture<StandardServer> m_fixture;
        private TrackingFactory? m_trackingFactory;
    }
}
