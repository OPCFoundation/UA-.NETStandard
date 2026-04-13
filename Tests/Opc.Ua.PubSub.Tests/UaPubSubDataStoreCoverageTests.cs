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
using NUnit.Framework;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Coverage tests for UaPubSubDataStore class")]
    public class UaPubSubDataStoreCoverageTests
    {
        [Test]
        public void ConstructorCreatesEmptyStore()
        {
            var store = new UaPubSubDataStore();
            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void WritePublishedDataItemVariantOverloadStoresValue()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Variant.From(42));
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.WrappedValue.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void WritePublishedDataItemVariantOverloadSetsStatusCode()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Variant.From(10), status: StatusCodes.Good);
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void WritePublishedDataItemVariantOverloadSetsTimestamp()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            var ts = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            store.WritePublishedDataItem(nodeId, Variant.From(10), timestamp: ts);
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.SourceTimestamp, Is.EqualTo(ts));
        }

        [Test]
        public void WritePublishedDataItemVariantOverloadThrowsOnNullNodeId()
        {
            var store = new UaPubSubDataStore();
            Assert.Throws<ArgumentException>(
                () => store.WritePublishedDataItem(NodeId.Null, Variant.From(1)));
        }

        [Test]
        public void WritePublishedDataItemDataValueOverloadStoresValue()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            var dv = new DataValue(new Variant(true));
            store.WritePublishedDataItem(nodeId, Attributes.Value, dv);
            DataValue result = store.ReadPublishedDataItem(nodeId, Attributes.Value);
            Assert.That(result, Is.SameAs(dv));
        }

        [Test]
        public void WritePublishedDataItemDataValueOverloadThrowsOnNullNodeId()
        {
            var store = new UaPubSubDataStore();
            Assert.Throws<ArgumentException>(
                () => store.WritePublishedDataItem(NodeId.Null, Attributes.Value, null));
        }

        [Test]
        public void WritePublishedDataItemDataValueOverloadDefaultsAttributeZeroToValue()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            var dv = new DataValue(new Variant(99));
            // attributeId 0 should default to Attributes.Value
            store.WritePublishedDataItem(nodeId, 0, dv);
            DataValue result = store.ReadPublishedDataItem(nodeId, Attributes.Value);
            Assert.That(result, Is.SameAs(dv));
        }

        [Test]
        public void WritePublishedDataItemDataValueOverloadThrowsOnInvalidAttributeId()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            Assert.Throws<ArgumentException>(
                () => store.WritePublishedDataItem(nodeId, 99999, null));
        }

        [Test]
        public void WritePublishedDataItemDataValueOverwritesExistingValue()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            var dv1 = new DataValue(new Variant(1));
            var dv2 = new DataValue(new Variant(2));
            store.WritePublishedDataItem(nodeId, Attributes.Value, dv1);
            store.WritePublishedDataItem(nodeId, Attributes.Value, dv2);
            DataValue result = store.ReadPublishedDataItem(nodeId, Attributes.Value);
            Assert.That(result, Is.SameAs(dv2));
        }

        [Test]
        public void ReadPublishedDataItemReturnsNullForMissingNode()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("Missing", 2);
            DataValue result = store.ReadPublishedDataItem(nodeId, Attributes.Value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadPublishedDataItemReturnsNullForMissingAttribute()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Attributes.Value,
                new DataValue(new Variant(42)));
            DataValue result = store.ReadPublishedDataItem(nodeId, Attributes.NodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadPublishedDataItemThrowsOnNullNodeId()
        {
            var store = new UaPubSubDataStore();
            Assert.Throws<ArgumentException>(
                () => store.ReadPublishedDataItem(NodeId.Null));
        }

        [Test]
        public void ReadPublishedDataItemDefaultsAttributeZeroToValue()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            var dv = new DataValue(new Variant(77));
            store.WritePublishedDataItem(nodeId, Attributes.Value, dv);
            // attributeId 0 should default to Attributes.Value
            DataValue result = store.ReadPublishedDataItem(nodeId, 0);
            Assert.That(result, Is.SameAs(dv));
        }

        [Test]
        public void ReadPublishedDataItemThrowsOnInvalidAttributeId()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            Assert.Throws<ArgumentException>(
                () => store.ReadPublishedDataItem(nodeId, 99999));
        }

        [Test]
        public void UpdateMetaDataDoesNotThrow()
        {
            var store = new UaPubSubDataStore();
            var pds = new PublishedDataSetDataType { Name = "Test" };
            Assert.DoesNotThrow(() => store.UpdateMetaData(pds));
        }

        [Test]
        public void UpdateMetaDataAcceptsNull()
        {
            var store = new UaPubSubDataStore();
            Assert.DoesNotThrow(() => store.UpdateMetaData(null));
        }

        [Test]
        public void WriteVariantOverloadOverwritesExistingNode()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Variant.From(1));
            store.WritePublishedDataItem(nodeId, Variant.From(2));
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result.WrappedValue.GetInt32(), Is.EqualTo(2));
        }

        [Test]
        public void WriteAndReadMultipleNodes()
        {
            var store = new UaPubSubDataStore();
            var node1 = new NodeId("Node1", 2);
            var node2 = new NodeId("Node2", 2);
            store.WritePublishedDataItem(node1, Attributes.Value,
                new DataValue(new Variant(100)));
            store.WritePublishedDataItem(node2, Attributes.Value,
                new DataValue(new Variant(200)));
            Assert.That(
                store.ReadPublishedDataItem(node1).WrappedValue.GetInt32(),
                Is.EqualTo(100));
            Assert.That(
                store.ReadPublishedDataItem(node2).WrappedValue.GetInt32(),
                Is.EqualTo(200));
        }

        [Test]
        public void WriteDataValueNullIsStoredAsNull()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Attributes.Value, null);
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void WriteVariantDefaultsStatusToGood()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId("TestNode", 2);
            store.WritePublishedDataItem(nodeId, Variant.From(5));
            DataValue result = store.ReadPublishedDataItem(nodeId);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
        }
    }
}
