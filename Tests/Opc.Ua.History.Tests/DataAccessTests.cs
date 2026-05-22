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

namespace Opc.Ua.History.Tests
{
    /// <summary>
    /// compliance tests for Data Access nodes (AnalogItem, DiscreteItem, arrays).
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("DataAccess")]
    public class DataAccessTests : TestFixture
    {
        [Test]
        public async Task ReadScalarInt32ValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadScalarDoubleValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            DataValue result = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadScalarStringValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);
            DataValue result = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadBooleanArrayValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayBoolean);
            DataValue result = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadInt32ArrayValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticArrayInt32);
            DataValue result = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task WriteAndReadBackScalarInt32Async()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
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
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            DataValue readResult = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(readResult.StatusCode), Is.True);
            Assert.That(readResult.WrappedValue.GetInt32(), Is.EqualTo(testValue));
        }

        [Test]
        public async Task WriteAndReadBackScalarDoubleAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticDouble);
            const double testValue = 3.14159;

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
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            DataValue readResult = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(readResult.StatusCode), Is.True);
            Assert.That(readResult.WrappedValue.GetDouble(), Is.EqualTo(testValue).Within(0.0001));
        }

        [Test]
        public async Task WriteAndReadBackStringAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticString);
            const string testValue = "Test String";

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
            Assert.That(StatusCode.IsGood(writeResponse.Results[0]), Is.True);

            DataValue readResult = await ReadNodeValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(readResult.StatusCode), Is.True);
            Assert.That(readResult.WrappedValue.GetString(), Is.EqualTo(testValue));
        }

        [Test]
        public async Task ReadAllScalarStaticNodesSucceedsAsync()
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

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
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
    }
}
