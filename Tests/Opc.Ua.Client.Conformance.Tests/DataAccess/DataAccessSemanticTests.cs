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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Data Access semantic checks:
    /// write-readback, EURange validation, MultiStateValueDiscrete,
    /// DataItem, subscriptions, and type definitions.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DataAccessSemantic")]
    public class DataAccessSemanticTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task WriteAnalogAndReadBackAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_AnalogType_Double");
            const double writeVal = 42.5;

            WriteResponse wr = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(writeVal))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(wr.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(wr.Results[0]))
            {
                Assert.Fail($"Write not permitted: {wr.Results[0]}");
            }

            DataValue readBack =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(readBack.StatusCode), Is.True);
            Assert.That(
                readBack.WrappedValue.GetDouble(),
                Is.EqualTo(writeVal).Within(0.01));
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task AnalogItemTypeDefinitionHasEURangeAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_AnalogType_Double");
            NodeId euRangeId =
                await FindChildNodeAsync(nodeId, "EURange")
                    .ConfigureAwait(false);
            Assert.That(euRangeId.IsNull, Is.False,
                "AnalogItemType should have EURange as mandatory child.");
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task AnalogItemHasEngineeringUnitsAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_AnalogType_Double");
            NodeId euId =
                await FindChildNodeAsync(nodeId, "EngineeringUnits")
                    .ConfigureAwait(false);
            Assert.That(euId.IsNull, Is.False,
                "AnalogItemType should have EngineeringUnits.");
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task WriteOutsideEURangeHandledGracefullyAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_AnalogType_Double");
            NodeId euRangeId =
                await FindChildNodeAsync(nodeId, "EURange")
                    .ConfigureAwait(false);
            if (euRangeId.IsNull)
            {
                Assert.Fail("EURange not found.");
            }

            DataValue rangeVal =
                await ReadNodeValueAsync(euRangeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(rangeVal.StatusCode))
            {
                Assert.Fail("Cannot read EURange.");
            }

            Range range = rangeVal.GetValue<Range>(default);
            if (range == null)
            {
                Assert.Fail("EURange is null.");
            }

            double outOfRange = range.High + 1000.0;
            WriteResponse wr = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(outOfRange))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(wr.Results.Count, Is.EqualTo(1));
            // Server may accept it (clamping) or reject with BadOutOfRange
            Assert.That(
                StatusCode.IsGood(wr.Results[0]) ||
                wr.Results[0].Code == StatusCodes.BadOutOfRange ||
                wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                Is.True,
                $"Unexpected status: {wr.Results[0]}");
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task ReadMultiStateValueDiscreteEnumValuesAsync()
        {
            NodeId nodeId =
                DaNodeId("DataAccess_MultiStateValueDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiStateValueDiscrete node not accessible.");
            }

            NodeId enumValuesId =
                await FindChildNodeAsync(nodeId, "EnumValues")
                    .ConfigureAwait(false);
            if (enumValuesId.IsNull)
            {
                Assert.Ignore("EnumValues child not found.");
            }

            DataValue result =
                await ReadNodeValueAsync(enumValuesId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task WriteValidMultiStateValueAsync()
        {
            NodeId nodeId =
                DaNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState node not accessible.");
            }

            // Write value 0 (first valid state)
            WriteResponse wr = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            new Variant(Convert.ToUInt32(0)))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(wr.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(wr.Results[0]))
            {
                Assert.Ignore($"Write not permitted: {wr.Results[0]}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task WriteInvalidMultiStateValueAsync()
        {
            NodeId nodeId =
                DaNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState node not accessible.");
            }

            // Write an extremely large value that should be out of range
            WriteResponse wr = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(
                            new Variant(Convert.ToUInt32(999999)))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(wr.Results.Count, Is.EqualTo(1));
            // Server may accept or reject; both are valid
            Assert.That(
                StatusCode.IsGood(wr.Results[0]) ||
                wr.Results[0].Code == StatusCodes.BadOutOfRange ||
                wr.Results[0].Code == StatusCodes.BadUserAccessDenied,
                Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task ReadDataItemValueAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_DataItem_Double");
            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task DataItemHasDefinitionPropertyAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_DataItem_Double");
            NodeId defId =
                await FindChildNodeAsync(nodeId, "Definition")
                    .ConfigureAwait(false);
            // Definition is optional
            if (defId.IsNull)
            {
                Assert.Fail("Definition property not present.");
            }

            DataValue result =
                await ReadNodeValueAsync(defId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task AnalogTypeHasTypeDefinitionAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_AnalogType_Double");

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].References.Count,
                Is.GreaterThan(0),
                "AnalogType node should have HasTypeDefinition reference.");
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task DataItemHasTypeDefinitionAsync()
        {
            NodeId nodeId = DaNodeId("DataAccess_DataItem_Double");

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].References.Count,
                Is.GreaterThan(0),
                "DataItem node should have HasTypeDefinition reference.");
        }

        [Test]
        [Property("ConformanceUnit", "Data Access Semantic Changes")]
        [Property("Tag", "N/A")]
        public async Task ReadAnalogArrayItemValueAsync()
        {
            NodeId nodeId =
                DaNodeId("DataAccess_AnalogType_Array_Double");
            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail(
                    $"AnalogType_Array_Double not accessible: {result.StatusCode}");
            }
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<NodeId> FindChildNodeAsync(
            NodeId parentId, string browseName)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = parentId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId =
                            ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            foreach (ReferenceDescription r in response.Results[0].References)
            {
                if (r.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(
                        r.NodeId, Session.NamespaceUris);
                }
            }

            return NodeId.Null;
        }

        private NodeId DaNodeId(string identifier)
        {
            return ToNodeId(new ExpandedNodeId(
                identifier, Constants.ReferenceServerNamespaceUri));
        }
    }
}
