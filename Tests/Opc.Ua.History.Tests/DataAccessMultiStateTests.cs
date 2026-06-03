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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for MultiState discrete types and
    /// semantic change verification across Data Access nodes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DataAccessMultiState")]
    public class DataAccessMultiStateTests : TestFixture
    {
        [Test]
        public async Task ReadMultiStateDiscreteValueAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    "MultiState discrete node not accessible: " +
                    $"{result.StatusCode}");
            }

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMultiStateDiscreteEnumStringsAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState discrete node not accessible.");
            }

            NodeId enumStringsId =
                await FindChildNodeAsync(nodeId, "EnumStrings")
                    .ConfigureAwait(false);
            if (enumStringsId.IsNull)
            {
                Assert.Ignore("EnumStrings child not found.");
            }

            DataValue result =
                await ReadNodeValueAsync(enumStringsId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out ArrayOf<LocalizedText> _), Is.True,
                "EnumStrings value must be an array.");
        }

        [Test]
        public async Task WriteValidMultiStateIndexAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState discrete node not accessible.");
            }

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((uint)0))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(writeResponse.Results[0]))
            {
                Assert.Ignore(
                    "MultiState write not permitted: " +
                    $"{writeResponse.Results[0]}");
            }
        }

        [Test]
        public async Task ReadMultiStateValueAfterWriteAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState discrete node not accessible.");
            }

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant((uint)0))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(writeResponse.Results[0]))
            {
                Assert.Ignore(
                    "MultiState write not permitted: " +
                    $"{writeResponse.Results[0]}");
            }

            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.WrappedValue.GetUInt32(), Is.Zero);
        }

        [Test]
        public async Task VerifyAnalogItemTypeHasTypeDefinitionAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_AnalogType_Double");

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
                "AnalogType_Double must have a HasTypeDefinition reference.");
        }

        [Test]
        public async Task ReadAnalogItemInstrumentRangeAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_AnalogType_Double");
            NodeId instrumentRangeId =
                await FindChildNodeAsync(nodeId, "InstrumentRange")
                    .ConfigureAwait(false);
            if (instrumentRangeId.IsNull)
            {
                Assert.Fail("InstrumentRange child not found.");
            }

            DataValue result =
                await ReadNodeValueAsync(instrumentRangeId)
                    .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task VerifyMultiStateDiscreteHasTypeDefinitionAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_MultiStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("MultiState discrete node not accessible.");
            }

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
                "MultiState discrete must have a HasTypeDefinition " +
                "reference.");
        }

        [Test]
        public async Task ReadDataAccessTwoStateDiscreteValueAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_TwoStateDiscreteType_001");
            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    "TwoState discrete node not accessible: " +
                    $"{result.StatusCode}");
            }

            Assert.That(
                result.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        public async Task ReadTwoStateDiscreteFalseStateAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_TwoStateDiscreteType_001");
            DataValue check =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            if (!StatusCode.IsGood(check.StatusCode))
            {
                Assert.Ignore("TwoState discrete node not accessible.");
            }

            NodeId falseStateId =
                await FindChildNodeAsync(nodeId, "FalseState")
                    .ConfigureAwait(false);
            if (falseStateId.IsNull)
            {
                Assert.Ignore("FalseState child not found.");
            }

            DataValue result =
                await ReadNodeValueAsync(falseStateId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.WrappedValue.TryGetValue(out LocalizedText _), Is.True,
                "FalseState must be a LocalizedText value.");
        }

        [Test]
        public async Task ReadAnalogItemDoubleEURangeHighGreaterThanLowAsync()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_AnalogType_Double");
            NodeId euRangeId =
                await FindChildNodeAsync(nodeId, "EURange")
                    .ConfigureAwait(false);
            Assert.That(euRangeId.IsNull, Is.False,
                "EURange child not found.");

            DataValue result =
                await ReadNodeValueAsync(euRangeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            Range range = result.GetValue<Range>(default);
            Assert.That(range, Is.Not.Null, "EURange must not be null.");
            Assert.That(range.High, Is.GreaterThanOrEqualTo(range.Low),
                "EURange High must be >= Low.");
        }

        [Test]
        public async Task WriteAndReadBackAnalogItemInt32Async()
        {
            NodeId nodeId =
                MultiStateNodeId("DataAccess_AnalogType_Int32");
            const int testValue = 42;

            WriteResponse writeResponse = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(testValue))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResponse.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(writeResponse.Results[0]))
            {
                Assert.Fail(
                    "AnalogType_Int32 write not permitted: " +
                    $"{writeResponse.Results[0]}");
            }

            DataValue result =
                await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(
                result.WrappedValue.GetInt32(), Is.EqualTo(testValue));
        }

        [Test]
        public async Task VerifyDataAccessVariableTypeExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableTypeIds.DataItemType,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                "DataItemType variable type must exist in the server.");
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

        private NodeId MultiStateNodeId(string identifier)
        {
            return ToNodeId(new ExpandedNodeId(
                identifier, Constants.ReferenceServerNamespaceUri));
        }
    }
}
