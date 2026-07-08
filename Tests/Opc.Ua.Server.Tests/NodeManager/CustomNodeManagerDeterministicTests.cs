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

// CA2000: test code; disposables are ownership-transferred to the test
// harness or are short-lived, making CA2000 noisy without a real leak risk.
#pragma warning disable CA2000

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests that drive the synchronous surface of
    /// <see cref="CustomNodeManager2"/> (the members implemented in
    /// CustomNodeManager.cs) directly against a fully mocked
    /// <see cref="IServerInternal"/>. No sockets, no sampling timers and no
    /// wall-clock assertions are used, so the tests are order independent and
    /// stable on every target framework.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("CustomNodeManagerDeterministic")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CustomNodeManagerDeterministicTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/CustomNodeManagerDeterministic/";

        [Test]
        public void CreateNode_AddsNodeToPredefinedNodesAndEmitsModelChange()
        {
            using Harness h = CreateHarness();
            ushort ns = h.NamespaceIndex;
            var instance = new BaseObjectState(null);
            // Opt into unconditional emission so the model-change event fires for a
            // node that carries no NodeVersion property (the default requires one).
            h.Manager.RequireNodeVersionForModelChange = false;

            NodeId resultId = h.Manager.CreateNode(
                h.Context,
                NodeId.Null,
                ReferenceTypeIds.Organizes,
                new QualifiedName("Created", ns),
                instance);

            Assert.That(resultId.IsNull, Is.False);
            Assert.That(resultId, Is.EqualTo(instance.NodeId));
            Assert.That(instance.BrowseName, Is.EqualTo(new QualifiedName("Created", ns)));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(resultId), Is.True);
            Assert.That(h.Manager.PredefinedNodes[resultId], Is.SameAs(instance));
            // The model-change event is emitted through Server.ReportEvent.
            h.MockServer.Verify(s => s.ReportEvent(It.IsAny<IFilterTarget>()), Times.AtLeastOnce);
        }

        [Test]
        public void CreateNode_WithUnknownParent_ThrowsBadNodeIdUnknown()
        {
            using Harness h = CreateHarness();
            ushort ns = h.NamespaceIndex;
            var child = new BaseObjectState(null)
            {
                NodeId = new NodeId("OrphanChild", ns)
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => h.Manager.CreateNode(
                    h.Context,
                    new NodeId("MissingParent", ns),
                    ReferenceTypeIds.Organizes,
                    new QualifiedName("OrphanChild", ns),
                    child))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void AddPredefinedNode_AddsNodeToPredefinedNodes()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewVariable("Added", 7);

            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            Assert.That(h.Manager.PredefinedNodes.ContainsKey(variable.NodeId), Is.True);
            Assert.That(h.Manager.PredefinedNodes[variable.NodeId], Is.SameAs(variable));
        }

        [Test]
        public void DeleteNode_RemovesKnownNodeAndReturnsTrue()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("ToDelete");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(node.NodeId), Is.True);

            bool result = h.Manager.DeleteNode(h.Context, node.NodeId);

            Assert.That(result, Is.True);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(node.NodeId), Is.False);
        }

        [Test]
        public void DeleteNode_ReturnsFalseForUnknownNode()
        {
            using Harness h = CreateHarness();

            bool result = h.Manager.DeleteNode(h.Context, new NodeId("Ghost", h.NamespaceIndex));

            Assert.That(result, Is.False);
        }

        [Test]
        public void CreateAddressSpace_LoadsNodesFromOverride()
        {
            using Harness h = CreateHarness();
            var folder = new FolderState(null);
            folder.CreateAsPredefinedNode(h.Context);
            folder.NodeId = new NodeId("Folder", h.NamespaceIndex);
            folder.BrowseName = new QualifiedName("Folder", h.NamespaceIndex);
            folder.DisplayName = new LocalizedText("Folder");
            h.Manager.NodesToLoad = [folder];

            h.Manager.CreateAddressSpace(new Dictionary<NodeId, IList<IReference>>());

            Assert.That(h.Manager.PredefinedNodes.ContainsKey(folder.NodeId), Is.True);
            Assert.That(h.Manager.PredefinedNodes[folder.NodeId], Is.SameAs(folder));
        }

        [Test]
        public void DeleteAddressSpace_ClearsPredefinedNodes()
        {
            using Harness h = CreateHarness();
            h.Manager.AddPredefinedNodePublic(h.Context, h.NewObject("A"));
            h.Manager.AddPredefinedNodePublic(h.Context, h.NewObject("B"));
            Assert.That(h.Manager.PredefinedNodes, Has.Count.EqualTo(2));

            h.Manager.DeleteAddressSpace();

            Assert.That(h.Manager.PredefinedNodes, Is.Empty);
        }

        [Test]
        public void FindPredefinedNodeGeneric_ReturnsNodeWhenTypeMatches()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewVariable("Find", 1);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            BaseDataVariableState? found = h.Manager.FindPredefinedNode<BaseDataVariableState>(variable.NodeId);

            Assert.That(found, Is.SameAs(variable));
        }

        [Test]
        public void FindPredefinedNodeGeneric_ReturnsNullWhenTypeMismatch()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("TypeMismatch");
            h.Manager.AddPredefinedNodePublic(h.Context, node);

            BaseDataVariableState? found = h.Manager.FindPredefinedNode<BaseDataVariableState>(node.NodeId);

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindPredefinedNodeGeneric_ReturnsNullWhenNotFound()
        {
            using Harness h = CreateHarness();

            NodeState? found = h.Manager.FindPredefinedNode<NodeState>(new NodeId("Unknown", h.NamespaceIndex));

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindPredefinedNodeGeneric_ReturnsNullForNullNodeId()
        {
            using Harness h = CreateHarness();

            NodeState? found = h.Manager.FindPredefinedNode<NodeState>(NodeId.Null);

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindPredefinedNodeObsolete_ChecksExpectedType()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("Obsolete");
            h.Manager.AddPredefinedNodePublic(h.Context, node);

#pragma warning disable CS0618, CA2263 // exercising the obsolete overload on purpose.
            NodeState? match = h.Manager.FindPredefinedNode(node.NodeId, typeof(BaseObjectState));
            NodeState? mismatch = h.Manager.FindPredefinedNode(node.NodeId, typeof(BaseDataVariableState));
            NodeState? missing = h.Manager.FindPredefinedNode(new NodeId("None", h.NamespaceIndex), typeof(NodeState));
#pragma warning restore CS0618, CA2263

            Assert.That(match, Is.SameAs(node));
            Assert.That(mismatch, Is.Null);
            Assert.That(missing, Is.Null);
        }

        [Test]
        public void GetManagerHandle_ReturnsValidatedHandleForKnownNode()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("Handle");
            h.Manager.AddPredefinedNodePublic(h.Context, node);

            object? handle = h.Manager.GetManagerHandle(node.NodeId);

            Assert.That(handle, Is.InstanceOf<NodeHandle>());
            var nodeHandle = (NodeHandle)handle!;
            Assert.That(nodeHandle.NodeId, Is.EqualTo(node.NodeId));
            Assert.That(nodeHandle.Node, Is.SameAs(node));
            Assert.That(nodeHandle.Validated, Is.True);
        }

        [Test]
        public void GetManagerHandle_ReturnsNullForUnknownNode()
        {
            using Harness h = CreateHarness();

            object? foreignHandle = h.Manager.GetManagerHandle(ObjectIds.Server);
            object? unknownHandle = h.Manager.GetManagerHandle(new NodeId("Unknown", h.NamespaceIndex));

            Assert.That(foreignHandle, Is.Null);
            Assert.That(unknownHandle, Is.Null);
        }

        [Test]
        public void Read_ReturnsValueForKnownNode()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewVariable("ReadVar", 42);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                new() { NodeId = ObjectIds.Server, AttributeId = Attributes.Value }
            };
            var values = new List<DataValue> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Read(h.NewContext(RequestType.Read), 0, nodesToRead, values, errors);

            Assert.That(nodesToRead[0].Processed, Is.True);
            Assert.That(nodesToRead[1].Processed, Is.False);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That((int)values[0].WrappedValue, Is.EqualTo(42));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(values[0].ServerTimestamp, Is.EqualTo(values[0].SourceTimestamp));
        }

        [Test]
        public void Read_ReturnsBrowseNameForNonValueAttribute()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewVariable("MetaVar", 1);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = variable.NodeId, AttributeId = Attributes.BrowseName }
            };
            var values = new List<DataValue> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Read(h.NewContext(RequestType.Read), 0, nodesToRead, values, errors);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            var browseName = (QualifiedName)values[0].WrappedValue;
            Assert.That(browseName, Is.EqualTo(new QualifiedName("MetaVar", h.NamespaceIndex)));
        }

        [Test]
        public void Read_ReturnsBadAttributeIdInvalidForInvalidAttribute()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("ReadObject");
            h.Manager.AddPredefinedNodePublic(h.Context, node);

            var nodesToRead = new List<ReadValueId>
            {
                // Value is not a valid attribute of an Object node.
                new() { NodeId = node.NodeId, AttributeId = Attributes.Value }
            };
            var values = new List<DataValue> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Read(h.NewContext(RequestType.Read), 0, nodesToRead, values, errors);

            Assert.That(nodesToRead[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
        }

        [Test]
        public void Read_ReturnsBadNodeIdUnknownForPlaceholderNode()
        {
            using Harness h = CreatePlaceholderHarness(out PlaceholderNodeManager manager);
            manager.PlaceholderNodeId = new NodeId("Ghost", h.NamespaceIndex);

            var nodesToRead = new List<ReadValueId>
            {
                new() { NodeId = manager.PlaceholderNodeId, AttributeId = Attributes.Value }
            };
            var values = new List<DataValue> { new() };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            manager.Read(h.NewContext(RequestType.Read), 0, nodesToRead, values, errors);

            Assert.That(nodesToRead[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void Write_WritesValueToKnownNode()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewWritableVariable("WriteVar", 0);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToWrite = new List<WriteValue>
            {
                new() {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(99))
                },
                new() {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(99))
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Write(h.NewContext(RequestType.Write), nodesToWrite, errors);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(nodesToWrite[1].Processed, Is.False);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(variable.Value, Is.EqualTo(99));
        }

        [Test]
        public void Write_ReturnsBadNotWritableForReadOnlyNode()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewVariable("ReadOnlyVar", 0);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToWrite = new List<WriteValue>
            {
                new() {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(5))
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Write(h.NewContext(RequestType.Write), nodesToWrite, errors);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void Write_ReturnsBadWriteNotSupportedForNonValueAttributeWithIndexRange()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewWritableVariable("IdxRangeVar", 0);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToWrite = new List<WriteValue>
            {
                new() {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.DisplayName,
                    IndexRange = "0",
                    Value = new DataValue(new Variant(new LocalizedText("X")))
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Write(h.NewContext(RequestType.Write), nodesToWrite, errors);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadWriteNotSupported));
        }

        [Test]
        public void Write_ReturnsBadTypeMismatchForWrongValueType()
        {
            using Harness h = CreateHarness();
            BaseDataVariableState variable = h.NewWritableVariable("TypeVar", 0);
            h.Manager.AddPredefinedNodePublic(h.Context, variable);

            var nodesToWrite = new List<WriteValue>
            {
                new() {
                    NodeId = variable.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant("not-an-int"))
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            h.Manager.Write(h.NewContext(RequestType.Write), nodesToWrite, errors);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void Write_ReturnsBadNodeIdUnknownForPlaceholderNode()
        {
            using Harness h = CreatePlaceholderHarness(out PlaceholderNodeManager manager);
            manager.PlaceholderNodeId = new NodeId("Ghost", h.NamespaceIndex);

            var nodesToWrite = new List<WriteValue>
            {
                new() {
                    NodeId = manager.PlaceholderNodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(1))
                }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };

            manager.Write(h.NewContext(RequestType.Write), nodesToWrite, errors);

            Assert.That(nodesToWrite[0].Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void Browse_ReturnsChildReferences()
        {
            using Harness h = CreateHarness();
            (BaseObjectState parent, BaseObjectState child) = h.AddParentWithChild(ReferenceTypeIds.Organizes);
            object handle = h.Manager.GetManagerHandle(parent.NodeId)!;

            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);
            var references = new List<ReferenceDescription>();

            h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references);

            Assert.That(continuationPoint, Is.Null);
            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].BrowseName, Is.EqualTo(child.BrowseName));
            Assert.That(references[0].NodeId, Is.EqualTo(new ExpandedNodeId(child.NodeId)));
        }

        [Test]
        public void Browse_WithMaxResultsToReturn_LeavesContinuationPoint()
        {
            using Harness h = CreateHarness();
            BaseObjectState parent = h.NewObject("BrowseParent");
            h.Manager.AddPredefinedNodePublic(h.Context, parent);
            BaseObjectState child1 = h.NewObject("BrowseChild1");
            BaseObjectState child2 = h.NewObject("BrowseChild2");
            h.Manager.AddPredefinedNodePublic(h.Context, child1);
            h.Manager.AddPredefinedNodePublic(h.Context, child2);
            parent.AddReference(ReferenceTypeIds.Organizes, false, child1.NodeId);
            parent.AddReference(ReferenceTypeIds.Organizes, false, child2.NodeId);
            object handle = h.Manager.GetManagerHandle(parent.NodeId)!;

            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);
            continuationPoint.MaxResultsToReturn = 1;
            var references = new List<ReferenceDescription>();

            h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references);

            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(continuationPoint, Is.Not.Null);
            continuationPoint.Dispose();
        }

        [Test]
        public void Browse_WithReferenceTypeFilter_ExcludesNonMatchingReferences()
        {
            using Harness h = CreateHarness();
            (BaseObjectState parent, _) = h.AddParentWithChild(ReferenceTypeIds.Organizes);
            object handle = h.Manager.GetManagerHandle(parent.NodeId)!;

            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);
            // The child is linked with Organizes only, so a HasComponent filter
            // (without subtypes) must return no references.
            continuationPoint.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            continuationPoint.IncludeSubtypes = false;
            var references = new List<ReferenceDescription>();

            h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references);

            Assert.That(references, Is.Empty);
        }

        [Test]
        public void Browse_WithInverseDirection_ReturnsParentReference()
        {
            using Harness h = CreateHarness();
            BaseObjectState parent = h.NewObject("InverseParent");
            BaseObjectState child = h.NewObject("InverseChild");
            h.Manager.AddPredefinedNodePublic(h.Context, parent);
            h.Manager.AddPredefinedNodePublic(h.Context, child);
            parent.AddReference(ReferenceTypeIds.Organizes, false, child.NodeId);
            child.AddReference(ReferenceTypeIds.Organizes, true, parent.NodeId);
            object handle = h.Manager.GetManagerHandle(child.NodeId)!;

            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);
            continuationPoint.BrowseDirection = BrowseDirection.Inverse;
            var references = new List<ReferenceDescription>();

            h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references);

            Assert.That(references, Has.Count.EqualTo(1));
            Assert.That(references[0].NodeId, Is.EqualTo(new ExpandedNodeId(parent.NodeId)));
        }

        [Test]
        public void Browse_NullContinuationPoint_ThrowsArgumentNullException()
        {
            using Harness h = CreateHarness();
            ContinuationPoint continuationPoint = null!;
            var references = new List<ReferenceDescription>();

            Assert.Throws<ArgumentNullException>(
                () => h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references));
        }

        [Test]
        public void Browse_NullReferences_ThrowsArgumentNullException()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("BrowseNullRefs");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            object handle = h.Manager.GetManagerHandle(node.NodeId)!;
            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);

            Assert.Throws<ArgumentNullException>(
                () => h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, null!));

            continuationPoint.Dispose();
        }

        [Test]
        public void Browse_ForeignHandle_ThrowsBadNodeIdUnknown()
        {
            using Harness h = CreateHarness();
            var foreignHandle = new NodeHandle { NodeId = ObjectIds.Server };
            ContinuationPoint continuationPoint = h.NewContinuationPoint(foreignHandle);
            var references = new List<ReferenceDescription>();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void Browse_UnknownView_ThrowsBadViewIdUnknown()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("BrowseView");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            object handle = h.Manager.GetManagerHandle(node.NodeId)!;

            ContinuationPoint continuationPoint = h.NewContinuationPoint(handle);
            continuationPoint.View = new ViewDescription { ViewId = new NodeId("NoView", h.NamespaceIndex) };
            var references = new List<ReferenceDescription>();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => h.Manager.Browse(h.NewContext(RequestType.Browse), ref continuationPoint, references))!;

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadViewIdUnknown));
            continuationPoint.Dispose();
        }

        [Test]
        public void TranslateBrowsePath_ResolvesTarget()
        {
            using Harness h = CreateHarness();
            (BaseObjectState parent, BaseObjectState child) = h.AddParentWithChild(ReferenceTypeIds.Organizes);
            object handle = h.Manager.GetManagerHandle(parent.NodeId)!;

            var relativePath = new RelativePathElement
            {
                IncludeSubtypes = true,
                IsInverse = false,
                TargetName = child.BrowseName
            };
            var targetIds = new List<ExpandedNodeId>();
            var unresolved = new List<NodeId>();

            h.Manager.TranslateBrowsePath(
                h.NewContext(RequestType.TranslateBrowsePathsToNodeIds),
                handle,
                relativePath,
                targetIds,
                unresolved);

            Assert.That(targetIds, Has.Count.EqualTo(1));
            Assert.That(targetIds[0], Is.EqualTo(new ExpandedNodeId(child.NodeId)));
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void TranslateBrowsePath_NoMatch_ReturnsEmptyTargets()
        {
            using Harness h = CreateHarness();
            (BaseObjectState parent, _) = h.AddParentWithChild(ReferenceTypeIds.Organizes);
            object handle = h.Manager.GetManagerHandle(parent.NodeId)!;

            var relativePath = new RelativePathElement
            {
                IncludeSubtypes = true,
                IsInverse = false,
                TargetName = new QualifiedName("DoesNotExist", h.NamespaceIndex)
            };
            var targetIds = new List<ExpandedNodeId>();
            var unresolved = new List<NodeId>();

            h.Manager.TranslateBrowsePath(
                h.NewContext(RequestType.TranslateBrowsePathsToNodeIds),
                handle,
                relativePath,
                targetIds,
                unresolved);

            Assert.That(targetIds, Is.Empty);
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void TranslateBrowsePath_ForeignHandle_ReturnsEmptyTargets()
        {
            using Harness h = CreateHarness();
            var foreignHandle = new NodeHandle { NodeId = ObjectIds.Server };

            var relativePath = new RelativePathElement
            {
                TargetName = new QualifiedName("Anything", h.NamespaceIndex)
            };
            var targetIds = new List<ExpandedNodeId>();
            var unresolved = new List<NodeId>();

            h.Manager.TranslateBrowsePath(
                h.NewContext(RequestType.TranslateBrowsePathsToNodeIds),
                foreignHandle,
                relativePath,
                targetIds,
                unresolved);

            Assert.That(targetIds, Is.Empty);
            Assert.That(unresolved, Is.Empty);
        }

        [Test]
        public void GetNodeMetadata_ReturnsMetadataForNode()
        {
            using Harness h = CreateHarness();
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(h.Context);
            variable.NodeId = new NodeId("MetaVar", h.NamespaceIndex);
            variable.BrowseName = new QualifiedName("MetaVar", h.NamespaceIndex);
            variable.DisplayName = new LocalizedText("MetaVar");
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentRead;
            variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.Value = 10;
            h.Manager.AddPredefinedNodePublic(h.Context, variable);
            object handle = h.Manager.GetManagerHandle(variable.NodeId)!;

            NodeMetadata? metadata = h.Manager.GetNodeMetadata(
                h.NewContext(RequestType.Read), handle, BrowseResultMask.All);

            Assert.That(metadata, Is.Not.Null);
            Assert.That(metadata!.NodeClass, Is.EqualTo(NodeClass.Variable));
            Assert.That(metadata.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(metadata.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(metadata.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
        }

        [Test]
        public void GetNodeMetadata_ForeignHandle_ReturnsNull()
        {
            using Harness h = CreateHarness();
            var foreignHandle = new NodeHandle { NodeId = ObjectIds.Server };

            NodeMetadata? metadata = h.Manager.GetNodeMetadata(
                h.NewContext(RequestType.Read), foreignHandle, BrowseResultMask.All);

            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void GetNodeMetadata_UnvalidatedHandle_ReturnsNull()
        {
            using Harness h = CreateHarness();
            // In namespace, but not backed by a node in memory.
            var placeholder = new NodeHandle { NodeId = new NodeId("Ghost", h.NamespaceIndex) };

            NodeMetadata? metadata = h.Manager.GetNodeMetadata(
                h.NewContext(RequestType.Read), placeholder, BrowseResultMask.All);

            Assert.That(metadata, Is.Null);
        }

        [Test]
        public void AddReferences_AddsExternalReferenceAndIsIdempotent()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("RefSource");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            var targetId = new NodeId("Target", 0);

            var references = new Dictionary<NodeId, IList<IReference>>
            {
                [node.NodeId] =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        TargetId = targetId
                    }
                ],
                // Unknown source node is silently skipped.
                [new NodeId("Missing", h.NamespaceIndex)] =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IsInverse = false,
                        TargetId = targetId
                    }
                ]
            };

            h.Manager.AddReferences(references);
            h.Manager.AddReferences(references);

            var refs = new List<IReference>();
            node.GetReferences(h.Context, refs);
            List<IReference> matching = refs.FindAll(
                r => r.TargetId == targetId && r.ReferenceTypeId == ReferenceTypeIds.HasComponent);
            Assert.That(matching, Has.Count.EqualTo(1));
        }

        [Test]
        public void DeleteReference_RemovesBidirectionalReference()
        {
            using Harness h = CreateHarness();
            BaseObjectState source = h.NewObject("DelSource");
            BaseObjectState target = h.NewObject("DelTarget");
            h.Manager.AddPredefinedNodePublic(h.Context, source);
            h.Manager.AddPredefinedNodePublic(h.Context, target);
            source.AddReference(ReferenceTypeIds.Organizes, false, target.NodeId);
            target.AddReference(ReferenceTypeIds.Organizes, true, source.NodeId);
            object handle = h.Manager.GetManagerHandle(source.NodeId)!;

            ServiceResult result = h.Manager.DeleteReference(
                handle, ReferenceTypeIds.Organizes, false, target.NodeId, true);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, target.NodeId), Is.False);
            Assert.That(target.ReferenceExists(ReferenceTypeIds.Organizes, true, source.NodeId), Is.False);
        }

        [Test]
        public void DeleteReference_ForeignHandle_ReturnsBadNodeIdUnknown()
        {
            using Harness h = CreateHarness();
            var foreignHandle = new NodeHandle { NodeId = ObjectIds.Server };

            ServiceResult result = h.Manager.DeleteReference(
                foreignHandle, ReferenceTypeIds.Organizes, false, ObjectIds.Server, false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void DeleteReference_UnvalidatedHandle_ReturnsBadNotSupported()
        {
            using Harness h = CreateHarness();
            // In namespace, but not validated / no node in memory.
            var placeholder = new NodeHandle { NodeId = new NodeId("Ghost", h.NamespaceIndex) };

            ServiceResult result = h.Manager.DeleteReference(
                placeholder, ReferenceTypeIds.Organizes, false, ObjectIds.Server, false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void CreateMonitoredItems_SkipsNodeNotOwnedByManager()
        {
            using Harness h = CreateHarness();
            var itemToCreate = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.Value },
                RequestedParameters = new MonitoringParameters { SamplingInterval = -1, QueueSize = 1 }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var filterErrors = new List<MonitoringFilterResult> { null! };
            var monitoredItems = new List<IMonitoredItem> { null! };
            var idFactory = new MonitoredItemIdFactory();

            h.Manager.CreateMonitoredItems(
                h.NewContext(RequestType.CreateMonitoredItems),
                1, 100, TimestampsToReturn.Both,
                new List<MonitoredItemCreateRequest> { itemToCreate },
                errors, filterErrors, monitoredItems, false, idFactory);

            Assert.That(itemToCreate.Processed, Is.False);
            Assert.That(monitoredItems[0], Is.Null);
        }

        [Test]
        public void CreateMonitoredItems_ReturnsBadAttributeIdInvalidForInvalidAttribute()
        {
            using Harness h = CreateHarness();
            BaseObjectState node = h.NewObject("MonitorObject");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            var itemToCreate = new MonitoredItemCreateRequest
            {
                // Value is invalid for an Object node.
                ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                RequestedParameters = new MonitoringParameters { SamplingInterval = -1, QueueSize = 1 }
            };
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var filterErrors = new List<MonitoringFilterResult> { null! };
            var monitoredItems = new List<IMonitoredItem> { null! };
            var idFactory = new MonitoredItemIdFactory();

            h.Manager.CreateMonitoredItems(
                h.NewContext(RequestType.CreateMonitoredItems),
                1, 100, TimestampsToReturn.Both,
                new List<MonitoredItemCreateRequest> { itemToCreate },
                errors, filterErrors, monitoredItems, false, idFactory);

            Assert.That(itemToCreate.Processed, Is.True);
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
            Assert.That(monitoredItems[0], Is.Null);
        }

        [Test]
        public void ModifyMonitoredItems_SkipsNullItem()
        {
            using Harness h = CreateHarness();
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var filterErrors = new List<MonitoringFilterResult> { null! };
            var itemsToModify = new List<MonitoredItemModifyRequest> { new() };
            var monitoredItems = new List<IMonitoredItem> { null! };

            h.Manager.ModifyMonitoredItems(
                h.NewContext(RequestType.ModifyMonitoredItems),
                TimestampsToReturn.Both, monitoredItems, itemsToModify, errors, filterErrors);

            Assert.That(itemsToModify[0].Processed, Is.False);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
        }

        [Test]
        public void ModifyMonitoredItems_SkipsForeignHandle()
        {
            using Harness h = CreateHarness();
            var mockItem = new Mock<IMonitoredItem>();
            mockItem.Setup(m => m.ManagerHandle).Returns(new NodeHandle { NodeId = new NodeId("x", 0) });
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var filterErrors = new List<MonitoringFilterResult> { null! };
            var itemsToModify = new List<MonitoredItemModifyRequest> { new() };
            var monitoredItems = new List<IMonitoredItem> { mockItem.Object };

            h.Manager.ModifyMonitoredItems(
                h.NewContext(RequestType.ModifyMonitoredItems),
                TimestampsToReturn.Both, monitoredItems, itemsToModify, errors, filterErrors);

            Assert.That(itemsToModify[0].Processed, Is.False);
        }

        [Test]
        public void DeleteMonitoredItems_SkipsNullItem()
        {
            using Harness h = CreateHarness();
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var processed = new List<bool> { false };
            var monitoredItems = new List<IMonitoredItem> { null! };

            h.Manager.DeleteMonitoredItems(
                h.NewContext(RequestType.DeleteMonitoredItems), monitoredItems, processed, errors);

            Assert.That(processed[0], Is.False);
            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
        }

        [Test]
        public void DeleteMonitoredItems_SkipsForeignHandle()
        {
            using Harness h = CreateHarness();
            var mockItem = new Mock<IMonitoredItem>();
            mockItem.Setup(m => m.ManagerHandle).Returns(new NodeHandle { NodeId = new NodeId("x", 0) });
            var errors = new List<ServiceResult> { ServiceResult.Good };
            var processed = new List<bool> { false };
            var monitoredItems = new List<IMonitoredItem> { mockItem.Object };

            h.Manager.DeleteMonitoredItems(
                h.NewContext(RequestType.DeleteMonitoredItems), monitoredItems, processed, errors);

            Assert.That(processed[0], Is.False);
        }

        [Test]
        public void IsNodeInView_ReturnsFalseForNonHandle()
        {
            using Harness h = CreateHarness();

            bool result = h.Manager.IsNodeInView(
                h.NewContext(RequestType.Browse), ObjectIds.ViewsFolder, new object());

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsNodeInView_ReturnsFalseWhenHandleNodeIsNull()
        {
            using Harness h = CreateHarness();
            var placeholder = new NodeHandle { NodeId = new NodeId("Ghost", h.NamespaceIndex) };

            bool result = h.Manager.IsNodeInView(
                h.NewContext(RequestType.Browse), ObjectIds.ViewsFolder, placeholder);

            Assert.That(result, Is.False);
        }

        [Test]
        public void IsNodeInView_ValidatedHandleReturnsFalseFromPublicOverloadRecursion()
        {
            // FLAGGED PRODUCTION BEHAVIOUR (not fixed here): the public
            // IsNodeInView(OperationContext, NodeId, object) delegates via
            // IsNodeInView(context, viewId, handle.Node). Because it forwards an
            // OperationContext (not a ServerSystemContext), overload resolution binds
            // back to the same public (OperationContext, NodeId, object) overload rather
            // than the intended protected (ServerSystemContext, NodeId, NodeState) helper.
            // The recursive call then receives a NodeState (not a NodeHandle) and returns
            // false, so the public API reports "not in view" for in-memory nodes even when
            // the view exists. This characterization test locks in the current behaviour.
            using Harness h = CreateHarness();
            var view = new ViewState();
            view.CreateAsPredefinedNode(h.Context);
            view.NodeId = new NodeId("MyView", h.NamespaceIndex);
            view.BrowseName = new QualifiedName("MyView", h.NamespaceIndex);
            h.Manager.AddPredefinedNodePublic(h.Context, view);
            BaseObjectState node = h.NewObject("ViewedNode");
            h.Manager.AddPredefinedNodePublic(h.Context, node);
            var handle = new NodeHandle(node.NodeId, node);

            // The view genuinely resolves through the protected helper...
            Assert.That(h.Manager.FindPredefinedNode<ViewState>(view.NodeId), Is.SameAs(view));

            // ...yet the public overload returns false because of the recursion described above.
            bool result = h.Manager.IsNodeInView(
                h.NewContext(RequestType.Browse), view.NodeId, handle);

            Assert.That(result, Is.False);
        }

        [Test]
        public void SessionActivated_WithNoMonitoredNodes_DoesNotThrow()
        {
            using Harness h = CreateHarness();

            Assert.DoesNotThrow(
                () => h.Manager.SessionActivated(
                    h.NewContext(RequestType.ActivateSession), new NodeId("Session", h.NamespaceIndex)));
        }

        private static Harness CreateHarness()
        {
            Mock<IServerInternal> mockServer = BuildMockServer(out MonitoredItemQueueFactory queueFactory);
            var manager = new TestableCustomNodeManager2(
                mockServer.Object, CreateConfiguration(), false, new Mock<ILogger>().Object, TestNamespaceUri);
            return new Harness(manager, mockServer, queueFactory);
        }

        private static Harness CreatePlaceholderHarness(out PlaceholderNodeManager manager)
        {
            Mock<IServerInternal> mockServer = BuildMockServer(out MonitoredItemQueueFactory queueFactory);
            manager = new PlaceholderNodeManager(
                mockServer.Object, CreateConfiguration(), new Mock<ILogger>().Object, TestNamespaceUri);
            return new Harness(manager, mockServer, queueFactory);
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        private static Mock<IServerInternal> BuildMockServer(out MonitoredItemQueueFactory queueFactory)
        {
            var mockServer = new Mock<IServerInternal>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            queueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            return mockServer;
        }

        private sealed class Harness : IDisposable
        {
            private readonly MonitoredItemQueueFactory m_queueFactory;

            public Harness(
                TestableCustomNodeManager2 manager,
                Mock<IServerInternal> mockServer,
                MonitoredItemQueueFactory queueFactory)
            {
                Manager = manager;
                MockServer = mockServer;
                m_queueFactory = queueFactory;
            }

            public TestableCustomNodeManager2 Manager { get; }

            public Mock<IServerInternal> MockServer { get; }

            public ServerSystemContext Context => Manager.SystemContext;

            public ushort NamespaceIndex => Manager.NamespaceIndex;

            public OperationContext NewContext(RequestType requestType)
            {
                return new OperationContext(
                    new RequestHeader(), null, requestType, RequestLifetime.None);
            }

            public BaseObjectState NewObject(string name)
            {
                var node = new BaseObjectState(null);
                node.CreateAsPredefinedNode(Context);
                node.NodeId = new NodeId(name, NamespaceIndex);
                node.BrowseName = new QualifiedName(name, NamespaceIndex);
                node.DisplayName = new LocalizedText(name);
                return node;
            }

            public BaseDataVariableState NewVariable(string name, int value)
            {
                var variable = new BaseDataVariableState(null);
                variable.CreateAsPredefinedNode(Context);
                variable.NodeId = new NodeId(name, NamespaceIndex);
                variable.BrowseName = new QualifiedName(name, NamespaceIndex);
                variable.DisplayName = new LocalizedText(name);
                variable.DataType = DataTypeIds.Int32;
                variable.ValueRank = ValueRanks.Scalar;
                variable.AccessLevel = AccessLevels.CurrentRead;
                variable.UserAccessLevel = AccessLevels.CurrentRead;
                variable.Value = value;
                return variable;
            }

            public BaseDataVariableState NewWritableVariable(string name, int value)
            {
                BaseDataVariableState variable = NewVariable(name, value);
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                return variable;
            }

            public (BaseObjectState Parent, BaseObjectState Child) AddParentWithChild(NodeId referenceTypeId)
            {
                BaseObjectState parent = NewObject("Parent");
                BaseObjectState child = NewObject("Child");
                Manager.AddPredefinedNodePublic(Context, parent);
                Manager.AddPredefinedNodePublic(Context, child);
                parent.AddReference(referenceTypeId, false, child.NodeId);
                child.AddReference(referenceTypeId, true, parent.NodeId);
                return (parent, child);
            }

            public ContinuationPoint NewContinuationPoint(object handle)
            {
                return new ContinuationPoint
                {
                    NodeToBrowse = handle,
                    View = new ViewDescription(),
                    BrowseDirection = BrowseDirection.Forward,
                    IncludeSubtypes = true,
                    ResultMask = BrowseResultMask.All
                };
            }

            public void Dispose()
            {
                m_queueFactory.Dispose();
                Manager.Dispose();
            }
        }

        /// <summary>
        /// A node manager that returns an unvalidated placeholder handle for a
        /// designated NodeId so the "node is not in memory" branches of Read and
        /// Write (which the default GetManagerHandle never reaches) can be
        /// exercised deterministically.
        /// </summary>
        private sealed class PlaceholderNodeManager : TestableCustomNodeManager2
        {
            public PlaceholderNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, false, logger, namespaceUris)
            {
            }

            public NodeId PlaceholderNodeId { get; set; } = NodeId.Null;

            protected override NodeHandle? GetManagerHandle(
                ServerSystemContext context,
                NodeId nodeId,
                IDictionary<NodeId, NodeState> cache)
            {
                if (!PlaceholderNodeId.IsNull && nodeId == PlaceholderNodeId)
                {
                    return new NodeHandle { NodeId = nodeId, RootId = nodeId, Validated = false };
                }

                return base.GetManagerHandle(context, nodeId, cache);
            }
        }
    }
}
