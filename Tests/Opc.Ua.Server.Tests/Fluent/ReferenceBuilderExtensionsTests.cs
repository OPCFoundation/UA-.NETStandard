/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="ReferenceBuilderExtensions"/> — fluent
    /// <c>Organizes</c> / <c>HasComponent</c> / <c>AddReference</c> /
    /// <c>AddObject</c> helpers.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class ReferenceBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        private static (NodeManagerBuilder Builder, BaseObjectState Root,
            BaseDataVariableState Target1, BaseDataVariableState Target2)
            CreateBuilder()
        {
            SystemContext ctx = CreateContext();

            var root = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId("Root", kNs),
                BrowseName = new QualifiedName("Root", kNs),
                DisplayName = new LocalizedText("Root")
            };

            var target1 = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("Target1", kNs),
                BrowseName = new QualifiedName("Target1", kNs),
                DisplayName = new LocalizedText("Target1"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            var target2 = new BaseDataVariableState(parent: null)
            {
                NodeId = new NodeId("Target2", kNs),
                BrowseName = new QualifiedName("Target2", kNs),
                DisplayName = new LocalizedText("Target2"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };

            var roots = new Dictionary<QualifiedName, NodeState> { [root.BrowseName] = root };
            var byId = new Dictionary<NodeId, NodeState>
            {
                [root.NodeId] = root,
                [target1.NodeId] = target1,
                [target2.NodeId] = target2
            };

            var builder = new NodeManagerBuilder(
                ctx,
                nodeManager: Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState? n) ? n! : null!,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                typeIdResolver: _ => []);

            return (builder, root, target1, target2);
        }

        [Test]
        public void OrganizesAddsForwardReference()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.Organizes(t1.NodeId);

            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.Organizes, isInverse: false, t1.NodeId),
                Is.True);
        }

        [Test]
        public void OrganizesNodeStateOverloadAddsReference()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.Organizes(t1);

            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.Organizes, isInverse: false, t1.NodeId),
                Is.True);
        }

        [Test]
        public void HasComponentAddsForwardReference()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.HasComponent(t1.NodeId);

            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.HasComponent, isInverse: false, t1.NodeId),
                Is.True);
        }

        [Test]
        public void HasPropertyAddsForwardReference()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.HasProperty(t1.NodeId);

            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.HasProperty, isInverse: false, t1.NodeId),
                Is.True);
        }

        [Test]
        public void AddReferenceChainsMultipleTargets()
        {
            (NodeManagerBuilder b, BaseObjectState root,
                BaseDataVariableState t1, BaseDataVariableState t2) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.Organizes(t1.NodeId).Organizes(t2.NodeId);

            Assert.That(root.ReferenceExists(ReferenceTypeIds.Organizes, false, t1.NodeId), Is.True);
            Assert.That(root.ReferenceExists(ReferenceTypeIds.Organizes, false, t2.NodeId), Is.True);
        }

        [Test]
        public void AddReferenceInverseFlagIsRespected()
        {
            (NodeManagerBuilder b, BaseObjectState root, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            nb.AddReference(ReferenceTypeIds.HasNotifier, isInverse: true, t1.NodeId);

            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.HasNotifier, isInverse: true, t1.NodeId),
                Is.True);
            Assert.That(
                root.ReferenceExists(ReferenceTypeIds.HasNotifier, isInverse: false, t1.NodeId),
                Is.False);
        }

        [Test]
        public void OrganizesNullTargetThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentNullException>(() => nb.Organizes(NodeId.Null));
            Assert.Throws<ArgumentNullException>(() => nb.Organizes(null!));
        }

        [Test]
        public void AddObjectCreatesAndAttachesChild()
        {
            (NodeManagerBuilder b, BaseObjectState root, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            Assert.That(child.Node, Is.Not.Null);
            Assert.That(child.Node.BrowseName, Is.EqualTo(new QualifiedName("Group1", kNs)));
            Assert.That(child.Node.Parent, Is.SameAs(root));
            Assert.That(child.Node.NodeId.IdentifierAsString,
                Is.EqualTo("Root_Group1"),
                "Generated NodeId should follow parentId_childName pattern.");
            Assert.That(child.Node.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.BaseObjectType));

            var children = new List<BaseInstanceState>();
            root.GetChildren(null!, children);
            Assert.That(children, Has.Member(child.Node));
        }

        [Test]
        public void AddObjectWithCustomTypeDefinition()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            var customType = new NodeId(1234u, kNs);

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("CustomGroup", kNs),
                customType);

            Assert.That(child.Node.TypeDefinitionId, Is.EqualTo(customType));
        }

        [Test]
        public void AddObjectThenOrganizesWiresFunctionalGroup()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState t1, BaseDataVariableState t2) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> group = nb.AddObject(
                new QualifiedName("Operational", kNs));
            group.Organizes(t1).Organizes(t2);

            Assert.That(group.Node.ReferenceExists(
                ReferenceTypeIds.Organizes, false, t1.NodeId), Is.True);
            Assert.That(group.Node.ReferenceExists(
                ReferenceTypeIds.Organizes, false, t2.NodeId), Is.True);
        }

        [Test]
        public void AddObjectNullBrowseNameThrowsArgumentNullException()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentNullException>(
                () => nb.AddObject(QualifiedName.Null));
        }

        [Test]
        public void AddReferenceNullArgsThrow()
        {
            (NodeManagerBuilder b, _, BaseDataVariableState t1, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            Assert.Throws<ArgumentNullException>(
                () => nb.AddReference(NodeId.Null, false, t1.NodeId));
            Assert.Throws<ArgumentNullException>(
                () => nb.AddReference(ReferenceTypeIds.Organizes, false, NodeId.Null));
        }

        [Test]
        public void AddReferenceNullBuilderThrowsArgumentNullException()
        {
            (_, _, BaseDataVariableState t1, _) = CreateBuilder();
            Assert.Throws<ArgumentNullException>(
                () => ((INodeBuilder)null!).AddReference(
                    ReferenceTypeIds.Organizes, false, t1.NodeId));
        }

        [Test]
        public void AddObjectNullParentThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => ((INodeBuilder)null!).AddObject(new QualifiedName("X", kNs)));
        }

        [Test]
        public void AddObjectReturnedBuilderExposesNodeAndOwnerBuilder()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            Assert.That(child.Builder, Is.SameAs(b));
            Assert.That(((INodeBuilder)child).Node, Is.SameAs(child.Node));
        }

        [Test]
        public void AddObjectAsCastsToBaseType()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));
            INodeBuilder<NodeState> upcast = child.As<NodeState>();

            Assert.That(upcast.Node, Is.SameAs(child.Node));
        }

        [Test]
        public void AddObjectAsThrowsOnTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => child.As<BaseDataVariableState>())!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void AddObjectOnReadOnNonVariableThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => child.OnRead((c, n, ref v) => ServiceResult.Good))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddObjectOnCallOnNonMethodThrowsBadInvalidArgument()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => child.OnCall((_, _, _, _, _) => ServiceResult.Good))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddObjectChildMissingThrowsBadNodeIdUnknown()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => child.Child(new QualifiedName("DoesNotExist", kNs)))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void AddObjectVariableOnNonVariableChildThrowsBadTypeMismatch()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> parent = nb.AddObject(
                new QualifiedName("Group1", kNs));
            // Attach a non-variable child.
            parent.AddObject(new QualifiedName("SubObj", kNs));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => parent.Variable<int>(new QualifiedName("SubObj", kNs)))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void AddObjectOnNodeAddedInvokesHandlerSynchronously()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));

            INodeBuilder<BaseObjectState> child = nb.AddObject(
                new QualifiedName("Group1", kNs));

            NodeState? captured = null;
            child.OnNodeAdded((_, n) => captured = n);

            Assert.That(captured, Is.SameAs(child.Node));
        }

        [Test]
        public void AddObjectAdHocChildBuildersWireCallbacksAndNoOpManagerHooks()
        {
            (NodeManagerBuilder b, _, _, _) = CreateBuilder();
            INodeBuilder nb = b.Node(new NodeId("Root", kNs));
            INodeBuilder<BaseObjectState> parent = nb.AddObject(new QualifiedName("Group1", kNs));
            var variable = new BaseDataVariableState(parent.Node)
            {
                NodeId = new NodeId("Group1.Variable", kNs),
                BrowseName = new QualifiedName("Variable", kNs),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            var method = new MethodState(parent.Node)
            {
                NodeId = new NodeId("Group1.Method", kNs),
                BrowseName = new QualifiedName("Method", kNs)
            };
            parent.Node.AddChild(variable);
            parent.Node.AddChild(method);

            INodeBuilder<BaseDataVariableState> variableBuilder =
                parent.Child<BaseDataVariableState>(variable.BrowseName);
            INodeBuilder<MethodState> methodBuilder = parent.Child<MethodState>(method.BrowseName);

            static ServiceResult read(
                ISystemContext c,
                NodeState n,
                NumericRange range,
                QualifiedName encoding,
                ref Variant value,
                ref StatusCode statusCode,
                ref DateTimeUtc timestamp) =>
                ServiceResult.Good;
            static ValueTask<AttributeWriteResult> writeAsync(
                ISystemContext c,
                NodeState n,
                Variant value,
                CancellationToken ct) =>
                new(new AttributeWriteResult(ServiceResult.Good));
            static ValueTask<ServiceResult> callAsync(
                ISystemContext c,
                MethodState m,
                NodeId objectId,
                ArrayOf<Variant> inputArguments,
                List<Variant> outputArguments,
                CancellationToken ct) =>
                new(ServiceResult.Good);

            variableBuilder.OnRead(read)
                .OnWrite(writeAsync)
                .OnHistoryRead((c, n, d, t, r, ntr, res) => ServiceResult.Good)
                .OnHistoryUpdate((c, n, details, result) => ServiceResult.Good)
                .OnConditionRefresh((c, n, events) => { })
                .OnMonitoredItemCreated((c, n, item) => { })
                .OnNodeRemoved((c, n) => { });
            methodBuilder.OnCall(callAsync);
            parent.OnEvent((c, n, e) => { });
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => parent.AllowMultipleEventConsumers(enable: false))!;

            Assert.That(variable.OnReadValue, Is.SameAs((NodeValueEventHandler)read));
            Assert.That(variable.OnSimpleWriteValueAsync, Is.SameAs(
                (NodeValueSimpleWriteEventHandlerAsync)writeAsync));
            Assert.That(method.OnCallMethod2Async, Is.SameAs((GenericMethodCalledEventHandler2Async)callAsync));
            Assert.That(parent.Node.OnReportEvent, Is.Not.Null);
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }
    }
}
