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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Unit tests for the <see cref="INodeManagementAsyncNodeManager"/>
    /// implementation on <see cref="AsyncCustomNodeManager"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("NodeManagement")]
    [Parallelizable(ParallelScope.All)]
    public class AsyncCustomNodeManagerNodeManagementTests
    {
        private const string TestNamespaceUri = "http://test.org/UA/NodeManagement/";

        [TestCase("unknownDataType")]
        [TestCase("nonDataType")]
        [TestCase("wrongValue")]
        [TestCase("arrayRank")]
        [TestCase("invalidRank")]
        [TestCase("negativeMinimum")]
        [TestCase("nanMinimum")]
        [TestCase("infiniteMinimum")]
        [TestCase("scalarDimensions")]
        [TestCase("rankDimensions")]
        public async Task AddVariableRejectsInconsistentAttributes(string invalidAttribute)
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            var types = (TypeTable)h.Context.TypeTable;
            types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            types.AddSubtype(DataTypeIds.Int32, DataTypeIds.BaseDataType);
            types.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            ushort ns = h.Manager.NamespaceIndexes[0];
            var attributes = new VariableAttributes
            {
                SpecifiedAttributes = (uint)NodeAttributesMask.DataType |
                    (uint)NodeAttributesMask.ValueRank |
                    (uint)NodeAttributesMask.Value |
                    (uint)NodeAttributesMask.MinimumSamplingInterval |
                    (uint)NodeAttributesMask.ArrayDimensions,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                Value = 42,
                MinimumSamplingInterval = -1
            };
            switch (invalidAttribute)
            {
                case "unknownDataType":
                    attributes.DataType = new NodeId("Unknown", ns);
                    break;
                case "nonDataType":
                    attributes.DataType = ObjectTypeIds.BaseObjectType;
                    break;
                case "wrongValue":
                    attributes.Value = "not an integer";
                    break;
                case "arrayRank":
                    attributes.ValueRank = ValueRanks.OneDimension;
                    break;
                case "invalidRank":
                    attributes.ValueRank = -4;
                    break;
                case "negativeMinimum":
                    attributes.MinimumSamplingInterval = -2;
                    break;
                case "nanMinimum":
                    attributes.MinimumSamplingInterval = double.NaN;
                    break;
                case "infiniteMinimum":
                    attributes.MinimumSamplingInterval = double.PositiveInfinity;
                    break;
                case "scalarDimensions":
                    attributes.ArrayDimensions = [2];
                    break;
                case "rankDimensions":
                    attributes.ValueRank = ValueRanks.OneDimension;
                    attributes.Value = new Variant([1, 2]);
                    attributes.ArrayDimensions = [2, 2];
                    break;
            }
            var requestedId = new NodeId("Variable", ns);
            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                RequestedNewNodeId = requestedId,
                BrowseName = new QualifiedName("Variable", ns),
                NodeClass = NodeClass.Variable,
                TypeDefinition = VariableTypeIds.BaseVariableType,
                NodeAttributes = new ExtensionObject(attributes)
            };

            (ServiceResult result, NodeId addedId) = await h.Manager.AddNodeAsync(h.OperationContext, item)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeAttributesInvalid));
            Assert.That(addedId.IsNull, Is.True);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(requestedId), Is.False);
            Assert.That(h.Manager.PredefinedNodes[parentId].FindChildWithQualifiedName(h.Context, item.BrowseName),
                Is.Null);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(100)]
        public async Task AddVariableAcceptsValidAttributesAndIndeterminateSampling(double minimumSamplingInterval)
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            var types = (TypeTable)h.Context.TypeTable;
            types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            types.AddSubtype(DataTypeIds.Int32, DataTypeIds.BaseDataType);
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            ushort ns = h.Manager.NamespaceIndexes[0];
            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("Variable", ns),
                NodeClass = NodeClass.Variable,
                TypeDefinition = VariableTypeIds.BaseVariableType,
                NodeAttributes = new ExtensionObject(new VariableAttributes
                {
                    SpecifiedAttributes = (uint)NodeAttributesMask.DataType |
                        (uint)NodeAttributesMask.ValueRank |
                        (uint)NodeAttributesMask.Value |
                        (uint)NodeAttributesMask.MinimumSamplingInterval |
                        (uint)NodeAttributesMask.ArrayDimensions,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.OneDimension,
                    Value = new Variant([1, 2]),
                    ArrayDimensions = [2],
                    MinimumSamplingInterval = minimumSamplingInterval
                })
            };

            (ServiceResult result, NodeId addedId) = await h.Manager.AddNodeAsync(h.OperationContext, item)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            BaseVariableState variable = h.Manager.FindPredefinedNode<BaseVariableState>(addedId);
            Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(variable.Value.TryGetValue(out ArrayOf<int> values), Is.True);
            Assert.That(values, Is.EqualTo((ArrayOf<int>)[1, 2]));
            Assert.That(variable.ArrayDimensions, Is.EqualTo((ArrayOf<uint>)[2]));
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(minimumSamplingInterval));
        }

        [TestCase(1, 2u, true)]
        [TestCase(2, 2u, true)]
        [TestCase(3, 2u, false)]
        [TestCase(4, 2u, false)]
        [TestCase(3, 0u, true)]
        public Task AddVariableEnforcesDeclaredArrayMaximum(int length, uint maximum, bool valid)
        {
            return AssertVariableDimensionAdmissionAsync(
                new Variant(Enumerable.Range(1, length).ToArrayOf()),
                DataTypeIds.Int32, ValueRanks.OneDimension, [maximum], valid);
        }

        [TestCase(false, 2u)]
        [TestCase(true, 2u)]
        [TestCase(false, 0u)]
        [TestCase(true, 0u)]
        public Task AddVariableAcceptsTypedNullAndEmptyArrayValues(bool nullArray, uint maximum)
        {
            ArrayOf<int> values = nullArray ? ArrayOf<int>.Null : ArrayOf<int>.Empty;
            return AssertVariableDimensionAdmissionAsync(
                new Variant(values), DataTypeIds.Int32, ValueRanks.OneDimension, [maximum], valid: true);
        }

        [TestCase(1, 2, 2u, 2u, true)]
        [TestCase(2, 1, 2u, 2u, true)]
        [TestCase(2, 2, 2u, 2u, true)]
        [TestCase(3, 1, 2u, 2u, false)]
        [TestCase(4, 1, 2u, 2u, false)]
        [TestCase(1, 3, 2u, 2u, false)]
        [TestCase(3, 2, 0u, 2u, true)]
        [TestCase(3, 3, 0u, 2u, false)]
        [TestCase(2, 3, 2u, 0u, true)]
        [TestCase(3, 3, 2u, 0u, false)]
        public Task AddVariableEnforcesDeclaredMatrixMaxima(
            int rows,
            int columns,
            uint rowMaximum,
            uint columnMaximum,
            bool valid)
        {
            MatrixOf<int> matrix = Enumerable.Range(1, rows * columns).ToArrayOf().ToMatrix(rows, columns);
            return AssertVariableDimensionAdmissionAsync(
                new Variant(matrix), DataTypeIds.Int32, 2, [rowMaximum, columnMaximum], valid);
        }

        [TestCase(1, 2u, true)]
        [TestCase(2, 2u, true)]
        [TestCase(3, 2u, false)]
        [TestCase(4, 2u, false)]
        [TestCase(3, 0u, true)]
        public Task AddByteArrayVariableEnforcesMaximumForByteStringValue(int length, uint maximum, bool valid)
        {
            ByteString bytes = Enumerable.Range(1, length).Select(value => (byte)value).ToByteString();
            return AssertVariableDimensionAdmissionAsync(
                new Variant(bytes), DataTypeIds.Byte, ValueRanks.OneDimension, [maximum], valid);
        }

        [TestCaseSource(nameof(DimensionValueTypeCases))]
        public async Task AddVariableEnforcesDimensionMaximaForBuiltInTypes(
            BuiltInType builtInType,
            Variant value,
            NodeId dataType,
            ArrayOf<uint> dimensions,
            bool valid)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(value.IsNull, Is.False);
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
                Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(dimensions.Count));
            }
            await AssertVariableDimensionAdmissionAsync(value, dataType, dimensions.Count, dimensions, valid)
                .ConfigureAwait(false);
        }

        [TestCase("subtype", true)]
        [TestCase("inherited", true)]
        [TestCase("dataType", false)]
        [TestCase("valueRank", false)]
        public async Task AddVariableHonorsTypeDefinitionConstraints(string constraint, bool valid)
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            var types = (TypeTable)h.Context.TypeTable;
            types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            types.AddSubtype(DataTypeIds.Number, DataTypeIds.BaseDataType);
            types.AddSubtype(DataTypeIds.Int32, DataTypeIds.Number);
            types.AddSubtype(DataTypeIds.String, DataTypeIds.BaseDataType);
            types.AddSubtype(VariableTypeIds.BaseVariableType, NodeId.Null);
            ushort ns = h.Manager.NamespaceIndexes[0];
            var variableType = new ConstrainedVariableType
            {
                NodeId = new NodeId("NumericType", ns),
                BrowseName = new QualifiedName("NumericType", ns),
                SuperTypeId = VariableTypeIds.BaseVariableType,
                DataType = DataTypeIds.Number,
                ValueRank = ValueRanks.Scalar
            };
            await h.Manager.AddPredefinedNodeAsyncPublic(h.Context, variableType).ConfigureAwait(false);
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            var attributes = new VariableAttributes
            {
                SpecifiedAttributes = (uint)NodeAttributesMask.DataType |
                    (uint)NodeAttributesMask.ValueRank |
                    (uint)NodeAttributesMask.Value,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                Value = 42
            };
            if (constraint == "inherited")
            {
                attributes.SpecifiedAttributes = (uint)NodeAttributesMask.Value;
            }
            else if (constraint == "dataType")
            {
                attributes.DataType = DataTypeIds.String;
                attributes.Value = "text";
            }
            else if (constraint == "valueRank")
            {
                attributes.ValueRank = ValueRanks.OneDimension;
                attributes.Value = new Variant([42]);
            }
            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("Variable", ns),
                NodeClass = NodeClass.Variable,
                TypeDefinition = variableType.NodeId,
                NodeAttributes = new ExtensionObject(attributes)
            };

            (ServiceResult result, NodeId addedId) = await h.Manager.AddNodeAsync(h.OperationContext, item)
                .ConfigureAwait(false);

            Assert.That(result.StatusCode,
                Is.EqualTo(valid ? StatusCodes.Good : StatusCodes.BadNodeAttributesInvalid));
            if (valid)
            {
                BaseVariableState variable = h.Manager.FindPredefinedNode<BaseVariableState>(addedId);
                Assert.That(variable.DataType,
                    Is.EqualTo(constraint == "inherited" ? DataTypeIds.Number : DataTypeIds.Int32));
                Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(variable.Value, Is.EqualTo(new Variant(42)));
            }
            else
            {
                Assert.That(addedId.IsNull, Is.True);
                Assert.That(h.Manager.PredefinedNodes[parentId].FindChildWithQualifiedName(h.Context, item.BrowseName),
                    Is.Null);
            }
        }

        [Test]
        public void AllowNodeManagement_DefaultsToFalse()
        {
            using Harness h = CreateHarness();
            Assert.That(h.Manager.AllowNodeManagement, Is.False);
        }

        [Test]
        public void AllowNodeManagement_IsTrueWhenOptedIn()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            Assert.That(h.Manager.AllowNodeManagement, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_BrowseNameInvalid_ReturnsBadBrowseNameInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddNodesItem
            {
                ParentNodeId = ObjectIds.ObjectsFolder,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = default,
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = new NodeId("MyObj", ns)
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_ParentNodeIdInvalid_ReturnsBadParentNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddNodesItem
            {
                ParentNodeId = default,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("MyObj", ns),
                NodeClass = NodeClass.Object,
                RequestedNewNodeId = new NodeId("MyObj", ns)
            };

            (ServiceResult result, _) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadParentNodeIdInvalid));
        }

        [Test]
        public async Task AddNodeAsync_RequestedNewNodeIdInOtherNamespace_ReturnsBadNodeIdRejectedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            // Add a parent locally so the inverse-edge path runs but the
            // RequestedNewNodeId is in a foreign namespace.
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
                RequestedNewNodeId = new NodeId("Foreign", 0) // ns=0 not in this manager
            };

            (ServiceResult result, _) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdRejected));
        }

        [Test]
        public async Task AddNodeAsync_NullTypeDefinition_ReturnsBadTypeDefinitionInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_WrongTypeDefinitionClass_ReturnsBadTypeDefinitionInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = VariableTypeIds.BaseVariableType
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_UnknownTypeDefinition_ReturnsBadTypeDefinitionInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = new NodeId("UnknownType", ns)
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            Assert.That(added.IsNull, Is.True);
        }

        [Test]
        public async Task AddNodeAsync_RequestedNodeIdAlreadyExists_ReturnsBadNodeIdExistsAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            NodeId existingId = await h.AddObjectAsync("Existing").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Child", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
                RequestedNewNodeId = existingId
            };

            (ServiceResult result, _) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public async Task AddNodeAsync_DuplicateBrowseNameUnderParent_ReturnsBadBrowseNameDuplicatedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            // First child succeeds.
            var first = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("SameName", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType
            };

            (ServiceResult firstResult, NodeId firstId) = await h.Manager
                .AddNodeAsync(h.OperationContext, first).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(firstResult), Is.True, $"expected Good result; got {firstResult}");
            Assert.That(firstId.IsNull, Is.False);

            // Second child under same parent with same browse name fails.
            var second = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("SameName", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType
            };

            (ServiceResult secondResult, NodeId secondId) = await h.Manager
                .AddNodeAsync(h.OperationContext, second).ConfigureAwait(false);

            Assert.That(secondResult.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(secondId.IsNull, Is.True);
        }

        /// <summary>
        /// Verifies indexed browse-name reuse after renaming cannot overwrite the existing node's identifier.
        /// </summary>
        [Test]
        public async Task IndexedBrowseNameReusePreservesExistingNodeIdentityAsync()
        {
            // The CTT (Node Management Delete Node Err-002.js) adds thousands of nodes
            // below one parent; the duplicate check must stay correct for large parents.
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            NodeId renamedId = default;
            for (int ii = 0; ii < 500; ii++)
            {
                (ServiceResult result, NodeId added) = await h.Manager
                    .AddNodeAsync(h.OperationContext, new AddNodesItem
                    {
                        ParentNodeId = parentId,
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        BrowseName = new QualifiedName("Child" + ii, ns),
                        NodeClass = NodeClass.Variable,
                        TypeDefinition = VariableTypeIds.BaseVariableType
                    }).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True, $"child {ii}: {result}");
                if (ii == 42)
                {
                    renamedId = added;
                }
            }

            (ServiceResult duplicate, NodeId duplicateId) = await h.Manager
                .AddNodeAsync(h.OperationContext, new AddNodesItem
                {
                    ParentNodeId = parentId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Child250", ns),
                    NodeClass = NodeClass.Variable,
                    TypeDefinition = VariableTypeIds.BaseVariableType
                }).ConfigureAwait(false);
            Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(duplicateId.IsNull, Is.True);

            // a renamed child frees its old browse name and takes the new one.
            NodeState renamedNode = h.Manager.PredefinedNodes[renamedId];
            renamedNode.BrowseName = new QualifiedName("Renamed", ns);
            int countBefore = h.Manager.PredefinedNodes.Count;

            (ServiceResult collision, NodeId collisionId) = await h.Manager
                .AddNodeAsync(h.OperationContext, new AddNodesItem
                {
                    ParentNodeId = parentId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    RequestedNewNodeId = renamedId,
                    BrowseName = new QualifiedName("Child42", ns),
                    NodeClass = NodeClass.Variable,
                    TypeDefinition = VariableTypeIds.BaseVariableType
                }).ConfigureAwait(false);
            Assert.That(collision.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(collisionId.IsNull, Is.True);
            Assert.That(h.Manager.PredefinedNodes, Has.Count.EqualTo(countBefore));
            Assert.That(h.Manager.PredefinedNodes[renamedId], Is.SameAs(renamedNode));
            Assert.That(renamedNode.BrowseName, Is.EqualTo(new QualifiedName("Renamed", ns)));

            // Renaming frees the BrowseName; automatic allocation must not reuse the still-owned NodeId.
            (ServiceResult freed, NodeId freedId) = await h.Manager
                .AddNodeAsync(h.OperationContext, new AddNodesItem
                {
                    ParentNodeId = parentId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Child42", ns),
                    NodeClass = NodeClass.Variable,
                    TypeDefinition = VariableTypeIds.BaseVariableType
                }).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(freed), Is.True, $"expected Good result; got {freed}");
            Assert.That(freedId.IsNull, Is.False);
            Assert.That(freedId.NamespaceIndex, Is.EqualTo(ns));
            Assert.That(freedId, Is.Not.EqualTo(renamedId));
            Assert.That(h.Manager.PredefinedNodes[renamedId], Is.SameAs(renamedNode));
            Assert.That(h.Manager.PredefinedNodes, Has.Count.EqualTo(countBefore + 1));

            (ServiceResult taken, _) = await h.Manager
                .AddNodeAsync(h.OperationContext, new AddNodesItem
                {
                    ParentNodeId = parentId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Renamed", ns),
                    NodeClass = NodeClass.Variable,
                    TypeDefinition = VariableTypeIds.BaseVariableType
                }).ConfigureAwait(false);
            Assert.That(taken.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task AddNodeAsync_AllocatesNewNodeIdWhenRequestedIsNullAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("AutoNode", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(added.IsNull, Is.False);
            Assert.That(added.NamespaceIndex, Is.EqualTo(ns));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(added), Is.True);
        }

        [Test]
        public async Task AddNodeAsync_HonoursRequestedNewNodeIdAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            var requested = new NodeId("ExplicitId", ns);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("ExplicitId", ns),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
                RequestedNewNodeId = requested
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(added, Is.EqualTo(requested));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(requested), Is.True);
        }

        [Test]
        public async Task AddNodeAsyncRequestedIdWithSupportedNamespaceUriIsHonouredAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort namespaceIndex = h.Manager.NamespaceIndexes[0];
            NodeId parentNodeId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            var requestedNodeId = new ExpandedNodeId("ExplicitUriId", TestNamespaceUri);
            var item = new AddNodesItem
            {
                ParentNodeId = parentNodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("ExplicitUriId", namespaceIndex),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
                RequestedNewNodeId = requestedNodeId
            };

            (ServiceResult result, NodeId addedNodeId) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            var expectedNodeId = new NodeId("ExplicitUriId", namespaceIndex);
            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(addedNodeId, Is.EqualTo(expectedNodeId));
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(expectedNodeId), Is.True);
        }

        [Test]
        public async Task AddNodeVariableWithAttributesAppliesAttributesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            var types = (TypeTable)h.Context.TypeTable;
            types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            types.AddSubtype(DataTypeIds.Int32, DataTypeIds.BaseDataType);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var attributes = new VariableAttributes
            {
                SpecifiedAttributes =
                    (uint)NodeAttributesMask.DisplayName |
                    (uint)NodeAttributesMask.DataType |
                    (uint)NodeAttributesMask.ValueRank |
                    (uint)NodeAttributesMask.AccessLevel,
                DisplayName = new LocalizedText("Test Variable"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead
            };

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("AttrVar", ns),
                NodeClass = NodeClass.Variable,
                TypeDefinition = VariableTypeIds.BaseVariableType,
                NodeAttributes = new ExtensionObject(attributes)
            };

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            var variable = h.Manager.PredefinedNodes[added] as BaseDataVariableState;
            Assert.That(variable, Is.Not.Null);
            Assert.That(variable!.DataType, Is.EqualTo(DataTypeIds.Int32));
            Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(variable.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(variable.DisplayName.Text, Is.EqualTo("Test Variable"));
        }

        [Test]
        public async Task AddNodeAsync_UnsupportedNodeClass_ReturnsBadNodeClassInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                BrowseName = new QualifiedName("MethodNode", ns),
                NodeClass = NodeClass.Method
            };

            (ServiceResult result, _) = await h.Manager
                .AddNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeClassInvalid));
        }

        [Test]
        public async Task DeleteNodeAsync_UnknownNodeId_ReturnsBadNodeIdUnknownAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new DeleteNodesItem
            {
                NodeId = new NodeId("DoesNotExist", ns),
                DeleteTargetReferences = false
            };

            ServiceResult result = await h.Manager
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DeleteNodeAsync_NullNodeId_ReturnsBadNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);

            var item = new DeleteNodesItem
            {
                NodeId = default,
                DeleteTargetReferences = false
            };

            ServiceResult result = await h.Manager
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task DeleteNodeAsync_RemovesNodeFromPredefinedNodesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            NodeId nodeId = await h.AddObjectAsync("Deletable").ConfigureAwait(false);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = false
            };

            ServiceResult result = await h.Manager
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(nodeId), Is.False);
        }

        [Test]
        public async Task DeleteNodeAsync_DeleteTargetReferencesTrue_RequestsRemoveReferencesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            // Set up a node with an inverse reference so RemovePredefinedNodeAsync
            // collects a LocalReference to remove on the target.
            NodeId nodeId = await h.AddObjectAsync("WithRefs").ConfigureAwait(false);
            NodeState node = h.Manager.PredefinedNodes[nodeId];
            // Add an inverse hierarchical reference pointing at a foreign target.
            var foreign = new NodeId("ForeignTarget", (ushort)(ns + 100));
            node.AddReference(ReferenceTypeIds.Organizes, true, foreign);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = true
            };

            ServiceResult result = await h.Manager
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            h.MockMasterNodeManager.Verify(
                m => m.RemoveReferencesAsync(
                    It.Is<List<LocalReference>>(l => l.Count > 0),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        [Test]
        public async Task DeleteNodeAsync_DeleteTargetReferencesFalse_DoesNotRequestRemoveReferencesAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId nodeId = await h.AddObjectAsync("KeepRefs").ConfigureAwait(false);
            NodeState node = h.Manager.PredefinedNodes[nodeId];
            var foreign = new NodeId("ForeignTarget", (ushort)(ns + 100));
            node.AddReference(ReferenceTypeIds.Organizes, true, foreign);

            var item = new DeleteNodesItem
            {
                NodeId = nodeId,
                DeleteTargetReferences = false
            };

            ServiceResult result = await h.Manager
                .DeleteNodeAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            h.MockMasterNodeManager.Verify(
                m => m.RemoveReferencesAsync(
                    It.IsAny<List<LocalReference>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task AddNodeAsync_LocalParentDoesNotCreateExplicitForwardReferenceAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);

            (ServiceResult result, NodeId added) = await h.Manager
                .AddNodeAsync(h.OperationContext, new AddNodesItem
                {
                    ParentNodeId = parentId,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    BrowseName = new QualifiedName("Child", ns),
                    NodeClass = NodeClass.Object,
                    TypeDefinition = ObjectTypeIds.BaseObjectType
                }).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"expected Good result; got {result}");
            Assert.That(
                h.Manager.PredefinedNodes[parentId].ReferenceExists(
                    ReferenceTypeIds.Organizes,
                    false,
                    added),
                Is.False);
        }

        [Test]
        public async Task AddReferenceAsync_SourceUnknown_ReturnsBadSourceNodeIdInvalidAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            var item = new AddReferencesItem
            {
                SourceNodeId = new NodeId("DoesNotExist", ns),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("Anything", ns)
            };

            ServiceResult result = await h.Manager
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSourceNodeIdInvalid));
        }

        [Test]
        public async Task AddReferenceAsync_DuplicateReference_ReturnsBadDuplicateReferenceNotAllowedAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);

            var item = new AddReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult first = await h.Manager
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);
            Assume.That(ServiceResult.IsGood(first), Is.True);

            ServiceResult second = await h.Manager
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(second.StatusCode, Is.EqualTo(StatusCodes.BadDuplicateReferenceNotAllowed));
        }

        [Test]
        public async Task AddReferenceAsync_AddsReferenceToSourceNodeAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);

            var item = new AddReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult result = await h.Manager
                .AddReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            NodeState source = h.Manager.PredefinedNodes[sourceId];
            Assert.That(source.ReferenceExists(ReferenceTypeIds.Organizes, false, targetId), Is.True);
        }

        [Test]
        public async Task DeleteReferenceAsync_NoMatch_ReturnsBadNoMatchAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = new NodeId("NoSuchTarget", ns)
            };

            ServiceResult result = await h.Manager
                .DeleteReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
        }

        [Test]
        public async Task DeleteReferenceAsync_RemovesExistingReferenceAsync()
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            ushort ns = h.Manager.NamespaceIndexes[0];

            NodeId sourceId = await h.AddObjectAsync("Source").ConfigureAwait(false);
            var targetId = new NodeId("Target", ns);
            h.Manager.PredefinedNodes[sourceId].AddReference(ReferenceTypeIds.Organizes, false, targetId);

            var item = new DeleteReferencesItem
            {
                SourceNodeId = sourceId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsForward = true,
                TargetNodeId = targetId
            };

            ServiceResult result = await h.Manager
                .DeleteReferenceAsync(h.OperationContext, item).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(
                h.Manager.PredefinedNodes[sourceId].ReferenceExists(ReferenceTypeIds.Organizes, false, targetId),
                Is.False);
        }

        [Test]
        public void AddPredefinedNodeSynchronously_RegistersNodeAndChildren()
        {
            using Harness h = CreateHarness();
            ushort ns = h.Manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("SyncParent", ns),
                BrowseName = new QualifiedName("SyncParent", ns),
                DisplayName = new LocalizedText("SyncParent")
            };
            var child = new BaseDataVariableState(parent)
            {
                NodeId = new NodeId("SyncParent_Child", ns),
                BrowseName = new QualifiedName("Child", ns),
                DisplayName = new LocalizedText("Child"),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                ReferenceTypeId = ReferenceTypeIds.HasProperty
            };
            parent.AddChild(child);

            h.Manager.AddPredefinedNodeSynchronouslyPublic(parent);

            Assert.That(h.Manager.PredefinedNodes.ContainsKey(parent.NodeId), Is.True);
            Assert.That(h.Manager.PredefinedNodes.ContainsKey(child.NodeId), Is.True);
        }

        [Test]
        public void AddPredefinedNodeSynchronously_NullNode_ThrowsArgumentNullException()
        {
            using Harness h = CreateHarness();

            Assert.Throws<System.ArgumentNullException>(
                () => h.Manager.AddPredefinedNodeSynchronouslyPublic(null!));
        }

        [Test]
        public void AddPredefinedNodeSynchronously_PropagatesTypeHierarchyAndUpdatesRootNotifier()
        {
            using Harness h = CreateHarness();
            ushort ns = h.Manager.NamespaceIndexes[0];

            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("SyncTypeParent", ns),
                BrowseName = new QualifiedName("SyncTypeParent", ns),
                DisplayName = new LocalizedText("SyncTypeParent"),
                IsPartOfTypeHierarchy = true
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId("SyncTypeParent_Child", ns),
                BrowseName = new QualifiedName("Child", ns),
                DisplayName = new LocalizedText("Child"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent
            };
            parent.AddChild(child);

            // Registering the parent as a root notifier exercises the
            // notifier-wiring branch inside IndexPredefinedNode.
            h.Manager.SeedRootNotifier(parent.NodeId);

            h.Manager.AddPredefinedNodeSynchronouslyPublic(parent);

            Assert.That(h.Manager.PredefinedNodes.ContainsKey(child.NodeId), Is.True);
            Assert.That(child.IsPartOfTypeHierarchy, Is.True);
            Assert.That(
                parent.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server),
                Is.True);
        }

        private static Harness CreateHarness(bool allowNodeManagement = false)
        {
            var mockServer = new Mock<IServerInternal>();
            var mockLogger = new Mock<ILogger>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            mockSession.Setup(s => s.PreferredLocales).Returns([]);

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(monitoredItemQueueFactory);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            NodeManagementTestNodeManager manager = allowNodeManagement
                ? new OptedInTestNodeManager(mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri)
                : new NodeManagementTestNodeManager(mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri);

            // Ensure cross-NodeManager add-reference path can find local nodes via the master.
            mockMasterNodeManager
                .Setup(m => m.AddReferencesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<IList<IReference>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            mockMasterNodeManager
                .Setup(m => m.RemoveReferencesAsync(
                    It.IsAny<List<LocalReference>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            var opContext = new OperationContext(
                new RequestHeader(), null!, RequestType.AddNodes, RequestLifetime.None, mockSession.Object);

            return new Harness(manager, serverSystemContext, opContext, monitoredItemQueueFactory, mockMasterNodeManager);
        }

        private static IEnumerable<TestCaseData> DimensionValueTypeCases()
        {
            IEnumerable<TestCaseData>[] cases =
            [
                CreateDimensionTypeCases(true, BuiltInType.Boolean, DataTypeIds.Boolean),
                CreateDimensionTypeCases((sbyte)-7, BuiltInType.SByte, DataTypeIds.SByte),
                CreateDimensionTypeCases((byte)7, BuiltInType.Byte, DataTypeIds.Byte),
                CreateDimensionTypeCases((short)-17, BuiltInType.Int16, DataTypeIds.Int16),
                CreateDimensionTypeCases((ushort)17, BuiltInType.UInt16, DataTypeIds.UInt16),
                CreateDimensionTypeCases(17u, BuiltInType.UInt32, DataTypeIds.UInt32),
                CreateDimensionTypeCases(-17L, BuiltInType.Int64, DataTypeIds.Int64),
                CreateDimensionTypeCases(17UL, BuiltInType.UInt64, DataTypeIds.UInt64),
                CreateDimensionTypeCases(1.25f, BuiltInType.Float, DataTypeIds.Float),
                CreateDimensionTypeCases(1.25, BuiltInType.Double, DataTypeIds.Double),
                CreateDimensionTypeCases("dimension-value", BuiltInType.String, DataTypeIds.String),
                CreateDimensionTypeCases(new DateTimeUtc(2024, 1, 2), BuiltInType.DateTime, DataTypeIds.DateTime),
                CreateDimensionTypeCases(new Uuid(new Guid("01234567-89ab-cdef-0123-456789abcdef")),
                    BuiltInType.Guid, DataTypeIds.Guid),
                CreateDimensionTypeCases(ByteString.From(1, 2), BuiltInType.ByteString, DataTypeIds.ByteString),
                CreateDimensionTypeCases(XmlElement.From("<dimension/>"), BuiltInType.XmlElement,
                    DataTypeIds.XmlElement),
                CreateDimensionTypeCases(new NodeId(17), BuiltInType.NodeId, DataTypeIds.NodeId),
                CreateDimensionTypeCases(new ExpandedNodeId(17), BuiltInType.ExpandedNodeId,
                    DataTypeIds.ExpandedNodeId),
                CreateDimensionTypeCases(StatusCodes.BadNoData, BuiltInType.StatusCode, DataTypeIds.StatusCode),
                CreateDimensionTypeCases(new QualifiedName("dimension"), BuiltInType.QualifiedName,
                    DataTypeIds.QualifiedName),
                CreateDimensionTypeCases(new LocalizedText("en", "dimension"), BuiltInType.LocalizedText,
                    DataTypeIds.LocalizedText),
                CreateDimensionTypeCases(new ExtensionObject(new Argument { Name = "DimensionValue" }),
                    BuiltInType.ExtensionObject, DataTypeIds.Structure),
                CreateDimensionTypeCases(new DataValue(new Variant(17)), BuiltInType.DataValue, DataTypeIds.DataValue),
                CreateDimensionTypeCases(new Variant(17), BuiltInType.Variant, DataTypeIds.BaseDataType),
                CreateDimensionTypeCases(new EnumValue(1), BuiltInType.Enumeration, DataTypeIds.Enumeration)
            ];
            return cases.SelectMany(value => value);
        }

        private static IEnumerable<TestCaseData> CreateDimensionTypeCases<T>(
            T seed,
            BuiltInType builtInType,
            NodeId dataType)
        {
            ArrayOf<T> values = ArrayOf.Wrapped(seed, seed);
            if (!VariantHelper.TryCastFrom(values, out Variant array) ||
                !VariantHelper.TryCastFrom(values.ToMatrix(1, 2), out Variant matrix))
            {
                throw new InvalidOperationException($"Unsupported dimension seed type {typeof(T).Name}.");
            }
            string test = nameof(AddVariableEnforcesDimensionMaximaForBuiltInTypes);
            yield return new TestCaseData(builtInType, array, dataType, (ArrayOf<uint>)[2], true)
                .SetName($"{test}({builtInType},Array,AtMaximum)");
            yield return new TestCaseData(builtInType, array, dataType, (ArrayOf<uint>)[1], false)
                .SetName($"{test}({builtInType},Array,Oversize)");
            yield return new TestCaseData(builtInType, matrix, dataType, (ArrayOf<uint>)[1, 2], true)
                .SetName($"{test}({builtInType},Matrix,AtMaximum)");
            yield return new TestCaseData(builtInType, matrix, dataType, (ArrayOf<uint>)[1, 1], false)
                .SetName($"{test}({builtInType},Matrix,Oversize)");
        }

        private static async Task AssertVariableDimensionAdmissionAsync(
            Variant value,
            NodeId dataType,
            int valueRank,
            ArrayOf<uint> dimensions,
            bool valid)
        {
            using Harness h = CreateHarness(allowNodeManagement: true);
            var types = (TypeTable)h.Context.TypeTable;
            types.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            if (dataType == DataTypeIds.Enumeration)
            {
                types.AddSubtype(DataTypeIds.Int32, DataTypeIds.BaseDataType);
                types.AddSubtype(dataType, DataTypeIds.Int32);
            }
            else if (dataType != DataTypeIds.BaseDataType)
            {
                types.AddSubtype(dataType, DataTypeIds.BaseDataType);
            }
            NodeId parentId = await h.AddObjectAsync("Parent").ConfigureAwait(false);
            ushort ns = h.Manager.NamespaceIndexes[0];
            var requestedId = new NodeId("DimensionedVariable", ns);
            var item = new AddNodesItem
            {
                ParentNodeId = parentId,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                RequestedNewNodeId = requestedId,
                BrowseName = new QualifiedName("DimensionedVariable", ns),
                NodeClass = NodeClass.Variable,
                TypeDefinition = VariableTypeIds.BaseVariableType,
                NodeAttributes = new ExtensionObject(new VariableAttributes
                {
                    SpecifiedAttributes = (uint)NodeAttributesMask.DataType |
                        (uint)NodeAttributesMask.ValueRank |
                        (uint)NodeAttributesMask.Value |
                        (uint)NodeAttributesMask.ArrayDimensions,
                    DataType = dataType,
                    ValueRank = valueRank,
                    Value = value,
                    ArrayDimensions = dimensions
                })
            };

            (ServiceResult result, NodeId addedId) = await h.Manager.AddNodeAsync(h.OperationContext, item)
                .ConfigureAwait(false);

            if (!valid)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeAttributesInvalid));
                    Assert.That(addedId.IsNull, Is.True);
                    Assert.That(h.Manager.PredefinedNodes.ContainsKey(requestedId), Is.False);
                    Assert.That(h.Manager.PredefinedNodes[parentId].FindChildWithQualifiedName(
                        h.Context, item.BrowseName), Is.Null);
                }
                return;
            }

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(addedId, Is.EqualTo(requestedId));
            BaseVariableState variable = h.Manager.FindPredefinedNode<BaseVariableState>(addedId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(variable.DataType, Is.EqualTo(dataType));
                Assert.That(variable.ValueRank, Is.EqualTo(valueRank));
                Assert.That(variable.Value, Is.EqualTo(value));
                Assert.That(variable.Value.TypeInfo, Is.EqualTo(value.TypeInfo));
                Assert.That(variable.ArrayDimensions, Is.EqualTo(dimensions));
                Assert.That(h.Manager.PredefinedNodes[parentId].FindChildWithQualifiedName(
                    h.Context, item.BrowseName), Is.SameAs(variable));
            }
        }

        private sealed class Harness : System.IDisposable
        {
            public NodeManagementTestNodeManager Manager { get; }
            public ServerSystemContext Context { get; }
            public OperationContext OperationContext { get; }
            public Mock<IMasterNodeManager> MockMasterNodeManager { get; }

            private readonly MonitoredItemQueueFactory m_queueFactory;

            public Harness(
                NodeManagementTestNodeManager manager,
                ServerSystemContext context,
                OperationContext operationContext,
                MonitoredItemQueueFactory queueFactory,
                Mock<IMasterNodeManager> mockMasterNodeManager)
            {
                Manager = manager;
                Context = context;
                OperationContext = operationContext;
                m_queueFactory = queueFactory;
                MockMasterNodeManager = mockMasterNodeManager;
            }

            public async ValueTask<NodeId> AddObjectAsync(string name)
            {
                ushort ns = Manager.NamespaceIndexes[0];
                var obj = new BaseObjectState(null);
                obj.CreateAsPredefinedNode(Context);
                obj.NodeId = new NodeId(name, ns);
                obj.BrowseName = new QualifiedName(name, ns);
                await Manager.AddPredefinedNodeAsyncPublic(Context, obj).ConfigureAwait(false);
                return obj.NodeId;
            }

            public void Dispose()
            {
                m_queueFactory.Dispose();
                Manager.Dispose();
            }
        }

        private sealed class ConstrainedVariableType : BaseVariableTypeState;

        private class NodeManagementTestNodeManager : AsyncCustomNodeManager
        {
            public NodeManagementTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            public ValueTask AddPredefinedNodeAsyncPublic(ISystemContext context, NodeState node, CancellationToken ct = default)
            {
                return AddPredefinedNodeAsync(context, node, ct);
            }

            public void AddPredefinedNodeSynchronouslyPublic(NodeState node)
            {
                AddPredefinedNodeSynchronously(node);
            }

            public void SeedRootNotifier(NodeId nodeId)
            {
                RootNotifiers[nodeId] = null!;
            }
        }

        private sealed class OptedInTestNodeManager : NodeManagementTestNodeManager
        {
            public OptedInTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public override bool AllowNodeManagement => true;
        }
    }
}
