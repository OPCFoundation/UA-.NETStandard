/* ====/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture(Description = "Tests for UaPubSubDataStore class")]
    [Parallelizable]
    public class UaPubSubDataStoreTests
    {
        [Test(Description = "Validate WritePublishedDataItem call with different values")]
        public void ValidateWritePublishedDataItem(
            [Values(true, (byte)1, (ushort)2, (short)3, (uint)4, 5, (ulong)6, (long)7, (double)8, (float)9, "10")]
                object value
        )
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();
            var nodeId = new NodeId("ns=1;i=1");

            //Act
            dataStore.WritePublishedDataItem(nodeId, Attributes.Value, new DataValue(new Variant(value)));
            DataValue readDataValue = dataStore.ReadPublishedDataItem(nodeId, Attributes.Value);

            //Assert
            Assert.IsNotNull(readDataValue, "Returned DataValue for written nodeId and attribute is null");
            Assert.AreEqual(readDataValue.Value, value, "Read after write returned different value");
        }

        [Test(Description = "Validate WritePublishedDataItem call with null NodeId")]
        public void ValidateWritePublishedDataItemNullNodeId()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();

            //Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(() => dataStore.WritePublishedDataItem(null));
        }

        [Test(Description = "Validate WritePublishedDataItem call with invalid Attribute")]
        public void ValidateWritePublishedDataItemInvalidAttribute()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();

            //Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                dataStore.WritePublishedDataItem(new NodeId("ns=0;i=2253"), Attributes.AccessLevelEx + 1)
            );
        }

        [Test(Description = "Validate ReadPublishedDataItem call for non existing node id")]
        public void ValidateReadPublishedDataItem()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();
            var nodeId = new NodeId("ns=1;i=1");

            //Act
            DataValue readDataValue = dataStore.ReadPublishedDataItem(nodeId, Attributes.Value);

            //Assert
            Assert.IsNull(readDataValue, "Returned DataValue for written nodeId and attribute is NOT null");
        }

        [Test(Description = "Validate ReadPublishedDataItem call with null NodeId")]
        public void ValidateReadPublishedDataItemNullNodeId()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();

            //Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(() => dataStore.ReadPublishedDataItem(null));
        }

        [Test(Description = "Validate ReadPublishedDataItem call with invalid Attribute")]
        public void ValidateReadPublishedDataIteminvalidAttribute()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();
            //Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                dataStore.ReadPublishedDataItem(new NodeId("ns=0;i=2253"), Attributes.AccessLevelEx + 1)
            );
        }
    }
}
