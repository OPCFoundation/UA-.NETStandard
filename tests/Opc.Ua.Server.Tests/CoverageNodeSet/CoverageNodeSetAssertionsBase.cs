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
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// The shared, data-driven assertion battery that is executed identically
    /// against both coverage pipelines (see
    /// <see cref="CoverageTestSourceGenAssertionsTests"/> and
    /// <see cref="CoverageTestRuntimeAssertionsTests"/>). Every attribute or
    /// reference dropped by either pipeline fails a test here.
    /// </summary>
    /// <typeparam name="TServer">
    /// The concrete <see cref="ReferenceServer"/> host under test.
    /// </typeparam>
    public abstract class CoverageNodeSetAssertionsBase<TServer>
        where TServer : global::Quickstarts.ReferenceServer.ReferenceServer
    {
        private ServerFixture<TServer> m_serverFixture;
        private TServer m_server;
        private string m_pkiRoot;
        private ushort m_ns;

        /// <summary>
        /// Creates the concrete server host from a telemetry context.
        /// </summary>
        protected abstract TServer CreateServer(ITelemetryContext telemetry);

        /// <summary>
        /// Starts the concrete coverage server host.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                Guid.NewGuid().ToString("N").Substring(0, 8));

            m_serverFixture = new ServerFixture<TServer>(CreateServer)
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_ns = (ushort)m_server.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.NamespaceUri);
        }

        /// <summary>
        /// Stops the server and cleans up PKI artefacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_server?.Dispose();

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The model namespace must be registered after startup, and its
        /// version/publication metadata must be discoverable.
        /// </summary>
        [Test]
        [Order(100)]
        public void ModelNamespaceRegistered()
        {
            Assert.That(m_ns, Is.GreaterThan(0),
                "The coverage model namespace should be registered.");
        }

        /// <summary>
        /// Every authored node resolves in the address space with the correct
        /// BrowseName and NodeClass.
        /// </summary>
        [Test]
        [Order(200)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.Nodes))]
        public async Task NodePresentWithBrowseNameAndClassAsync(CoverageTestCatalogue.ExpectedNode expected)
        {
            NodeState node = await FindNodeAsync(expected.Id).ConfigureAwait(false);

            Assert.That(node, Is.Not.Null,
                $"Node ns={m_ns};i={expected.Id} ({expected.BrowseName}) should be present.");
            Assert.Multiple(() =>
            {
                Assert.That(node.BrowseName.Name, Is.EqualTo(expected.BrowseName));
                Assert.That(node.NodeClass, Is.EqualTo(expected.NodeClass));
            });
        }

        /// <summary>
        /// Every authored node materialises as the expected
        /// <see cref="NodeState"/> family for its NodeClass.
        /// </summary>
        [Test]
        [Order(210)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.Nodes))]
        public async Task NodeMaterialisesAsExpectedStateFamilyAsync(CoverageTestCatalogue.ExpectedNode expected)
        {
            NodeState node = await FindNodeAsync(expected.Id).ConfigureAwait(false);
            Assert.That(node, Is.Not.Null);
            Assert.That(node, Is.AssignableTo(ExpectedStateFamily(expected.NodeClass)),
                $"Node {expected.BrowseName} should materialise as {ExpectedStateFamily(expected.NodeClass).Name}.");
        }

        /// <summary>
        /// The authored node count in the model namespace matches the catalogue
        /// exactly (no extra, no missing).
        /// </summary>
        [Test]
        [Order(220)]
        public async Task OwnedNodeCountMatchesCatalogueAsync()
        {
            int found = 0;
            foreach (CoverageTestCatalogue.ExpectedNode expected in CoverageTestCatalogue.Nodes)
            {
                if (await FindNodeAsync(expected.Id).ConfigureAwait(false) != null)
                {
                    found++;
                }
            }

            Assert.That(found, Is.EqualTo(CoverageTestCatalogue.Nodes.Count),
                "All catalogue nodes must be present.");
        }

        /// <summary>
        /// Every reference relation in the catalogue exists on the source node
        /// with the correct type, direction and target.
        /// </summary>
        [Test]
        [Order(300)]
        [TestCaseSource(typeof(CoverageTestCatalogue), nameof(CoverageTestCatalogue.References))]
        public async Task ReferenceExistsAsync(CoverageTestCatalogue.ExpectedReference expected)
        {
            NodeState source = await FindNodeAsync(expected.Source).ConfigureAwait(false);
            Assert.That(source, Is.Not.Null,
                $"Reference source ns={m_ns};i={expected.Source} should exist.");

            NodeId referenceTypeId = expected.ReferenceType is >= 5001 and <= 5004
                ? new NodeId(expected.ReferenceType, m_ns)
                : new NodeId(expected.ReferenceType, 0);
            NodeId targetId = expected.TargetIsOwned
                ? new NodeId(expected.Target, m_ns)
                : new NodeId(expected.Target, 0);

            bool exists = ReferenceExists(
                source,
                referenceTypeId,
                isInverse: !expected.IsForward,
                targetId: targetId);

            Assert.That(exists, Is.True,
                $"{expected.Source} should have {(expected.IsForward ? "forward" : "inverse")} " +
                $"reference {referenceTypeId} to {targetId}.");
        }

        /// <summary>
        /// The root instance carries its non-default common-node attributes.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task RootAttributesRoundTripAsync()
        {
            NodeState root = await FindNodeAsync(5400).ConfigureAwait(false);
            Assert.That(root, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That((uint)root.WriteMask, Is.EqualTo(2097151u));
                Assert.That((uint)root.UserWriteMask, Is.EqualTo(2097151u));
            });
        }

        /// <summary>
        /// The boolean value variable carries its non-default variable
        /// attributes and multi-locale DisplayName/Description.
        /// </summary>
        [Test]
        [Order(410)]
        public async Task BooleanValueAttributesRoundTripAsync()
        {
            var v = (BaseVariableState)await FindNodeAsync(5420).ConfigureAwait(false);
            Assert.That(v, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(v.AccessLevel, Is.EqualTo((byte)3));
                Assert.That(v.UserAccessLevel, Is.EqualTo((byte)3));
                Assert.That(v.MinimumSamplingInterval, Is.EqualTo(100.0));
                Assert.That(v.Historizing, Is.True);
                Assert.That(v.DisplayName.Text, Is.EqualTo("BooleanValue"));
            });
        }

        /// <summary>
        /// Reference-type attributes (Symmetric, InverseName, IsAbstract)
        /// round-trip on the custom reference types.
        /// </summary>
        [Test]
        [Order(420)]
        public async Task ReferenceTypeAttributesRoundTripAsync()
        {
            var hierarchical = (ReferenceTypeState)await FindNodeAsync(5001).ConfigureAwait(false);
            var symmetric = (ReferenceTypeState)await FindNodeAsync(5002).ConfigureAwait(false);
            var abstractRef = (ReferenceTypeState)await FindNodeAsync(5003).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(hierarchical.Symmetric, Is.False);
                Assert.That(hierarchical.InverseName.Text, Is.EqualTo("IsCoverageChildOf"));
                Assert.That(symmetric.Symmetric, Is.True);
                Assert.That(abstractRef.IsAbstract, Is.True);
            });
        }

        /// <summary>
        /// The view carries ContainsNoLoops and a non-zero EventNotifier.
        /// </summary>
        [Test]
        [Order(430)]
        public async Task ViewAttributesRoundTripAsync()
        {
            var view = (ViewState)await FindNodeAsync(5460).ConfigureAwait(false);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.ContainsNoLoops, Is.True);
        }

        /// <summary>
        /// The event-source object carries a non-zero EventNotifier bitmask.
        /// </summary>
        [Test]
        [Order(440)]
        public async Task EventSourceEventNotifierRoundTripAsync()
        {
            var obj = (BaseObjectState)await FindNodeAsync(5410).ConfigureAwait(false);
            Assert.That(obj, Is.Not.Null);
            Assert.That(obj.EventNotifier, Is.EqualTo((byte)1));
        }

        /// <summary>
        /// The locked method is not executable.
        /// </summary>
        [Test]
        [Order(450)]
        public async Task LockedMethodNotExecutableAsync()
        {
            var method = (MethodState)await FindNodeAsync(5451).ConfigureAwait(false);
            Assert.That(method, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(method.Executable, Is.False);
                Assert.That(method.UserExecutable, Is.False);
            });
        }

        /// <summary>
        /// The data-type definitions round-trip to the expected shapes.
        /// </summary>
        [Test]
        [Order(460)]
        public async Task DataTypeDefinitionsRoundTripAsync()
        {
            var enumeration = (DataTypeState)await FindNodeAsync(5100).ConfigureAwait(false);
            var structure = (DataTypeState)await FindNodeAsync(5130).ConfigureAwait(false);
            var union = (DataTypeState)await FindNodeAsync(5150).ConfigureAwait(false);
            var optionSet = (DataTypeState)await FindNodeAsync(5110).ConfigureAwait(false);
            var abstractType = (DataTypeState)await FindNodeAsync(5120).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(enumeration.DataTypeDefinition.TryGetValue<EnumDefinition>(out _), Is.True);
                Assert.That(structure.DataTypeDefinition.TryGetValue<StructureDefinition>(out _), Is.True);
                Assert.That(optionSet.DataTypeDefinition.TryGetValue<EnumDefinition>(out _), Is.True);
                Assert.That(abstractType.IsAbstract, Is.True);

                Assert.That(union.DataTypeDefinition.TryGetValue<StructureDefinition>(out StructureDefinition unionDef), Is.True);
                Assert.That(unionDef.StructureType, Is.EqualTo(StructureType.Union));
            });
        }

        /// <summary>
        /// The scalar, array and matrix variable value shapes round-trip.
        /// </summary>
        [Test]
        [Order(470)]
        public async Task ValueShapesRoundTripAsync()
        {
            var scalar = (BaseVariableState)await FindNodeAsync(5421).ConfigureAwait(false);
            var array = (BaseVariableState)await FindNodeAsync(5422).ConfigureAwait(false);
            var matrix = (BaseVariableState)await FindNodeAsync(5423).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(scalar.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(scalar.WrappedValue.TryGetValue(out int scalarValue), Is.True);
                Assert.That(scalarValue, Is.EqualTo(-12345));

                Assert.That(array.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(array.WrappedValue.TryGetValue(out ArrayOf<string> arrayValue), Is.True);
                Assert.That(arrayValue.Count, Is.EqualTo(2));

                Assert.That(matrix.ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
                Assert.That(matrix.WrappedValue.IsNull, Is.False);
                Assert.That(matrix.WrappedValue.TypeInfo.ValueRank, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// The deep, branching instance tree is navigable child-by-child in
        /// both pipelines (TreeRoot → BranchA → SubBranchA → LeafA2).
        /// </summary>
        [Test]
        [Order(480)]
        public async Task DeepTreeNavigableAsync()
        {
            NodeState treeRoot = await FindNodeAsync(5490).ConfigureAwait(false);
            Assert.That(treeRoot, Is.Not.Null);

            var context = m_server.CurrentInstance.DefaultSystemContext;
            BaseInstanceState branchA = treeRoot.FindChild(context, new QualifiedName("BranchA", m_ns));
            BaseInstanceState subBranchA = branchA?.FindChild(context, new QualifiedName("SubBranchA", m_ns));
            BaseInstanceState leafA2 = subBranchA?.FindChild(context, new QualifiedName("LeafA2", m_ns));

            Assert.Multiple(() =>
            {
                Assert.That(branchA?.NodeId, Is.EqualTo(new NodeId(5491u, m_ns)));
                Assert.That(subBranchA?.NodeId, Is.EqualTo(new NodeId(5493u, m_ns)));
                Assert.That(leafA2, Is.Not.Null);
                Assert.That(leafA2.NodeId, Is.EqualTo(new NodeId(5494u, m_ns)));
            });
        }

        /// <summary>
        /// The callable method exposes its argument metadata.
        /// </summary>
        [Test]
        [Order(500)]
        public async Task MethodArgumentMetadataRoundTripsAsync()
        {
            var inputArgs = (BaseVariableState)await FindNodeAsync(5452).ConfigureAwait(false);
            var outputArgs = (BaseVariableState)await FindNodeAsync(5453).ConfigureAwait(false);

            Argument[] inputs = ReadArguments(inputArgs);
            Argument[] outputs = ReadArguments(outputArgs);

            Assert.Multiple(() =>
            {
                Assert.That(inputs, Is.Not.Null.And.Length.EqualTo(2));
                Assert.That(inputs[0].Name, Is.EqualTo("A"));
                Assert.That(inputs[1].Name, Is.EqualTo("B"));
                Assert.That(outputs, Is.Not.Null.And.Length.EqualTo(1));
                Assert.That(outputs[0].Name, Is.EqualTo("Sum"));
            });
        }
        /// <summary>
        /// The method instance points at its type-level declaration.
        /// </summary>
        [Test]
        [Order(510)]
        public async Task MethodDeclarationIdWiredAsync()
        {
            var method = (MethodState)await FindNodeAsync(5404).ConfigureAwait(false);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.MethodDeclarationId, Is.EqualTo(new NodeId(5216u, m_ns)));
        }

        /// <summary>
        /// The complex-type loader registered stand-ins for the runtime-loaded
        /// structure and enumeration (mirrors the default server complex-type
        /// path).
        /// </summary>
        /// <summary>
        /// The complex-type loader registered stand-ins for the runtime-loaded
        /// enumeration and option set (mirrors the default server complex-type
        /// path). Structure definitions are additionally validated by
        /// <see cref="DataTypeDefinitionsRoundTripAsync"/>.
        /// </summary>
        [Test]
        [Order(600)]
        public void ComplexTypesRegistered()
        {
            IServerInternal server = m_server.CurrentInstance;

            ExpandedNodeId enumTypeId = NodeId.ToExpandedNodeId(
                new NodeId(5100u, m_ns), server.NamespaceUris);
            ExpandedNodeId optionSetTypeId = NodeId.ToExpandedNodeId(
                new NodeId(5110u, m_ns), server.NamespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.Factory.TryGetEnumeratedType(enumTypeId, out IEnumeratedType enumType),
                    Is.True,
                    "CoverageEnumeration stand-in should be registered in the server factory.");
                Assert.That(enumType, Is.Not.Null);

                Assert.That(
                    server.Factory.TryGetEncodeableType(optionSetTypeId, out IEncodeableType optionSetType),
                    Is.True,
                    "CoverageOptionSet stand-in should be registered in the server factory.");
                Assert.That(optionSetType, Is.Not.Null);
            });
        }

        /// <summary>
        /// The secondary (dependent) model namespace is loaded, its nodes are
        /// present, and the cross-namespace references between the two models
        /// resolve in both directions.
        /// </summary>
        [Test]
        [Order(700)]
        public async Task SecondaryNamespaceRoundTripsAsync()
        {
            ushort ns2 = (ushort)m_server.CurrentInstance.NamespaceUris
                .GetIndex(CoverageTestCatalogue.SecondaryNamespaceUri);
            Assert.That(ns2, Is.GreaterThan(0), "secondary namespace should be registered.");

            foreach (CoverageTestCatalogue.ExpectedNode expected in CoverageTestCatalogue.SecondaryNodes)
            {
                NodeState node = await m_server.CurrentInstance.NodeManager
                    .FindNodeInAddressSpaceAsync(new NodeId(expected.Id, ns2))
                    .ConfigureAwait(false);
                Assert.That(node, Is.Not.Null,
                    $"secondary node ns={ns2};i={expected.Id} ({expected.BrowseName}) should be present.");
                Assert.That(node.NodeClass, Is.EqualTo(expected.NodeClass));
            }

            NodeState secondaryInstance = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(6010u, ns2)).ConfigureAwait(false);
            NodeState secondaryType = await m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(6001u, ns2)).ConfigureAwait(false);
            NodeState primaryRoot = await FindNodeAsync(5400).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                // SecondaryInstance is parented under the primary CoverageRoot.
                Assert.That(
                    ReferenceExists(secondaryInstance, new NodeId(ReferenceTypes.HasComponent, 0),
                        isInverse: true, new NodeId(5400u, m_ns)),
                    Is.True,
                    "SecondaryInstance should have an inverse HasComponent to the primary CoverageRoot.");

                // SecondaryObjectType is a subtype of the primary CoverageObjectType.
                Assert.That(
                    ReferenceExists(secondaryType, new NodeId(ReferenceTypes.HasSubtype, 0),
                        isInverse: true, new NodeId(5210u, m_ns)),
                    Is.True,
                    "SecondaryObjectType should be a HasSubtype of the primary CoverageObjectType.");

                // The forward edge is visible from the primary root (resolved
                // across NodeManagers where the two models are hosted separately).
                Assert.That(
                    ReferenceExists(primaryRoot, new NodeId(ReferenceTypes.HasComponent, 0),
                        isInverse: false, new NodeId(6010u, ns2)),
                    Is.True,
                    "The primary CoverageRoot should have a forward HasComponent to SecondaryInstance.");
            });
        }

        private ValueTask<NodeState> FindNodeAsync(uint id)
        {
            return m_server.CurrentInstance.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(id, m_ns));
        }

        private bool ReferenceExists(NodeState node, NodeId referenceTypeId, bool isInverse, NodeId targetId)
        {
            var context = m_server.CurrentInstance.DefaultSystemContext;
            using INodeBrowser browser = node.CreateBrowser(
                context, null, NodeId.Null, false, BrowseDirection.Both, QualifiedName.Null, null, true);

            for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
            {
                if (reference.ReferenceTypeId != referenceTypeId ||
                    reference.IsInverse != isInverse)
                {
                    continue;
                }

                NodeId target = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                if (target == targetId)
                {
                    return true;
                }
            }

            return false;
        }

        private static Type ExpectedStateFamily(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.Object => typeof(BaseObjectState),
                NodeClass.Variable => typeof(BaseVariableState),
                NodeClass.Method => typeof(MethodState),
                NodeClass.ObjectType => typeof(BaseObjectTypeState),
                NodeClass.VariableType => typeof(BaseVariableTypeState),
                NodeClass.ReferenceType => typeof(ReferenceTypeState),
                NodeClass.DataType => typeof(DataTypeState),
                NodeClass.View => typeof(ViewState),
                _ => typeof(NodeState)
            };
        }

        private Argument[] ReadArguments(BaseVariableState arguments)
        {
            if (arguments == null)
            {
                return null;
            }

            IServiceMessageContext context = m_server.CurrentInstance.MessageContext;
            if (arguments.WrappedValue.TryGetValue(out ArrayOf<Argument> decoded, context))
            {
                return decoded.ToArray();
            }

            return null;
        }
    }
}
