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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="INodeManagerBuilder.Import"/>: the documents
    /// imported during one <c>Configure</c> pass form a single batch which is
    /// linked once and registered with the manager afterwards.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    [Category("NodeSetImport")]
    [Parallelizable]
    public sealed class NodeSetImportBuilderTests
    {
        private const ushort kNs = 2;
        private const string kNamespaceUri = "urn:opcfoundation.org:Tests:FluentNodeSetImport";

        [Test]
        public void ImportedNodesResolveBeforeTheyAreRegistered()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Imported">
                    <DisplayName>Imported</DisplayName>
                  </UAObject>
                """));

            INodeBuilder node = harness.Builder.Node(new NodeId(100u, kNs));

            Assert.Multiple(() =>
            {
                Assert.That(node.Node.BrowseName.Name, Is.EqualTo("Imported"));
                Assert.That(harness.Added, Is.Empty);
            });
        }

        [Test]
        public async Task CompleteRegistersImportedRootsAndAttachedSubtreesAsync()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Root">
                    <DisplayName>Root</DisplayName>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=101" BrowseName="1:Child"
                              ParentNodeId="ns=1;i=100" DataType="i=6">
                    <DisplayName>Child</DisplayName>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=100</Reference>
                    </References>
                  </UAVariable>
                """));

            await harness.CompleteAsync().ConfigureAwait(false);

            NodeState root = harness.Builder.ImportedNodes[0];
            NodeState child = harness.Builder.ImportedNodes[1];
            var children = new List<BaseInstanceState>();
            root.GetChildren(harness.Context, children);

            Assert.Multiple(() =>
            {
                // Only the root is handed to the manager; the manager itself
                // indexes the subtree recursively.
                Assert.That(harness.Added, Is.EquivalentTo(new[] { root }));
                Assert.That(children, Is.EquivalentTo(new[] { child }));
                Assert.That(((BaseInstanceState)child).Parent, Is.SameAs(root));
            });
        }

        [Test]
        public async Task DocumentsOfOneBatchLinkAcrossEachOtherAsync()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Root">
                    <DisplayName>Root</DisplayName>
                  </UAObject>
                """));
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAVariable NodeId="ns=1;i=200" BrowseName="1:LateChild"
                              ParentNodeId="ns=1;i=100" DataType="i=6">
                    <DisplayName>LateChild</DisplayName>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=100</Reference>
                    </References>
                  </UAVariable>
                """));

            await harness.CompleteAsync().ConfigureAwait(false);

            NodeState root = harness.Builder.ImportedNodes[0];
            var child = (BaseInstanceState)harness.Builder.ImportedNodes[1];

            Assert.Multiple(() =>
            {
                Assert.That(child.Parent, Is.SameAs(root));
                Assert.That(harness.Added, Is.EquivalentTo(new[] { root }));
            });
        }

        [Test]
        public async Task ImportedChildAttachesToANodeTheManagerAlreadyOwnsAsync()
        {
            Harness harness = Harness.Create();
            harness.AddExisting(
                new BaseObjectState(null)
                {
                    NodeId = new NodeId(500u, kNs),
                    BrowseName = new QualifiedName("Existing", kNs),
                    DisplayName = new LocalizedText("Existing")
                });

            harness.Builder.Import(ReadNodeSet(
                """
                  <UAVariable NodeId="ns=1;i=100" BrowseName="1:Overlay"
                              ParentNodeId="ns=1;i=500" DataType="i=6">
                    <DisplayName>Overlay</DisplayName>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=500</Reference>
                    </References>
                  </UAVariable>
                """));

            await harness.CompleteAsync().ConfigureAwait(false);

            var overlay = (BaseInstanceState)harness.Builder.ImportedNodes[0];
            NodeState existing = harness.Existing[new NodeId(500u, kNs)];
            var children = new List<BaseInstanceState>();
            existing.GetChildren(harness.Context, children);

            Assert.Multiple(() =>
            {
                Assert.That(overlay.Parent, Is.SameAs(existing));
                Assert.That(children, Is.EquivalentTo(new[] { overlay }));
                // The attachment point is not a root of the batch, so it is
                // registered explicitly.
                Assert.That(harness.Added, Is.EquivalentTo(new[] { overlay }));
            });
        }

        [Test]
        public void ImportUsesTheFactoriesTheManagerProvides()
        {
            Harness harness = Harness.Create(
                new ManualFactoryProvider(
                    new ManualImportFactory(
                        NodeClass.Object,
                        new ExpandedNodeId(1000u, kNamespaceUri),
                        static () => new TypedObjectState(null))));

            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Typed">
                    <DisplayName>Typed</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                """));

            Assert.That(
                harness.Builder.ImportedNodes[0],
                Is.TypeOf<TypedObjectState>());
        }

        [Test]
        public void ImportAcceptsAFactoryProviderPerCall()
        {
            Harness harness = Harness.Create();

            harness.Builder.Import(
                ReadNodeSet(
                    """
                      <UAObject NodeId="ns=1;i=100" BrowseName="1:Typed">
                        <DisplayName>Typed</DisplayName>
                        <References>
                          <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                        </References>
                      </UAObject>
                    """),
                new ManualFactoryProvider(
                    new ManualImportFactory(
                        NodeClass.Object,
                        new ExpandedNodeId(1000u, kNamespaceUri),
                        static () => new TypedObjectState(null))));

            Assert.That(
                harness.Builder.ImportedNodes[0],
                Is.TypeOf<TypedObjectState>());
        }

        [Test]
        public void OneBatchRejectsASecondFactoryProvider()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(
                ReadNodeSet(
                    """
                      <UAObject NodeId="ns=1;i=100" BrowseName="1:First">
                        <DisplayName>First</DisplayName>
                      </UAObject>
                    """),
                new ManualFactoryProvider());

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => harness.Builder.Import(
                    ReadNodeSet(
                        """
                          <UAObject NodeId="ns=1;i=101" BrowseName="1:Second">
                            <DisplayName>Second</DisplayName>
                          </UAObject>
                        """),
                    new ManualFactoryProvider()));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ImportedChildReplacesAGeneratedPlaceholderAsync()
        {
            Harness harness = Harness.Create(CreateTypedValueProvider());
            var parent = new GeneratedLikeObjectState(null)
            {
                NodeId = new NodeId(500u, kNs),
                BrowseName = new QualifiedName("Device", kNs),
                DisplayName = new LocalizedText("Device")
            };
            parent.Create(harness.Context, NodeId.Null, parent.BrowseName, LocalizedText.Null, true);
            parent.NodeId = new NodeId(500u, kNs);
            parent.MandatoryValue.NodeId = new NodeId(501u, kNs);
            harness.AddExisting(parent);
            harness.AddExisting(parent.MandatoryValue);

            var referrer = new BaseObjectState(null)
            {
                NodeId = new NodeId(600u, kNs),
                BrowseName = new QualifiedName("Referrer", kNs),
                DisplayName = new LocalizedText("Referrer")
            };
            referrer.AddReference(
                ReferenceTypeIds.Organizes,
                false,
                new NodeId(501u, kNs));
            harness.AddExisting(referrer);

            harness.Builder.Import(ReadNodeSet(
                """
                  <UAVariable NodeId="ns=1;i=700" BrowseName="1:MandatoryValue"
                              ParentNodeId="ns=1;i=500" DataType="i=6">
                    <DisplayName>MandatoryValue</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=1001</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=500</Reference>
                    </References>
                  </UAVariable>
                """));

            await harness.CompleteAsync().ConfigureAwait(false);

            NodeState imported = harness.Builder.ImportedNodes[0];
            var references = new List<IReference>();
            referrer.GetReferences(harness.Context, references);

            Assert.Multiple(() =>
            {
                Assert.That(parent.MandatoryValue, Is.SameAs(imported));
                Assert.That(harness.Added, Does.Contain(imported));
                Assert.That(harness.Removed.Select(node => node.NodeId),
                    Is.EquivalentTo(new[] { new NodeId(501u, kNs) }));
                Assert.That(
                    references.Select(reference => reference.TargetId),
                    Does.Contain((ExpandedNodeId)new NodeId(700u, kNs)));
            });
        }

        [Test]
        public void ReplacingAConfiguredPlaceholderIsRejected()
        {
            Harness harness = Harness.Create(CreateTypedValueProvider());
            var parent = new GeneratedLikeObjectState(null)
            {
                NodeId = new NodeId(500u, kNs),
                BrowseName = new QualifiedName("Device", kNs),
                DisplayName = new LocalizedText("Device")
            };
            parent.Create(harness.Context, NodeId.Null, parent.BrowseName, LocalizedText.Null, true);
            parent.NodeId = new NodeId(500u, kNs);
            parent.MandatoryValue.NodeId = new NodeId(501u, kNs);
            harness.AddExisting(parent);
            harness.AddExisting(parent.MandatoryValue);

            // The placeholder is wired before the overlay is imported.
            harness.Builder.Node(new NodeId(501u, kNs));
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAVariable NodeId="ns=1;i=700" BrowseName="1:MandatoryValue"
                              ParentNodeId="ns=1;i=500" DataType="i=6">
                    <DisplayName>MandatoryValue</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=1001</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=500</Reference>
                    </References>
                  </UAVariable>
                """));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.CompleteAsync().ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ImportIsRejectedAfterTheBatchCompletedAsync()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Root">
                    <DisplayName>Root</DisplayName>
                  </UAObject>
                """));
            await harness.CompleteAsync().ConfigureAwait(false);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => harness.Builder.Import(ReadNodeSet(
                    """
                      <UAObject NodeId="ns=1;i=101" BrowseName="1:Late">
                        <DisplayName>Late</DisplayName>
                      </UAObject>
                    """)));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void SealingWithAnUnregisteredImportBatchIsRejected()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Root">
                    <DisplayName>Root</DisplayName>
                  </UAObject>
                """));

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => harness.Builder.Seal());

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void ImportIsRejectedAfterTheBuilderIsSealed()
        {
            Harness harness = Harness.Create();
            harness.Builder.Seal();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => harness.Builder.Import(ReadNodeSet(
                    """
                      <UAObject NodeId="ns=1;i=100" BrowseName="1:Root">
                        <DisplayName>Root</DisplayName>
                      </UAObject>
                    """)));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidState));
        }

        [Test]
        public void ImportRejectsANullDocument()
        {
            Harness harness = Harness.Create();

            Assert.Throws<ArgumentNullException>(() => harness.Builder.Import(null));
        }

        [Test]
        public void ImportRejectsANodeIdOwnedByAnExistingRoot()
        {
            Harness harness = Harness.Create();
            harness.AddExisting(
                new BaseObjectState(null)
                {
                    NodeId = new NodeId(100u, kNs),
                    BrowseName = new QualifiedName("Existing", kNs),
                    DisplayName = new LocalizedText("Existing")
                });

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => harness.Builder.Import(ReadNodeSet(
                    """
                      <UAObject NodeId="ns=1;i=100" BrowseName="1:Clash">
                        <DisplayName>Clash</DisplayName>
                      </UAObject>
                    """)));

            Assert.That(exception.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void ImportedInstancesAreFoundByTypeDefinition()
        {
            Harness harness = Harness.Create();
            harness.Builder.Import(ReadNodeSet(
                """
                  <UAObject NodeId="ns=1;i=100" BrowseName="1:Typed">
                    <DisplayName>Typed</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                """));

            INodeBuilder node = harness.Builder.NodeFromTypeId(new NodeId(1000u, kNs));

            Assert.That(node.Node.NodeId, Is.EqualTo(new NodeId(100u, kNs)));
        }

        private static ManualFactoryProvider CreateTypedValueProvider()
        {
            return new ManualFactoryProvider(
                new ManualImportFactory(
                    NodeClass.Variable,
                    new ExpandedNodeId(1001u, kNamespaceUri),
                    static () => new TypedValueState(null)));
        }
        private static UANodeSet ReadNodeSet(string nodes)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                "    <Uri>" + kNamespaceUri + "</Uri>\r\n" +
                "  </NamespaceUris>\r\n" +
                nodes.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n" +
                "</UANodeSet>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return UANodeSet.Read(stream);
        }

        /// <summary>
        /// Drives <see cref="NodeManagerBuilder"/> without a server: the
        /// registration callbacks record what the owning node manager would
        /// index and remove.
        /// </summary>
        private sealed class Harness
        {
            public static Harness Create(INodeSetImportFactoryProvider provider = null)
            {
                var namespaceUris = new NamespaceTable();
                var context = new SystemContext(NUnitTelemetryContext.Create())
                {
                    NamespaceUris = namespaceUris,
                    ServerUris = new StringTable(),
                    TypeTable = new TypeTable(namespaceUris),
                    EncodeableFactory = EncodeableFactory.Create()
                };
                // Index 1 of the document maps onto the manager's namespace.
                context.NamespaceUris.GetIndexOrAppend("urn:placeholder");
                ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                Assert.That(namespaceIndex, Is.EqualTo(kNs));

                var harness = new Harness { Context = context };
                var manager = new Mock<IAsyncNodeManager>();
                if (provider is not null)
                {
                    manager.As<INodeSetImportFactoryProvider>()
                        .Setup(instance => instance.GetNodeSetImportFactories())
                        .Returns(provider.GetNodeSetImportFactories());
                }

                harness.Builder = new NodeManagerBuilder(
                    context,
                    manager.Object,
                    kNs,
                    browseName => harness.Existing.Values
                        .FirstOrDefault(node => node.BrowseName == browseName),
                    nodeId => harness.Existing.GetValueOrDefault(nodeId),
                    typeDefinitionId =>
                    [
                        .. harness.Existing.Values
                            .Where(node => node is BaseInstanceState instance &&
                                instance.TypeDefinitionId == typeDefinitionId)
                    ]);
                return harness;
            }

            public ISystemContext Context { get; private init; }

            public NodeManagerBuilder Builder { get; private set; }

            public Dictionary<NodeId, NodeState> Existing { get; } = [];

            public List<NodeState> Added { get; } = [];

            public List<NodeState> Removed { get; } = [];

            public void AddExisting(NodeState node)
            {
                Existing[node.NodeId] = node;
            }

            public ValueTask CompleteAsync()
            {
                return Builder.CompleteNodeSetImportsAsync(
                    new Dictionary<NodeId, NodeState>(Existing),
                    (node, _) =>
                    {
                        Added.Add(node);
                        Existing[node.NodeId] = node;
                        return default;
                    },
                    (node, _) =>
                    {
                        Removed.Add(node);
                        Existing.Remove(node.NodeId);
                        return default;
                    },
                    CancellationToken.None);
            }
        }

        private sealed class ManualFactoryProvider : INodeSetImportFactoryProvider
        {
            public ManualFactoryProvider(params INodeSetImportFactory[] factories)
            {
                m_factories = factories;
            }

            public ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories()
            {
                return m_factories;
            }

            private readonly ArrayOf<INodeSetImportFactory> m_factories;
        }

        private sealed class ManualImportFactory : INodeSetImportFactory
        {
            public ManualImportFactory(
                NodeClass nodeClass,
                ExpandedNodeId discriminatorId,
                Func<NodeState> create)
            {
                NodeClass = nodeClass;
                DiscriminatorId = discriminatorId;
                Discriminator = nodeClass switch
                {
                    NodeClass.Object or NodeClass.Variable =>
                        NodeSetImportDiscriminator.TypeDefinition,
                    NodeClass.Method => NodeSetImportDiscriminator.MethodDeclaration,
                    _ => NodeSetImportDiscriminator.NodeId
                };
                m_create = create;
            }

            public NodeClass NodeClass { get; }

            public NodeSetImportDiscriminator Discriminator { get; }

            public ExpandedNodeId DiscriminatorId { get; }

            public NodeState CreateEmptyState()
            {
                return m_create();
            }

            private readonly Func<NodeState> m_create;
        }

        private sealed class TypedObjectState : BaseObjectState
        {
            public TypedObjectState(NodeState parent)
                : base(parent)
            {
            }
        }

        private sealed class TypedValueState : BaseDataVariableState
        {
            public TypedValueState(NodeState parent)
                : base(parent)
            {
            }
        }

        /// <summary>
        /// Mimics a generated state: <c>MandatoryValue</c> lives in an
        /// explicitly defined slot rather than the ordinary child collection.
        /// </summary>
        private sealed class GeneratedLikeObjectState : BaseObjectState
        {
            public GeneratedLikeObjectState(NodeState parent)
                : base(parent)
            {
            }

            public BaseVariableState MandatoryValue { get; private set; }

            protected override void Initialize(ISystemContext context)
            {
                base.Initialize(context);
                MandatoryValue = new BaseDataVariableState(this)
                {
                    BrowseName = new QualifiedName("MandatoryValue", kNs),
                    DisplayName = new LocalizedText("MandatoryValue"),
                    DataType = DataTypeIds.Int32
                };
            }

            public override void GetChildren(
                ISystemContext context,
                IList<BaseInstanceState> children)
            {
                if (MandatoryValue is not null)
                {
                    children.Add(MandatoryValue);
                }
                base.GetChildren(context, children);
            }

            protected override void RemoveExplicitlyDefinedChild(BaseInstanceState child)
            {
                if (ReferenceEquals(MandatoryValue, child))
                {
                    MandatoryValue = null;
                    DetachExplicitlyDefinedChild(child);
                }
                base.RemoveExplicitlyDefinedChild(child);
            }

            protected override BaseInstanceState FindChild(
                ISystemContext context,
                QualifiedName browseName,
                bool createOrReplace,
                BaseInstanceState replacement,
                bool assignInstanceNodeIds = true)
            {
                if (browseName.Name == "MandatoryValue")
                {
                    if (createOrReplace && MandatoryValue is null)
                    {
                        MandatoryValue = replacement as BaseVariableState ??
                            new BaseDataVariableState(this)
                            {
                                BrowseName = browseName,
                                DisplayName = new LocalizedText(browseName.Name),
                                DataType = DataTypeIds.Int32
                            };
                    }
                    else if (createOrReplace && replacement is BaseVariableState variable)
                    {
                        MandatoryValue = variable;
                    }
                    return MandatoryValue;
                }

                return base.FindChild(
                    context,
                    browseName,
                    createOrReplace,
                    replacement,
                    assignInstanceNodeIds);
            }
        }
    }
}
