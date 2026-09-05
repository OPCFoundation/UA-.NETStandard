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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    internal sealed class NodeBehaviorTestSource :
        INodeSource,
        INodeBehaviorFactoryProvider
    {
        public const string NamespaceUri =
            "urn:opcfoundation.org:Tests:NodeBehavior";
        public const string BaseTypeIdentifier = "BehaviorBaseType";
        public const string DerivedTypeIdentifier = "BehaviorDerivedType";

        public NodeBehaviorTestSource(
            NodeBehaviorTestRecorder recorder,
            bool includeChild = true,
            bool includeSibling = true,
            Func<string, string, Exception> createFailure = null,
            Func<string, string, NodeBehaviorTestLeaseOptions> leaseOptions = null)
        {
            Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            m_includeChild = includeChild;
            m_includeSibling = includeSibling;
            m_factories =
            [
                new NodeBehaviorTestFactory(
                    "base",
                    new ExpandedNodeId(BaseTypeIdentifier, NamespaceUri),
                    recorder,
                    createFailure,
                    leaseOptions),
                new NodeBehaviorTestFactory(
                    "derived",
                    new ExpandedNodeId(DerivedTypeIdentifier, NamespaceUri),
                    recorder,
                    createFailure,
                    leaseOptions)
            ];
        }

        public ArrayOf<string> NamespaceUris => [NamespaceUri];

        public NodeBehaviorTestRecorder Recorder { get; }

        public NodeId BaseTypeId { get; private set; }

        public NodeId DerivedTypeId { get; private set; }

        public NodeId ParentId { get; private set; }

        public NodeId ChildId { get; private set; }

        public NodeId SiblingId { get; private set; }

        public NodeState Parent { get; private set; }

        public NodeState Child { get; private set; }

        public NodeState Sibling { get; private set; }

        public ValueTask BuildAsync(
            INodeGraphBuilder builder,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int resolvedIndex = builder.Context.NamespaceUris.GetIndex(NamespaceUri);
            if (resolvedIndex <= 0 || resolvedIndex > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Namespace '{NamespaceUri}' was not assigned a valid index.");
            }
            var namespaceIndex = (ushort)resolvedIndex;

            BaseTypeId = new NodeId(BaseTypeIdentifier, namespaceIndex);
            DerivedTypeId = new NodeId(DerivedTypeIdentifier, namespaceIndex);
            var baseType = new BaseObjectTypeState
            {
                NodeId = BaseTypeId,
                BrowseName = new QualifiedName(BaseTypeIdentifier, namespaceIndex),
                DisplayName = new LocalizedText(BaseTypeIdentifier),
                SuperTypeId = ObjectTypeIds.BaseObjectType
            };
            var derivedType = new BaseObjectTypeState
            {
                NodeId = DerivedTypeId,
                BrowseName = new QualifiedName(DerivedTypeIdentifier, namespaceIndex),
                DisplayName = new LocalizedText(DerivedTypeIdentifier),
                SuperTypeId = BaseTypeId
            };

            ParentId = new NodeId("Parent", namespaceIndex);
            var parent = new BaseObjectState(null)
            {
                NodeId = ParentId,
                BrowseName = new QualifiedName("Parent", namespaceIndex),
                DisplayName = new LocalizedText("Parent"),
                TypeDefinitionId = DerivedTypeId
            };
            Parent = parent;

            if (m_includeChild)
            {
                ChildId = new NodeId("Child", namespaceIndex);
                var child = new BaseObjectState(parent)
                {
                    NodeId = ChildId,
                    BrowseName = new QualifiedName("Child", namespaceIndex),
                    DisplayName = new LocalizedText("Child"),
                    TypeDefinitionId = DerivedTypeId
                };
                parent.AddChild(child);
                Child = child;
            }

            if (m_includeSibling)
            {
                SiblingId = new NodeId("Sibling", namespaceIndex);
                var sibling = new BaseObjectState(parent)
                {
                    NodeId = SiblingId,
                    BrowseName = new QualifiedName("Sibling", namespaceIndex),
                    DisplayName = new LocalizedText("Sibling"),
                    TypeDefinitionId = ObjectTypeIds.BaseObjectType
                };
                parent.AddChild(sibling);
                Sibling = sibling;
            }

            builder.Add(baseType);
            builder.Add(derivedType);
            builder.Add(parent);
            return default;
        }

        public ArrayOf<INodeBehaviorFactory> GetNodeBehaviorFactories()
        {
            return m_factories;
        }

        private readonly bool m_includeChild;
        private readonly bool m_includeSibling;
        private readonly ArrayOf<INodeBehaviorFactory> m_factories;
    }

    internal sealed class ImportedNodeBehaviorTestSource :
        INodeSource,
        INodeBehaviorFactoryProvider,
        INodeSetImportFactoryProvider
    {
        public const string NamespaceUri =
            "urn:opcfoundation.org:Tests:ImportedNodeBehavior";

        public ImportedNodeBehaviorTestSource(
            NodeBehaviorTestRecorder recorder,
            Func<string, string, NodeBehaviorTestLeaseOptions> leaseOptions = null)
        {
            Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            m_behaviorFactories =
            [
                new NodeBehaviorTestFactory(
                    "imported",
                    new ExpandedNodeId(100u, NamespaceUri),
                    recorder,
                    createFailure: null,
                    leaseOptions)
            ];
        }

        public ArrayOf<string> NamespaceUris => [NamespaceUri];

        public NodeBehaviorTestRecorder Recorder { get; }

        public NodeId NodeId { get; private set; }

        public ImportedBehaviorObjectState Node { get; private set; }

        public ValueTask BuildAsync(
            INodeGraphBuilder builder,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Import(ReadNodeSet());
            var namespaceIndex =
                (ushort)builder.Context.NamespaceUris.GetIndex(NamespaceUri);
            NodeId = new NodeId(200u, namespaceIndex);
            Node = builder.Node<ImportedBehaviorObjectState>(NodeId).Node;
            return default;
        }

        public ArrayOf<INodeBehaviorFactory> GetNodeBehaviorFactories()
        {
            return m_behaviorFactories;
        }

        public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
        {
            return
            [
                new ImportedBehaviorNodeFactory()
            ];
        }

        private static UANodeSet ReadNodeSet()
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                $"    <Uri>{NamespaceUri}</Uri>\r\n" +
                "  </NamespaceUris>\r\n" +
                "  <UAObjectType NodeId=\"ns=1;i=100\" BrowseName=\"1:ImportedBehaviorType\">\r\n" +
                "    <DisplayName>ImportedBehaviorType</DisplayName>\r\n" +
                "    <References>\r\n" +
                "      <Reference ReferenceType=\"i=45\" IsForward=\"false\">i=58</Reference>\r\n" +
                "    </References>\r\n" +
                "  </UAObjectType>\r\n" +
                "  <UAObject NodeId=\"ns=1;i=200\" BrowseName=\"1:ImportedBehaviorObject\">\r\n" +
                "    <DisplayName>ImportedBehaviorObject</DisplayName>\r\n" +
                "    <References>\r\n" +
                "      <Reference ReferenceType=\"i=40\">ns=1;i=100</Reference>\r\n" +
                "      <Reference ReferenceType=\"i=35\" IsForward=\"false\">i=85</Reference>\r\n" +
                "    </References>\r\n" +
                "  </UAObject>\r\n" +
                "</UANodeSet>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return UANodeSet.Read(stream);
        }

        private readonly ArrayOf<INodeBehaviorFactory> m_behaviorFactories;

        private sealed class ImportedBehaviorNodeFactory : INodeSetImportFactory
        {
            public NodeClass NodeClass => NodeClass.Object;

            public NodeSetImportDiscriminator Discriminator =>
                NodeSetImportDiscriminator.TypeDefinition;

            public ExpandedNodeId DiscriminatorId =>
                new(100u, NamespaceUri);

            public NodeState CreateEmptyState()
            {
                return new ImportedBehaviorObjectState(null);
            }
        }
    }

    internal sealed class ImportedBehaviorObjectState : BaseObjectState
    {
        public ImportedBehaviorObjectState(NodeState parent)
            : base(parent)
        {
        }
    }

    internal sealed class NodeBehaviorTestRecorder
    {
        public void Record(string value)
        {
            lock (m_lock)
            {
                m_events.Add(value);
            }
        }

        public void AddContext(NodeBehaviorContext context)
        {
            lock (m_lock)
            {
                m_contexts.Add(context);
            }
        }

        public void AddLease(NodeBehaviorTestLease lease)
        {
            lock (m_lock)
            {
                m_leases.Add(lease);
            }
        }

        public string[] GetEvents()
        {
            lock (m_lock)
            {
                return [.. m_events];
            }
        }

        public NodeBehaviorContext[] GetContexts()
        {
            lock (m_lock)
            {
                return [.. m_contexts];
            }
        }

        public NodeBehaviorTestLease[] GetLeases()
        {
            lock (m_lock)
            {
                return [.. m_leases];
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<string> m_events = [];
        private readonly List<NodeBehaviorContext> m_contexts = [];
        private readonly List<NodeBehaviorTestLease> m_leases = [];
    }

    internal sealed class NodeBehaviorTestFactory : INodeBehaviorFactory
    {
        public NodeBehaviorTestFactory(
            string name,
            ExpandedNodeId typeDefinitionId,
            NodeBehaviorTestRecorder recorder,
            Func<string, string, Exception> createFailure,
            Func<string, string, NodeBehaviorTestLeaseOptions> leaseOptions)
        {
            Name = name;
            TypeDefinitionId = typeDefinitionId;
            m_recorder = recorder;
            m_createFailure = createFailure;
            m_leaseOptions = leaseOptions;
        }

        public string Name { get; }

        public ExpandedNodeId TypeDefinitionId { get; }

        public ValueTask<INodeBehaviorLease> CreateAsync(
            NodeBehaviorContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string nodeName = context.Node.BrowseName.Name ??
                context.Node.NodeId.ToString();
            m_recorder.Record($"create:{nodeName}:{Name}");
            m_recorder.AddContext(context);
            Exception failure = m_createFailure?.Invoke(nodeName, Name);
            if (failure is not null)
            {
                throw failure;
            }

            var lease = new NodeBehaviorTestLease(
                nodeName,
                Name,
                context,
                m_recorder,
                m_leaseOptions?.Invoke(nodeName, Name));
            m_recorder.AddLease(lease);
            return new ValueTask<INodeBehaviorLease>(lease);
        }

        private readonly NodeBehaviorTestRecorder m_recorder;
        private readonly Func<string, string, Exception> m_createFailure;
        private readonly Func<string, string, NodeBehaviorTestLeaseOptions> m_leaseOptions;
    }

    internal sealed class NodeBehaviorTestLease : INodeBehaviorLease
    {
        public NodeBehaviorTestLease(
            string nodeName,
            string factoryName,
            NodeBehaviorContext context,
            NodeBehaviorTestRecorder recorder,
            NodeBehaviorTestLeaseOptions options)
        {
            NodeName = nodeName;
            FactoryName = factoryName;
            Context = context;
            m_recorder = recorder;
            m_options = options ?? new NodeBehaviorTestLeaseOptions();
        }

        public string NodeName { get; }

        public string FactoryName { get; }

        public NodeBehaviorContext Context { get; }

        public int ActivateCount { get; private set; }

        public int DeactivateCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsActive { get; private set; }

        public bool DeactivationTokenCanBeCanceled { get; private set; }

        public async ValueTask ActivateAsync(CancellationToken cancellationToken)
        {
            ActivateCount++;
            m_recorder.Record($"activate:{NodeName}:{FactoryName}");
            if (m_options.OnActivateAsync is not null)
            {
                await m_options
                    .OnActivateAsync(Context, cancellationToken)
                    .ConfigureAwait(false);
            }
            m_options.CancelActivation?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (m_options.ActivationException is not null)
            {
                throw m_options.ActivationException;
            }
            IsActive = true;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            DeactivateCount++;
            DeactivationTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            IsActive = false;
            m_recorder.Record($"deactivate:{NodeName}:{FactoryName}");
            if (m_options.DeactivationException is not null)
            {
                throw m_options.DeactivationException;
            }
            return default;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsActive = false;
            m_recorder.Record($"dispose:{NodeName}:{FactoryName}");
            if (m_options.DisposalException is not null)
            {
                throw m_options.DisposalException;
            }
            return default;
        }

        private readonly NodeBehaviorTestRecorder m_recorder;
        private readonly NodeBehaviorTestLeaseOptions m_options;
    }

    internal sealed class NodeBehaviorTestLeaseOptions
    {
        public Exception ActivationException { get; init; }

        public Exception DeactivationException { get; init; }

        public Exception DisposalException { get; init; }

        public Action CancelActivation { get; init; }

        public Func<NodeBehaviorContext, CancellationToken, ValueTask> OnActivateAsync { get; init; }
    }
}
