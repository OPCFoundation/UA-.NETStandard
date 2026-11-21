// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.ISA95.Server.Builders;

namespace Opc.Ua.ISA95.Tests.Common
{
    /// <summary>
    /// Lightweight system context, NodeId factory and root node used to exercise
    /// the ISA-95 common-model builder without a full server.
    /// </summary>
    internal sealed class Isa95CommonTestContext
    {
        public Isa95CommonTestContext()
        {
            NamespaceUris = new NamespaceTable();
            InstanceNamespaceIndex =
                NamespaceUris.GetIndexOrAppend("urn:test:isa95:instance");
            NamespaceUris.GetIndexOrAppend(Namespaces.ISA95);
            Context = new SystemContext(new StubTelemetry())
            {
                NamespaceUris = NamespaceUris,
                NodeIdFactory = new ChildNodeIdFactory()
            };
            Root = new FolderState(null)
            {
                NodeId = new NodeId("Root", InstanceNamespaceIndex),
                BrowseName = new QualifiedName("Root", InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Root"),
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType
            };
        }

        public SystemContext Context { get; }

        public NamespaceTable NamespaceUris { get; }

        public ushort InstanceNamespaceIndex { get; }

        public FolderState Root { get; }

        public int RegisterCount { get; private set; }

        public int RemoveCount { get; private set; }

        public Isa95ModelBuilder CreateBuilder(bool withRemove = false)
        {
            return new Isa95ModelBuilder(
                Context,
                Root,
                InstanceNamespaceIndex,
                RegisterAsync,
                withRemove ? RemoveAsync : null);
        }

        public NodeId Resolve(ExpandedNodeId referenceTypeId)
        {
            return ExpandedNodeId.ToNodeId(referenceTypeId, NamespaceUris);
        }

        public NodeId ExpectedChildId(string name)
        {
            return new NodeId("Root_" + name, InstanceNamespaceIndex);
        }

        private ValueTask RegisterAsync(NodeState node, CancellationToken cancellationToken)
        {
            RegisterCount++;
            return new ValueTask();
        }

        private ValueTask RemoveAsync(NodeState node, CancellationToken cancellationToken)
        {
            RemoveCount++;
            return new ValueTask();
        }

        private sealed class ChildNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                if (!node.NodeId.IsNull)
                {
                    return node.NodeId;
                }
                if (node is BaseInstanceState instance && instance.Parent != null)
                {
                    string name = instance.SymbolicName ?? instance.BrowseName.Name ?? "Node";
                    return new NodeId(
                        instance.Parent.NodeId.IdentifierAsString + "_" + name,
                        instance.Parent.NodeId.NamespaceIndex);
                }
                return node.NodeId;
            }
        }

        private sealed class StubTelemetry : ITelemetryContext
        {
            public Meter CreateMeter()
            {
                return new Meter("Opc.Ua.ISA95.Tests");
            }

            public Meter CreateMeter(System.Reflection.Assembly assembly)
            {
                return CreateMeter();
            }

            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

            public ActivitySource ActivitySource { get; } =
                new ActivitySource("Opc.Ua.ISA95.Tests");

            public ActivitySource GetActivitySource(System.Reflection.Assembly assembly)
            {
                return ActivitySource;
            }
        }
    }
}
