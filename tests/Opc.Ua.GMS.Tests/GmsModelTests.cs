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
using NUnit.Framework;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies dependency-aware model registration without runtime or unrelated WMTP features.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsModelTests
    {
        /// <summary>
        /// The GMS closure includes Machinery Result and the selected MachineTool version's job dependencies.
        /// </summary>
        [Test]
        public void NamespaceClosureSelectsMachineToolAndResultsWithoutWmtp()
        {
            Assert.That(GmsModel.GetNamespaceUris().ToArray(),
                Is.EqualTo(ModelTestContext.ExpectedNamespaceUris.ToArray()));
        }

        /// <summary>
        /// Actual declarations load in all required namespaces even if those namespaces were pre-registered backwards.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void LoaderMaterializesExactlyTheRequiredNamespaceClosure(bool reverseNamespaces)
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: reverseNamespaces);
            var nodes = new NodeStateCollection();
            Assert.That(nodes.AddGmsTypeSystem(context), Is.SameAs(nodes));
            Dictionary<NodeId, NodeState> indexed = ModelTestContext.IndexNodes(context, nodes);
            string?[] populated =
            [
                .. indexed.Keys.Select(id => context.NamespaceUris.GetString(id.NamespaceIndex))
                    .Distinct(StringComparer.Ordinal)
            ];
            Assert.Multiple(() =>
            {
                Assert.That(populated, Is.EquivalentTo(ModelTestContext.ExpectedNamespaceUris.ToArray()!));
                Assert.That(indexed.ContainsKey(NodeId.Create(1002, GmsModel.NamespaceUri, context.NamespaceUris)),
                    Is.True);
                Assert.That(indexed.ContainsKey(ExpandedNodeId.ToNodeId(
                    MachineTool.ObjectTypeIds.MachineToolType, context.NamespaceUris)), Is.True);
                Assert.That(indexed.ContainsKey(ExpandedNodeId.ToNodeId(
                    Machinery.Jobs.ObjectTypeIds.JobManagementType, context.NamespaceUris)), Is.True);
                Assert.That(indexed.ContainsKey(ExpandedNodeId.ToNodeId(
                    ISA95.JobControl.V2.DataTypeIds.ISA95JobOrderDataType, context.NamespaceUris)), Is.True);
                Assert.That(indexed.ContainsKey(ExpandedNodeId.ToNodeId(
                    Machinery.Result.ObjectTypeIds.ResultManagementType, context.NamespaceUris)), Is.True);
                Assert.That(context.NamespaceUris.GetIndex(MachineToolModels.WmtpNamespaceUri), Is.EqualTo(-1));
                Assert.That(context.NamespaceUris.GetIndex(PADIM.Namespaces.PADIM), Is.EqualTo(-1));
                Assert.That(context.NamespaceUris.GetIndex(Machinery.ProcessValues.Namespaces.MachineryProcessValues),
                    Is.EqualTo(-1));
                Assert.That(context.NamespaceUris.GetIndex(IRDI.Namespaces.IRDI), Is.EqualTo(-1));
            });
        }

        /// <summary>
        /// Loading into a core context registers its complete namespace closure rather than relying on the caller.
        /// </summary>
        [Test]
        public void LoaderRegistersMissingNamespaces()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(registerModel: false);
            new NodeStateCollection().AddGmsTypeSystem(context);
            Assert.That(context.NamespaceUris.ToArray().Skip(3),
                Is.EqualTo(ModelTestContext.ExpectedNamespaceUris.ToArray()));
        }

        /// <summary>
        /// Existing dependency nodes survive repeated and composed registration by identity, without duplicate roots.
        /// </summary>
        [Test]
        public void RepeatedLoadingRetainsExistingDependencyAndGmsNodes()
        {
            SystemContext context = ModelTestContext.CreateSystemContext();
            var nodes = new NodeStateCollection();
            nodes.AddMachineToolTypeSystem(context).AddOpcUaMachineryResult(context);
            NodeState[] dependencies = [.. nodes];
            nodes.AddGmsTypeSystem(context);
            NodeState[] combined = [.. nodes];
            nodes.AddGmsTypeSystem(context);

            Assert.Multiple(() =>
            {
                Assert.That(nodes, Has.Count.EqualTo(combined.Length));
                Assert.That(nodes.Select(node => node.NodeId), Is.Unique);
                foreach (NodeState original in dependencies.Concat(combined))
                {
                    Assert.That(nodes.Single(node => node.NodeId == original.NodeId), Is.SameAs(original));
                }
            });
        }

        /// <summary>
        /// Encodeable registration waits for the caller's commit and never adds WMTP output data.
        /// </summary>
        [Test]
        public void EncodeableLoaderRegistersGmsAndRequiredStructuresOnly()
        {
            ServiceMessageContext context = ModelTestContext.CreateMessageContext(registerModel: false);
            IEncodeableFactoryBuilder builder = context.Factory.Builder;
            Assert.That(builder.AddGmsTypeSystem(), Is.SameAs(builder));
            Assert.That(context.Factory.ContainsEncodeableType(DataTypeIds.CartesianWorkspaceType), Is.False);
            builder.Commit();

            Assert.Multiple(() =>
            {
                Assert.That(context.Factory.ContainsEncodeableType(DataTypeIds.CartesianWorkspaceType), Is.True);
                Assert.That(context.Factory.ContainsEncodeableType(DataTypeIds.CylindricalWorkspaceType), Is.True);
                Assert.That(context.Factory.ContainsEncodeableType(Machinery.Result.DataTypeIds.ResultDataType),
                    Is.True);
                Assert.That(context.Factory.ContainsEncodeableType(
                    ISA95.JobControl.V2.DataTypeIds.ISA95JobOrderDataType), Is.True);
                Assert.That(context.Factory.TryGetEnumeratedType(MachineTool.DataTypeIds.MachineOperationMode, out _),
                    Is.True);
                Assert.That(context.Factory.ContainsEncodeableType(
                    MachineTool.WMTP.DataTypeIds.WMTPOutputDataType), Is.False);
            });
        }

        /// <summary>
        /// Typed factories preserve inheritance while unrelated types still use the generic fallback.
        /// </summary>
        [Test]
        public void NodeFactoryRegistersGmsAndDependenciesWithoutWmtp()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(reverseNamespaces: true);
            context.NamespaceUris.GetIndexOrAppend(MachineToolModels.WmtpNamespaceUri);
            var factory = new NodeStateFactory();
            Assert.That(factory.AddGmsTypeSystem(), Is.SameAs(factory));
            Assert.Multiple(() =>
            {
                Assert.That(CreateObject(factory, context, ObjectTypeIds.GMSType), Is.TypeOf<GMSState>());
                Assert.That(CreateObject(factory, context, ObjectTypeIds.GMSResultManagementType),
                    Is.TypeOf<GMSResultManagementState>());
                Assert.That(CreateObject(factory, context, MachineTool.ObjectTypeIds.MachineToolType),
                    Is.TypeOf<MachineToolState>());
                Assert.That(CreateObject(factory, context, Machinery.Result.ObjectTypeIds.ResultManagementType),
                    Is.TypeOf<ResultManagementState>());
                Assert.That(CreateObject(factory, context, Machinery.Jobs.ObjectTypeIds.JobManagementType),
                    Is.TypeOf<Machinery.Jobs.JobManagementState>());
                Assert.That(CreateObject(
                    factory, context, MachineTool.WMTP.ObjectTypeIds.WirelessMachineToolPeripheralType),
                    Is.TypeOf<BaseObjectState>());
            });
        }

        /// <summary>
        /// Each handwritten registration entry point rejects null collaborators before any registration.
        /// </summary>
        [Test]
        public void LoadersRejectNullArguments()
        {
            SystemContext context = ModelTestContext.CreateSystemContext(registerModel: false);
            var nodes = new NodeStateCollection();
            int count = context.NamespaceUris.Count;
            Assert.Multiple(() =>
            {
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((NodeStateCollection)null!).AddGmsTypeSystem(context))!.ParamName, Is.EqualTo("nodes"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => nodes.AddGmsTypeSystem(null!))!.ParamName, Is.EqualTo("context"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((IEncodeableFactoryBuilder)null!).AddGmsTypeSystem())!.ParamName, Is.EqualTo("builder"));
                Assert.That(Assert.Throws<ArgumentNullException>(
                    () => ((INodeStateFactoryBuilder)null!).AddGmsTypeSystem())!.ParamName, Is.EqualTo("builder"));
                Assert.That(nodes, Is.Empty);
                Assert.That(context.NamespaceUris.Count, Is.EqualTo(count));
            });
        }

        private static NodeState? CreateObject(
            NodeStateFactory factory,
            SystemContext context,
            ExpandedNodeId typeId)
        {
            return factory.CreateInstance(
                context, null, NodeClass.Object, new QualifiedName("Instance"),
                ReferenceTypeIds.HasComponent, ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris));
        }
    }
}
