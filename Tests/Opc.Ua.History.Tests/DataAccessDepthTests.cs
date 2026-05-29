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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

// Conformance tests use inline literal arrays as expected-value
// assertions; the per-call allocation cost is irrelevant for tests
// and keeping the literal adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance depth tests for Data Access DataItems, PercentDeadBand,
    /// and TwoState conformance units.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DataAccessDepth")]
    public class DataAccessDepthTests : TestFixture
    {
        [Test]
        public async Task DataItems000BrowseDataItemTypeDefinitionAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task DataItems001TranslateBrowsePathForInt32Async()
        {
            NodeId startNode = ObjectIds.ObjectsFolder;
            ushort ns = (ushort)Session.NamespaceUris.GetIndex(
                Constants.ReferenceServerNamespaceUri);

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new BrowsePath[]
                    {
                        new() {
                            StartingNode = startNode,
                            RelativePath = MakeForwardPath(ns,
                                "CTT", "Scalar", "Scalar_Static", "Scalar_Static_Int32")
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"TranslateBrowsePath should resolve full path to Scalar_Static_Int32: {response.Results[0].StatusCode}");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task DataItems002TranslateBrowsePathForDoubleAsync()
        {
            NodeId startNode = ObjectIds.ObjectsFolder;
            ushort ns = (ushort)Session.NamespaceUris.GetIndex(
                Constants.ReferenceServerNamespaceUri);

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    new BrowsePath[]
                    {
                        new() {
                            StartingNode = startNode,
                            RelativePath = MakeForwardPath(ns,
                                "CTT", "Scalar", "Scalar_Static", "Scalar_Static_Double")
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"TranslateBrowsePath should resolve full path to Scalar_Static_Double: {response.Results[0].StatusCode}");
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThan(0));
        }

        private static RelativePath MakeForwardPath(ushort ns, params string[] names)
        {
            var elements = new RelativePathElement[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                elements[i] = new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IsInverse = false,
                    IncludeSubtypes = true,
                    TargetName = new QualifiedName(names[i], ns)
                };
            }
            return new RelativePath { Elements = elements.ToArrayOf() };
        }

        [Test]
        public async Task DataItems003ReadValueAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TypeInfo, Is.Not.Null);
        }

        [Test]
        public async Task DataItems004ReadDisplayNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.DisplayName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out LocalizedText _), Is.True);
        }

        [Test]
        public async Task DataItems005ReadBrowseNameAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.BrowseName)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out QualifiedName _), Is.True);
        }

        [Test]
        public async Task DataItems006ReadNodeClassAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.NodeClass)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int nodeClass = (int)result.WrappedValue.GetInt32();
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Variable));
        }

        [Test]
        public async Task DataItems007ReadDataTypeAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.DataType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out NodeId _), Is.True);
        }

        [Test]
        public async Task DataItems008ReadAccessLevelAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.AccessLevel)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task DataItems009ReadValueRankAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(nodeId, Attributes.ValueRank)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int valueRank = result.WrappedValue.GetInt32();
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public async Task DataItems010WriteInt32ValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const int testValue = 12345;

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testValue))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(readBack.WrappedValue.GetInt32(),
                Is.EqualTo(testValue));
        }

        [Test]
        public async Task DataItems011WriteDoubleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            const double testValue = 2.71828;

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testValue))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(readBack.WrappedValue.GetDouble(),
                Is.EqualTo(testValue).Within(0.0001));
        }

        [Test]
        public async Task DataItems012WriteStringValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);
            const string testValue = "DepthTestString";

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testValue))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(readBack.WrappedValue.GetString(),
                Is.EqualTo(testValue));
        }

        [Test]
        public async Task DataItems013WriteBooleanValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            const bool testValue = true;

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testValue))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(readBack.WrappedValue.GetBoolean(),
                Is.EqualTo(testValue));
        }

        [Test]
        public async Task DataItems014BatchReadMultipleNodesAsync()
        {
            var readValueIds = Constants.ScalarStaticNodes
                .Select(n => new ReadValueId
                {
                    NodeId = ToNodeId(n),
                    AttributeId = Attributes.Value
                }).ToArrayOf();

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                readValueIds,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(readValueIds.Count));
            foreach (DataValue result in response.Results)
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task DataItems015BatchWriteMultipleNodesAsync()
        {
            NodeId intNode = ToNodeId(Constants.ScalarStaticInt32);
            NodeId doubleNode = ToNodeId(Constants.ScalarStaticDouble);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = intNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(99))
                    },
                    new() {
                        NodeId = doubleNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(9.9))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
            Assert.That(StatusCode.IsGood(response.Results[1]), Is.True);
        }

        [Test]
        public async Task DataItems016ReadArrayWithIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            int[] testArray = [10, 20, 30, 40, 50];
            StatusCode writeStatus = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testArray))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(writeStatus), Is.True);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = "1:3"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task DataItems017WriteArrayWithIndexRangeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);

            int[] testArray = [100, 200, 300, 400, 500];
            StatusCode writeStatus = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(testArray))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(writeStatus), Is.True);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(new int[] { 999, 888 })),
                        IndexRange = "1:2"
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(readBack.StatusCode), Is.True);
        }

        [Test]
        public async Task DataItems018ReadDefinitionPropertyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            ReferenceDescription[] children =
                await BrowseChildrenAsync(nodeId).ConfigureAwait(false);

            ReferenceDescription definition = children
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.Definition);

            if (definition == null)
            {
                Assert.Ignore("Definition property is not available on this node.");
                return;
            }

            var defNodeId = ExpandedNodeId.ToNodeId(
                definition.NodeId, Session.NamespaceUris);
            DataValue result = await ReadValueAsync(defNodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task DataItems019ReadValuePrecisionPropertyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            ReferenceDescription[] children =
                await BrowseChildrenAsync(nodeId).ConfigureAwait(false);

            ReferenceDescription precision = children
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.ValuePrecision);

            if (precision == null)
            {
                Assert.Ignore(
                    "ValuePrecision property is not available on this node.");
                return;
            }

            var precNodeId = ExpandedNodeId.ToNodeId(
                precision.NodeId, Session.NamespaceUris);
            DataValue result = await ReadValueAsync(precNodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task DataItems020ReadWithDifferentTimestampsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            foreach (TimestampsToReturn tsReturn in new[]
            {
                TimestampsToReturn.Source,
                TimestampsToReturn.Server,
                TimestampsToReturn.Both,
                TimestampsToReturn.Neither
            })
            {
                ReadResponse response = await Session.ReadAsync(
                    null, 0, tsReturn,
                    new ReadValueId[]
                    {
                        new() {
                            NodeId = nodeId,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            }
        }

        [Test]
        public async Task DataItemsErr001WriteWrongTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant("WrongTypeString"))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task DataItemsErr002ReadInvalidNodeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task PercentDeadBand001ReadEuRangeAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            ReferenceDescription[] children =
                await BrowseChildrenAsync(analogNode).ConfigureAwait(false);

            ReferenceDescription euRange = children
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.EURange);

            if (euRange == null)
            {
                Assert.Fail("EURange property is not available.");
                return;
            }

            var euRangeId = ExpandedNodeId.ToNodeId(
                euRange.NodeId, Session.NamespaceUris);
            DataValue result = await ReadValueAsync(euRangeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task PercentDeadBand002ReadInstrumentRangeAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            ReferenceDescription[] children =
                await BrowseChildrenAsync(analogNode).ConfigureAwait(false);

            ReferenceDescription instrRange = children
                .FirstOrDefault(r => r.BrowseName.Name == BrowseNames.InstrumentRange);

            if (instrRange == null)
            {
                Assert.Fail("InstrumentRange property is not available.");
                return;
            }

            var instrRangeId = ExpandedNodeId.ToNodeId(
                instrRange.NodeId, Session.NamespaceUris);
            DataValue result = await ReadValueAsync(instrRangeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task PercentDeadBand003ReadEngineeringUnitsAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            ReferenceDescription[] children =
                await BrowseChildrenAsync(analogNode).ConfigureAwait(false);

            ReferenceDescription engUnits = children
                .FirstOrDefault(r => r.BrowseName.Name ==
                    BrowseNames.EngineeringUnits);

            if (engUnits == null)
            {
                Assert.Fail("EngineeringUnits property is not available.");
                return;
            }

            var engUnitsId = ExpandedNodeId.ToNodeId(
                engUnits.NodeId, Session.NamespaceUris);
            DataValue result = await ReadValueAsync(engUnitsId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task PercentDeadBand004CreateSubscriptionForAnalogAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                Assert.That(subId, Is.GreaterThan(0));
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand005MonitorWithAbsoluteDeadbandAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Absolute, 5.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand006MonitorWithPercentDeadbandAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand007PercentDeadbandZeroAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 0.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand008PercentDeadbandHundredAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 100.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand009ModifyMonitoredItemDeadbandAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0));

                CreateMonitoredItemsResponse createResp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
                uint monId = createResp.Results[0].MonitoredItemId;

                ModifyMonitoredItemsResponse modResp =
                    await Session.ModifyMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new MonitoredItemModifyRequest[]
                        {
                            new() {
                                MonitoredItemId = monId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 100,
                                    Filter = MakeDeadbandFilter(
                                        (uint)DeadbandType.Percent, 25.0),
                                    DiscardOldest = true,
                                    QueueSize = 10
                                }
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(modResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand010DeleteMonitoredItemAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 5.0));

                CreateMonitoredItemsResponse createResp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
                uint monId = createResp.Results[0].MonitoredItemId;

                DeleteMonitoredItemsResponse delResp =
                    await Session.DeleteMonitoredItemsAsync(
                        null, subId,
                        new uint[] { monId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(delResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(delResp.Results[0]), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand011StatusChangeTriggerAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0,
                        DataChangeTrigger.Status));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand012StatusValueTimestampTriggerAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0,
                        DataChangeTrigger.StatusValueTimestamp));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand013MultipleMonitoredItemsAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item1 = CreateMonitoredItemRequest(
                    analogNode, clientHandle: 1, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 5.0));

                MonitoredItemCreateRequest item2 = CreateMonitoredItemRequest(
                    ToNodeId(Constants.AnalogTypeInt32), clientHandle: 2,
                    filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 15.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item1, item2 }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(2));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
                Assert.That(
                    StatusCode.IsGood(resp.Results[1].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand014AbsoluteDeadbandSmallValueAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Absolute, 0.001));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand015MonitorWithNoDeadbandAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.None, 0.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand016ModifySubscriptionIntervalAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(500).ConfigureAwait(false);
            try
            {
                ModifySubscriptionResponse modResp =
                    await Session.ModifySubscriptionAsync(
                        null, subId, 1000, 100, 10, 0, 0,
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    modResp.RevisedPublishingInterval, Is.GreaterThan(0));
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand017SetPublishingModeDisableAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                SetPublishingModeResponse disableResp =
                    await Session.SetPublishingModeAsync(
                        null, false,
                        new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(disableResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(disableResp.Results[0]), Is.True);

                SetPublishingModeResponse enableResp =
                    await Session.SetPublishingModeAsync(
                        null, true,
                        new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(enableResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(enableResp.Results[0]), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBand018DeadbandWithQueueSizeOneAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, queueSize: 1,
                    filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBandErr001NegativeDeadbandValueAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, -5.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBandErr002PercentDeadbandExceedsHundredAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 150.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBandErr003InvalidDeadbandTypeAsync()
        {
            NodeId analogNode = ToNodeId(Constants.AnalogTypeDouble);
            DataValue value = await ReadValueAsync(analogNode).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode))
            {
                Assert.Fail("AnalogTypeDouble node is not accessible.");
                return;
            }

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    analogNode, filter: MakeDeadbandFilter(99, 10.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBandErr004DeadbandOnNonAnalogNodeAsync()
        {
            NodeId stringNode = ToNodeId(Constants.ScalarStaticString);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    stringNode, filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PercentDeadBandErr005MonitorInvalidNodeAsync()
        {
            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    Constants.InvalidNodeId,
                    filter: MakeDeadbandFilter(
                        (uint)DeadbandType.Percent, 10.0));

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState000ReadBooleanValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task TwoState001ReadBooleanDisplayNameAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.DisplayName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out LocalizedText _), Is.True);
        }

        [Test]
        public async Task TwoState002ReadBooleanBrowseNameAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out QualifiedName _), Is.True);
        }

        [Test]
        public async Task TwoState003ReadBooleanNodeClassAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.NodeClass).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int nodeClass = (int)result.WrappedValue.GetInt32();
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Variable));
        }

        [Test]
        public async Task TwoState004ReadBooleanDataTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.DataType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            var dataType = result.WrappedValue.GetNodeId();
            Assert.That(dataType, Is.EqualTo(DataTypeIds.Boolean));
        }

        [Test]
        public async Task TwoState005WriteTrueValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(true))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId)
                .ConfigureAwait(false);
            Assert.That(
                readBack.WrappedValue.GetBoolean(),
                Is.True);
        }

        [Test]
        public async Task TwoState006WriteFalseValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(false))).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId)
                .ConfigureAwait(false);
            Assert.That(
                readBack.WrappedValue.GetBoolean(),
                Is.False);
        }

        [Test]
        public async Task TwoState007ToggleBooleanValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            DataValue original = await ReadValueAsync(nodeId)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(original.StatusCode), Is.True);
            bool originalValue = original.WrappedValue.GetBoolean();

            StatusCode status = await WriteValueAsync(
                nodeId,
                new DataValue(new Variant(!originalValue)))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(status), Is.True);

            DataValue readBack = await ReadValueAsync(nodeId)
                .ConfigureAwait(false);
            Assert.That(
                readBack.WrappedValue.GetBoolean(),
                Is.EqualTo(!originalValue));
        }

        [Test]
        public async Task TwoState008ReadAccessLevelAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task TwoState009ReadValueRankAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.ValueRank).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            int valueRank = result.WrappedValue.GetInt32();
            Assert.That(valueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public async Task TwoState66001CreateSubscriptionForBooleanAsync()
        {
            uint subId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                Assert.That(subId, Is.GreaterThan(0));
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66002MonitorBooleanValueChangesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(nodeId);

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66003MonitorWithStatusTriggerAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                var filter = new ExtensionObject(new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.Status,
                    DeadbandType = (uint)DeadbandType.None,
                    DeadbandValue = 0.0
                });

                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    nodeId, filter: filter);

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66004MonitorWithStatusValueTriggerAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                var filter = new ExtensionObject(new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.None,
                    DeadbandValue = 0.0
                });

                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    nodeId, filter: filter);

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66005ModifyMonitoredItemSamplingIntervalAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(nodeId);

                CreateMonitoredItemsResponse createResp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(createResp.Results[0].StatusCode),
                    Is.True);
                uint monId = createResp.Results[0].MonitoredItemId;

                ModifyMonitoredItemsResponse modResp =
                    await Session.ModifyMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new MonitoredItemModifyRequest[]
                        {
                            new() {
                                MonitoredItemId = monId,
                                RequestedParameters = new MonitoringParameters
                                {
                                    ClientHandle = 1,
                                    SamplingInterval = 500,
                                    Filter = default,
                                    DiscardOldest = true,
                                    QueueSize = 10
                                }
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(modResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66006DeleteMonitoredItemAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(nodeId);

                CreateMonitoredItemsResponse createResp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(createResp.Results[0].StatusCode),
                    Is.True);
                uint monId = createResp.Results[0].MonitoredItemId;

                DeleteMonitoredItemsResponse delResp =
                    await Session.DeleteMonitoredItemsAsync(
                        null, subId,
                        new uint[] { monId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(delResp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(delResp.Results[0]), Is.True);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoState66007DeleteSubscriptionAsync()
        {
            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);

            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);
            MonitoredItemCreateRequest item = CreateMonitoredItemRequest(nodeId);

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, subId, TimestampsToReturn.Both,
                    new[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            DeleteSubscriptionsResponse delResp =
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(delResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);
        }

        [Test]
        public async Task TwoStateErr001WriteWrongTypeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant("NotABoolean"))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task TwoStateErr002ReadInvalidNodeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task TwoStateErr003WriteInvalidNodeAsync()
        {
            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(true))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task TwoStateErr004WriteInvalidAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.NodeClass,
                        Value = new DataValue(
                            new Variant((int)NodeClass.Method))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task TwoStateErr005ReadInvalidAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = 999
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsBad(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        public async Task TwoStateErr006MonitorInvalidNodeAsync()
        {
            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest item = CreateMonitoredItemRequest(
                    Constants.InvalidNodeId);

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoStateErr007MonitorInvalidAttributeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            uint subId = await CreateSubscriptionAsync(250).ConfigureAwait(false);
            try
            {
                var item = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = 999
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 100,
                        Filter = default,
                        DiscardOldest = true,
                        QueueSize = 10
                    }
                };

                CreateMonitoredItemsResponse resp =
                    await Session.CreateMonitoredItemsAsync(
                        null, subId, TimestampsToReturn.Both,
                        new[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(resp.Results.Count, Is.EqualTo(1));
                Assert.That(
                    StatusCode.IsGood(resp.Results[0].StatusCode), Is.False);
            }
            finally
            {
                await DeleteSubscriptionAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task TwoStateErr008WriteInt32ToBooleanNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(42))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task TwoStateErr009WriteDoubleToBooleanNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(3.14))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.False);
        }

        [Test]
        public async Task TwoStateErr010BatchReadWithOneInvalidAsync()
        {
            NodeId validNode = ToNodeId(Constants.ScalarStaticBoolean);

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = validNode,
                        AttributeId = Attributes.Value
                    },
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsBad(response.Results[1].StatusCode), Is.True);
        }

        [Test]
        public async Task TwoStateErr011BatchWriteWithOneInvalidAsync()
        {
            NodeId validNode = ToNodeId(Constants.ScalarStaticBoolean);

            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = validNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(true))
                    },
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(true))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(response.Results[0]), Is.True);
            Assert.That(
                StatusCode.IsGood(response.Results[1]), Is.False);
        }

        private async Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<DataValue> ReadAttributeAsync(NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<StatusCode> WriteValueAsync(NodeId nodeId, DataValue value)
        {
            WriteResponse response = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<ReferenceDescription[]> BrowseChildrenAsync(
            NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return response.Results[0].References.ToArray();
        }

        private async Task<uint> CreateSubscriptionAsync(
            double interval = 500,
            uint lifetime = 100,
            uint keepAlive = 10)
        {
            CreateSubscriptionResponse response =
                await Session.CreateSubscriptionAsync(
                    null, interval, lifetime, keepAlive, 0, true, 0,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.SubscriptionId, Is.GreaterThan(0));
            return response.SubscriptionId;
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { subscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private MonitoredItemCreateRequest CreateMonitoredItemRequest(
            NodeId nodeId,
            uint clientHandle = 1,
            double samplingInterval = 100,
            uint queueSize = 10,
            ExtensionObject filter = default)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    Filter = filter,
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            };
        }

        private ExtensionObject MakeDeadbandFilter(
            uint deadbandType,
            double deadbandValue,
            DataChangeTrigger trigger = DataChangeTrigger.StatusValue)
        {
            return new ExtensionObject(new DataChangeFilter
            {
                Trigger = trigger,
                DeadbandType = deadbandType,
                DeadbandValue = deadbandValue
            });
        }
    }
}
