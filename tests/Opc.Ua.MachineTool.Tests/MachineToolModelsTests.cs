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
using Moq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises independent model and factory registration through the public model seams.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class MachineToolModelsTests
    {
        /// <summary>
        /// All three nonempty selections contain their exact released transitive closure.
        /// </summary>
        [TestCase(MachineToolParts.MachineTool)]
        [TestCase(MachineToolParts.Wmtp)]
        [TestCase(MachineToolParts.All)]
        public void SelectedNamespaceClosureMatchesReleasedDependencies(MachineToolParts parts)
        {
            Assert.That(MachineToolModels.GetNamespaceUris(parts).ToArray(),
                Is.EqualTo(ExpectedNamespaceUris(parts)));
        }

        /// <summary>
        /// Loading populates actual model nodes without importing the unselected model or its private dependencies.
        /// </summary>
        [TestCase(MachineToolParts.MachineTool)]
        [TestCase(MachineToolParts.Wmtp)]
        [TestCase(MachineToolParts.All)]
        public void SelectedModelClosureLoadsOnlySelectedTypeSystems(MachineToolParts parts)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.None);
            var nodes = new NodeStateCollection();
            Assert.That(nodes.AddMachineToolTypeSystem(context, parts), Is.SameAs(nodes));

            Dictionary<NodeId, NodeState> indexed = ModelTestContext.IndexNodes(context, nodes);
            string?[] populatedNamespaces =
            [
                .. indexed.Keys.Select(id => context.NamespaceUris.GetString(id.NamespaceIndex))
                    .Distinct(StringComparer.Ordinal)
            ];
            Assert.That(populatedNamespaces, Is.EquivalentTo(ExpectedNamespaceUris(parts)));
            Assert.Multiple(() =>
            {
                Assert.That(ContainsType(indexed, context, ObjectTypeIds.MachineToolType),
                    Is.EqualTo((parts & MachineToolParts.MachineTool) != 0));
                Assert.That(ContainsType(indexed, context, WMTP.ObjectTypeIds.WirelessMachineToolPeripheralType),
                    Is.EqualTo((parts & MachineToolParts.Wmtp) != 0));
                Assert.That(context.NamespaceUris.GetIndex(MachineToolModels.MachineToolNamespaceUri) >= 0,
                    Is.EqualTo((parts & MachineToolParts.MachineTool) != 0));
                Assert.That(context.NamespaceUris.GetIndex(MachineToolModels.WmtpNamespaceUri) >= 0,
                    Is.EqualTo((parts & MachineToolParts.Wmtp) != 0));
            });
        }

        /// <summary>
        /// Default registrations load MachineTool without opting GMS or other consumers into WMTP.
        /// </summary>
        [Test]
        public void DefaultSelectionDoesNotLoadWmtp()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.None);
            var nodes = new NodeStateCollection();
            nodes.AddMachineToolTypeSystem(context);
            Assert.Multiple(() =>
            {
                Assert.That(MachineToolModels.GetNamespaceUris().ToArray(),
                    Is.EqualTo(ExpectedNamespaceUris(MachineToolParts.MachineTool)));
                Assert.That(nodes.Any(node => node.NodeId == NodeId.Create(
                    ObjectTypes.MachineToolType, MachineToolModels.MachineToolNamespaceUri, context.NamespaceUris)),
                    Is.True);
                Assert.That(context.NamespaceUris.GetIndex(MachineToolModels.WmtpNamespaceUri), Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// Omitting the selection argument on either factory overload does not opt into WMTP.
        /// </summary>
        [Test]
        public void DefaultFactoriesSelectMachineToolOnly()
        {
            ServiceMessageContext message = ModelTestContext.CreateMessageContext(MachineToolParts.None);
            message.Factory.Builder.AddMachineToolTypeSystem().Commit();
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.All);
            var factory = new NodeStateFactory();
            factory.AddMachineToolTypeSystem();
            NodeState? machine = factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Machine"), ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(ObjectTypeIds.MachineToolType, context.NamespaceUris));
            NodeState? peripheral = factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Peripheral"), ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(WMTP.ObjectTypeIds.WirelessMachineToolPeripheralType, context.NamespaceUris));
            Assert.Multiple(() =>
            {
                Assert.That(machine, Is.TypeOf<MachineToolState>());
                Assert.That(peripheral, Is.TypeOf<BaseObjectState>());
                Assert.That(message.Factory.TryGetEnumeratedType(DataTypeIds.MachineOperationMode, out _), Is.True);
                Assert.That(message.Factory.ContainsEncodeableType(WMTP.DataTypeIds.WMTPOutputDataType), Is.False);
            });
        }

        /// <summary>
        /// Either composition order retains already-loaded dependency nodes and avoids duplicate NodeIds.
        /// </summary>
        [TestCase(MachineToolParts.MachineTool, MachineToolParts.Wmtp)]
        [TestCase(MachineToolParts.Wmtp, MachineToolParts.MachineTool)]
        public void RepeatedAndComposedLoadingPreservesExistingNodes(
            MachineToolParts first,
            MachineToolParts second)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.All);
            var nodes = new NodeStateCollection();
            nodes.AddOpcUaDi(context);
            NodeState[] dependencyNodes = [.. nodes];
            nodes.AddMachineToolTypeSystem(context, first);
            NodeState[] firstNodes = [.. nodes];
            nodes.AddMachineToolTypeSystem(context, second);
            NodeState[] combinedNodes = [.. nodes];
            nodes.AddMachineToolTypeSystem(context, MachineToolParts.All);

            Assert.Multiple(() =>
            {
                Assert.That(nodes, Has.Count.EqualTo(combinedNodes.Length));
                Assert.That(nodes.Select(node => node.NodeId), Is.Unique);
                foreach (NodeState original in dependencyNodes.Concat(firstNodes).Concat(combinedNodes))
                {
                    Assert.That(nodes.Single(node => node.NodeId == original.NodeId), Is.SameAs(original));
                }
                Assert.That(ContainsType(
                    ModelTestContext.IndexNodes(context, nodes), context, ObjectTypeIds.MachineToolType), Is.True);
                Assert.That(ContainsType(
                    ModelTestContext.IndexNodes(context, nodes), context,
                    WMTP.ObjectTypeIds.WirelessMachineToolPeripheralType), Is.True);
            });
        }

        /// <summary>
        /// Selection controls real factory behavior even when both namespaces have entries in the table.
        /// </summary>
        [TestCase(MachineToolParts.None)]
        [TestCase(MachineToolParts.MachineTool)]
        [TestCase(MachineToolParts.Wmtp)]
        [TestCase(MachineToolParts.All)]
        public void NodeStateFactoriesRespectIndependentSelection(MachineToolParts parts)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.All);
            var factory = new NodeStateFactory();
            Assert.That(factory.AddMachineToolTypeSystem(parts), Is.SameAs(factory));
            NodeState? machine = factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Machine"),
                ReferenceTypeIds.HasComponent, ExpandedNodeId.ToNodeId(ObjectTypeIds.MachineToolType,
                    context.NamespaceUris));
            NodeState? peripheral = factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Peripheral"),
                ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(WMTP.ObjectTypeIds.WirelessMachineToolPeripheralType,
                    context.NamespaceUris));
            NodeState? measurement = factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Measurement"),
                ReferenceTypeIds.HasComponent,
                ExpandedNodeId.ToNodeId(Machinery.ProcessValues.ObjectTypeIds.ProcessValueType,
                    context.NamespaceUris));
            Assert.Multiple(() =>
            {
                Assert.That(machine is MachineToolState,
                    Is.EqualTo((parts & MachineToolParts.MachineTool) != 0));
                Assert.That(peripheral is WirelessMachineToolPeripheralState,
                    Is.EqualTo((parts & MachineToolParts.Wmtp) != 0));
                Assert.That(measurement is Machinery.ProcessValues.ProcessValueState,
                    Is.EqualTo((parts & MachineToolParts.Wmtp) != 0));
            });
        }

        /// <summary>
        /// Encodeable selection includes the required ISA95 structures or WMTP output, but not both implicitly.
        /// </summary>
        [TestCase(MachineToolParts.None)]
        [TestCase(MachineToolParts.MachineTool)]
        [TestCase(MachineToolParts.Wmtp)]
        [TestCase(MachineToolParts.All)]
        public void EncodeableFactoriesRespectIndependentSelection(MachineToolParts parts)
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(MachineToolParts.None);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            Assert.That(builder.AddMachineToolTypeSystem(parts), Is.SameAs(builder));
            Assert.That(context.Factory.ContainsEncodeableType(WMTP.DataTypeIds.WMTPOutputDataType), Is.False,
                "The loader must not commit the caller's builder.");
            builder.Commit();
            Assert.Multiple(() =>
            {
                Assert.That(context.Factory.ContainsEncodeableType(WMTP.DataTypeIds.WMTPOutputDataType),
                    Is.EqualTo((parts & MachineToolParts.Wmtp) != 0));
                Assert.That(context.Factory.ContainsEncodeableType(
                    ISA95.JobControl.V2.DataTypeIds.ISA95JobOrderDataType),
                    Is.EqualTo((parts & MachineToolParts.MachineTool) != 0));
            });
        }

        /// <summary>
        /// None leaves model, namespace and factory state completely untouched.
        /// </summary>
        [Test]
        public void NoneSelectionIsANoOp()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.None);
            var nodes = new NodeStateCollection();
            int namespaceCount = context.NamespaceUris.Count;
            var encodeables = new Mock<IEncodeableFactoryBuilder>(MockBehavior.Strict);
            var nodeFactory = new Mock<INodeStateFactoryBuilder>(MockBehavior.Strict);

            Assert.Multiple(() =>
            {
                Assert.That(MachineToolModels.GetNamespaceUris(MachineToolParts.None).IsEmpty, Is.True);
                Assert.That(nodes.AddMachineToolTypeSystem(context, MachineToolParts.None), Is.SameAs(nodes));
                Assert.That(encodeables.Object.AddMachineToolTypeSystem(MachineToolParts.None),
                    Is.SameAs(encodeables.Object));
                Assert.That(nodeFactory.Object.AddMachineToolTypeSystem(MachineToolParts.None),
                    Is.SameAs(nodeFactory.Object));
                Assert.That(nodes, Is.Empty);
                Assert.That(context.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            });
            encodeables.VerifyNoOtherCalls();
            nodeFactory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Undefined flags are rejected before any namespace, node or collaborator is mutated.
        /// </summary>
        [TestCase((MachineToolParts)4)]
        [TestCase((MachineToolParts)7)]
        [TestCase((MachineToolParts)(-1))]
        public void InvalidSelectionsAreRejectedBeforeMutation(MachineToolParts parts)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.None);
            var nodes = new NodeStateCollection();
            int namespaceCount = context.NamespaceUris.Count;
            var encodeables = new Mock<IEncodeableFactoryBuilder>(MockBehavior.Strict);
            var nodeFactory = new Mock<INodeStateFactoryBuilder>(MockBehavior.Strict);

            Assert.Multiple(() =>
            {
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
                    () => MachineToolModels.GetNamespaceUris(parts))!.ParamName, Is.EqualTo("parts"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
                    () => nodes.AddMachineToolTypeSystem(context, parts))!.ParamName, Is.EqualTo("parts"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
                    () => encodeables.Object.AddMachineToolTypeSystem(parts))!.ParamName, Is.EqualTo("parts"));
                Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
                    () => nodeFactory.Object.AddMachineToolTypeSystem(parts))!.ParamName, Is.EqualTo("parts"));
                Assert.That(nodes, Is.Empty);
                Assert.That(context.NamespaceUris.Count, Is.EqualTo(namespaceCount));
            });
            encodeables.VerifyNoOtherCalls();
            nodeFactory.VerifyNoOtherCalls();
        }

        /// <summary>
        /// All handwritten loader entry points reject null collaborators consistently, including None.
        /// </summary>
        [TestCase(MachineToolParts.None)]
        [TestCase(MachineToolParts.All)]
        public void LoadersRejectNullArguments(MachineToolParts parts)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(MachineToolParts.None);
            var nodes = new NodeStateCollection();
            Assert.Multiple(() =>
            {
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((NodeStateCollection)null!).AddMachineToolTypeSystem(context, parts))!.ParamName,
                    Is.EqualTo("nodes"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => nodes.AddMachineToolTypeSystem(null!, parts))!.ParamName, Is.EqualTo("context"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((IEncodeableFactoryBuilder)null!).AddMachineToolTypeSystem(parts))!.ParamName,
                    Is.EqualTo("builder"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((INodeStateFactoryBuilder)null!).AddMachineToolTypeSystem(parts))!.ParamName,
                    Is.EqualTo("builder"));
            });
        }

        private static bool ContainsType(
            Dictionary<NodeId, NodeState> nodes,
            SystemContext context,
            ExpandedNodeId typeId)
        {
            return nodes.TryGetValue(ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris), out NodeState? node) &&
                node.NodeClass == NodeClass.ObjectType;
        }

        private static string[] ExpectedNamespaceUris(MachineToolParts parts)
        {
            string[] common =
            [
                "http://opcfoundation.org/UA/DI/",
                "http://opcfoundation.org/UA/IA/",
                "http://opcfoundation.org/UA/Machinery/"
            ];
            string[] machine =
            [
                "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/",
                "http://opcfoundation.org/UA/Machinery/Jobs/",
                "http://opcfoundation.org/UA/MachineTool/"
            ];
            string[] wmtp =
            [
                "http://opcfoundation.org/UA/Dictionary/IRDI",
                "http://opcfoundation.org/UA/PADIM/",
                "http://opcfoundation.org/UA/Machinery/ProcessValues/",
                "http://opcfoundation.org/UA/WMTP/"
            ];
            return parts switch
            {
                MachineToolParts.MachineTool => [.. common, .. machine],
                MachineToolParts.Wmtp => [.. common, .. wmtp],
                MachineToolParts.All => [.. common, .. machine, .. wmtp],
                _ => []
            };
        }
    }
}
