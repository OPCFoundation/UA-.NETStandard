/* ========================================================================
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
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.PublishedData;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.PubSub.Tests.PublishedData
{
    [TestFixture(Description = "Tests for DataCollector class")]
    public class DataCollectorTests
    {
        private readonly string m_configurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        public const int NamespaceIndex = 2;

        [Test(Description = "Validate AddPublishedDataSet with null parameter.")]
        public void ValidateAddPublishedDataSetWithNullParameter()
        {
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore());

            //Assert
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => dataCollector.AddPublishedDataSet(null));
        }

        [Test(Description = "Validate AddPublishedDataSet.")]
        public void ValidateAddPublishedDataSet()
        {
            //Arrange
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_configurationFileName,
                true,
                true,
                false);
            PubSubConfigurationDataType pubSubConfiguration = UaPubSubConfigurationHelper
                .LoadConfiguration(
                    configurationFile);

            var dataCollector = new DataCollector(new UaPubSubDataStore());
            //Act
            dataCollector.AddPublishedDataSet(pubSubConfiguration.PublishedDataSets[0]);
            DataSet collectedDataSet = dataCollector.CollectData(
                pubSubConfiguration.PublishedDataSets[0].Name);
            //Assert
            Assert.IsNotNull(
                collectedDataSet,
                "Cannot collect data therefore the '{0}' publishedDataSet was not registered correctly.",
                pubSubConfiguration.PublishedDataSets[0].Name);
        }

        [Test(Description = "Validate RemovePublishedDataSet.")]
        public void ValidateRemovePublishedDataSet()
        {
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore());
            var publishedDataSet = new PublishedDataSetDataType { Name = "Name" };
            //Act
            dataCollector.AddPublishedDataSet(publishedDataSet);
            dataCollector.RemovePublishedDataSet(publishedDataSet);
            DataSet collectedDataSet = dataCollector.CollectData(publishedDataSet.Name);
            //Assert
            Assert.IsNull(
                collectedDataSet,
                "The '{0}' publishedDataSet was not removed correctly.",
                publishedDataSet.Name);
        }

        [Test(Description = "Validate RemovePublishedDataSet with null parameter.")]
        public void ValidateRemovePublishedDataSetWithNullParameter()
        {
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore());
            //Assert
            NUnit.Framework.Assert
                .Throws<ArgumentException>(() => dataCollector.RemovePublishedDataSet(null));
        }

        [Test(Description = "Validate CollectData from DataStore.")]
        public void ValidateCollectDataFromDataStore()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("BoolToggle", NamespaceIndex),
                0,
                new DataValue(new Variant(false)));
            dataStore.WritePublishedDataItem(
                new NodeId("Int32", NamespaceIndex),
                0,
                new DataValue(new Variant(1)));
            dataStore.WritePublishedDataItem(
                new NodeId("Int32Fast", NamespaceIndex),
                0,
                new DataValue(new Variant(2)));
            dataStore.WritePublishedDataItem(
                new NodeId("DateTime", NamespaceIndex),
                0,
                new DataValue(new Variant(DateTime.MaxValue)));

            var dataCollector = new DataCollector(dataStore);

            var publishedDataSetSimple = new PublishedDataSetDataType { Name = "Simple" };
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.Empty),
                Name = publishedDataSetSimple.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            var publishedDataItems = new PublishedDataItemsDataType { PublishedData = [] };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataItems.PublishedData.Add(
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId(field.Name, NamespaceIndex),
                        AttributeId = Attributes.Value
                    });
            }
            publishedDataSetSimple.DataSetSource = new ExtensionObject(publishedDataItems);

            //Act
            dataCollector.AddPublishedDataSet(publishedDataSetSimple);
            DataSet collectedDataSet = dataCollector.CollectData(publishedDataSetSimple.Name);

            //Assert
            Assert.IsNotNull(
                publishedDataItems,
                "The m_firstPublishedDataSet.DataSetSource is not PublishedDataItemsDataType.");
            Assert.IsNotNull(collectedDataSet, "collectedDataSet is null.");
            Assert.IsNotNull(collectedDataSet.Fields, "collectedDataSet.Fields is null.");

            Assert.AreEqual(
                collectedDataSet.Fields.Length,
                publishedDataItems.PublishedData.Count,
                "collectedDataSet and published data fields count do not match.");

            // validate collected values
            Assert.AreEqual(
                collectedDataSet.Fields[0].Value.Value,
                false,
                "collectedDataSet.Fields[0].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[1].Value.Value,
                1,
                "collectedDataSet.Fields[1].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[2].Value.Value,
                2,
                "collectedDataSet.Fields[2].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[3].Value.Value,
                DateTime.MaxValue,
                "collectedDataSet.Fields[3].Value.Value does not match.");
        }

        [Test(Description = "Validate CollectData from ExtensionFields.")]
        public void ValidateCollectDataFromExtensionFields()
        {
            //Arrange
            var dataStore = new UaPubSubDataStore();
            var dataCollector = new DataCollector(dataStore);

            var publishedDataSetSimple = new PublishedDataSetDataType { Name = "Simple" };
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.Empty),
                Name = publishedDataSetSimple.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            //initialize Extension fields collection
            publishedDataSetSimple.ExtensionFields =
            [
                new KeyValuePair { Key = new QualifiedName("BoolToggle"), Value = true },
                new KeyValuePair { Key = new QualifiedName("Int32"), Value = 100 },
                new KeyValuePair { Key = new QualifiedName("Int32Fast"), Value = 50 },
                new KeyValuePair { Key = new QualifiedName("DateTime"), Value = DateTime.Today }
            ];

            var publishedDataItems = new PublishedDataItemsDataType { PublishedData = [] };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataItems.PublishedData.Add(
                    new PublishedVariableDataType
                    {
                        SubstituteValue = new QualifiedName(field.Name)
                    });
            }
            publishedDataSetSimple.DataSetSource = new ExtensionObject(publishedDataItems);

            //Act
            dataCollector.AddPublishedDataSet(publishedDataSetSimple);
            DataSet collectedDataSet = dataCollector.CollectData(publishedDataSetSimple.Name);
            //Assert
            Assert.IsNotNull(
                publishedDataItems,
                "The m_firstPublishedDataSet.DataSetSource is not PublishedDataItemsDataType.");
            Assert.IsNotNull(collectedDataSet, "collectedDataSet is null.");
            Assert.IsNotNull(collectedDataSet.Fields, "collectedDataSet.Fields is null.");

            Assert.AreEqual(
                collectedDataSet.Fields.Length,
                publishedDataItems.PublishedData.Count,
                "collectedDataSet and published data fields count do not match.");
            // validate collected values
            Assert.AreEqual(
                collectedDataSet.Fields[0].Value.Value,
                true,
                "collectedDataSet.Fields[0].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[1].Value.Value,
                100,
                "collectedDataSet.Fields[1].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[2].Value.Value,
                50,
                "collectedDataSet.Fields[2].Value.Value does not match.");
            Assert.AreEqual(
                collectedDataSet.Fields[3].Value.Value,
                DateTime.Today,
                "collectedDataSet.Fields[3].Value.Value does not match.");
        }

        [Test(Description = "Validate CollectData unknown dataset name.")]
        public void ValidateCollectDataUnknownDataSetName()
        {
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore());
            //Act
            DataSet collectedDataSet = dataCollector.CollectData(string.Empty);
            //Assert
            Assert.IsNull(
                collectedDataSet,
                "The data collect returns data for unknown DataSetName.");
        }

        [Test(Description = "Validate CollectData null dataset name.")]
        public void ValidateCollectDataNullDataSetName()
        {
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore());

            //Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(
                () => dataCollector.CollectData(null),
                "The data collect does not throw exception when null parameter.");
        }
    }
}
