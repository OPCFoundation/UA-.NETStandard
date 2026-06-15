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
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

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
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);

            //Assert
            Assert
                .Throws<ArgumentException>(() => dataCollector.AddPublishedDataSet(null));
        }

        [Test(Description = "Validate AddPublishedDataSet.")]
        public void ValidateAddPublishedDataSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            string configurationFile = Utils.GetAbsoluteFilePath(
                m_configurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            PubSubConfigurationDataType pubSubConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(configurationFile, telemetry);

            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);
            //Act
            dataCollector.AddPublishedDataSet(pubSubConfiguration.PublishedDataSets[0]);
            DataSet collectedDataSet = dataCollector.CollectData(
                pubSubConfiguration.PublishedDataSets[0].Name);
            //Assert
            Assert.That(
                collectedDataSet,
                Is.Not.Null,
                $"Cannot collect data therefore the '{pubSubConfiguration.PublishedDataSets[0].Name}' publishedDataSet was not registered correctly.");
        }

        [Test(Description = "Validate RemovePublishedDataSet.")]
        public void ValidateRemovePublishedDataSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);
            var publishedDataSet = new PublishedDataSetDataType { Name = "Name" };
            //Act
            dataCollector.AddPublishedDataSet(publishedDataSet);
            dataCollector.RemovePublishedDataSet(publishedDataSet);
            DataSet collectedDataSet = dataCollector.CollectData(publishedDataSet.Name);
            //Assert
            Assert.That(
                collectedDataSet,
                Is.Null,
                $"The '{publishedDataSet.Name}' publishedDataSet was not removed correctly.");
        }

        [Test(Description = "Validate RemovePublishedDataSet with null parameter.")]
        public void ValidateRemovePublishedDataSetWithNullParameter()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);
            //Assert
            Assert
                .Throws<ArgumentException>(() => dataCollector.RemovePublishedDataSet(null));
        }

        [Test(Description = "Validate CollectData from DataStore.")]
        public void ValidateCollectDataFromDataStore()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

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
                new DataValue(new Variant(DateTimeUtc.MaxValue)));

            var dataCollector = new DataCollector(dataStore, telemetry);

            var publishedDataSetSimple = new PublishedDataSetDataType { Name = "Simple" };
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = publishedDataSetSimple.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            var publishedDataItems = new PublishedDataItemsDataType { PublishedData = [] };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataItems.PublishedData = publishedDataItems.PublishedData.AddItem(
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
            Assert.That(
                publishedDataItems,
                Is.Not.Null,
                "The m_firstPublishedDataSet.DataSetSource is not PublishedDataItemsDataType.");
            Assert.That(collectedDataSet, Is.Not.Null, "collectedDataSet is null.");
            Assert.That(collectedDataSet.Fields, Is.Not.Null, "collectedDataSet.Fields is null.");

            Assert.That(
                publishedDataItems.PublishedData.Count,
                Is.EqualTo(collectedDataSet.Fields.Length),
                "collectedDataSet and published data fields count do not match.");

            // validate collected values
            Assert.That(
                collectedDataSet.Fields[0].Value.WrappedValue.GetBoolean(),
                Is.False,
                "collectedDataSet.Fields[0].Value does not match.");
            Assert.That(
                collectedDataSet.Fields[1].Value.WrappedValue.GetInt32(),
                Is.EqualTo(1),
                "collectedDataSet.Fields[1].Value does not match.");
            Assert.That(
                collectedDataSet.Fields[2].Value.WrappedValue.GetInt32(),
                Is.EqualTo(2),
                "collectedDataSet.Fields[2].Value does not match.");
            Assert.That(
                DateTimeUtc.MaxValue,
                Is.EqualTo(collectedDataSet.Fields[3].Value.WrappedValue.GetDateTime()),
                "collectedDataSet.Fields[3].Value does not match.");
        }

        [Test(Description = "Validate CollectData from ExtensionFields.")]
        public void ValidateCollectDataFromExtensionFields()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Arrange
            var dataStore = new UaPubSubDataStore();
            var dataCollector = new DataCollector(dataStore, telemetry);

            var publishedDataSetSimple = new PublishedDataSetDataType { Name = "Simple" };
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = publishedDataSetSimple.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                ]
            };

            //initialize Extension fields collection
            publishedDataSetSimple.ExtensionFields =
            [
                new KeyValuePair { Key = QualifiedName.From("BoolToggle"), Value = true },
                new KeyValuePair { Key = QualifiedName.From("Int32"), Value = 100 },
                new KeyValuePair { Key = QualifiedName.From("Int32Fast"), Value = 50 },
                new KeyValuePair { Key = QualifiedName.From("DateTime"), Value = new DateTimeUtc(DateTime.Today) }
            ];

            var publishedDataItems = new PublishedDataItemsDataType { PublishedData = [] };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataItems.PublishedData = publishedDataItems.PublishedData.AddItem(
                    new PublishedVariableDataType
                    {
                        SubstituteValue = QualifiedName.From(field.Name)
                    });
            }
            publishedDataSetSimple.DataSetSource = new ExtensionObject(publishedDataItems);

            //Act
            dataCollector.AddPublishedDataSet(publishedDataSetSimple);
            DataSet collectedDataSet = dataCollector.CollectData(publishedDataSetSimple.Name);
            //Assert
            Assert.That(
                publishedDataItems,
                Is.Not.Null,
                "The m_firstPublishedDataSet.DataSetSource is not PublishedDataItemsDataType.");
            Assert.That(collectedDataSet, Is.Not.Null, "collectedDataSet is null.");
            Assert.That(collectedDataSet.Fields, Is.Not.Null, "collectedDataSet.Fields is null.");

            Assert.That(
                publishedDataItems.PublishedData.Count,
                Is.EqualTo(collectedDataSet.Fields.Length),
                "collectedDataSet and published data fields count do not match.");
            // validate collected values
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(
                collectedDataSet.Fields[0].Value.Value,
                Is.True,
                "collectedDataSet.Fields[0].Value.Value does not match.");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(
                collectedDataSet.Fields[1].Value.Value,
                Is.EqualTo(100),
                "collectedDataSet.Fields[1].Value.Value does not match.");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(
                collectedDataSet.Fields[2].Value.Value,
                Is.EqualTo(50),
                "collectedDataSet.Fields[2].Value.Value does not match.");
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(
                new DateTimeUtc(DateTime.Today),
                Is.EqualTo(collectedDataSet.Fields[3].Value.Value),
                "collectedDataSet.Fields[3].Value.Value does not match.");
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test(Description = "Validate CollectData unknown dataset name.")]
        public void ValidateCollectDataUnknownDataSetName()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);
            //Act
            DataSet collectedDataSet = dataCollector.CollectData(string.Empty);
            //Assert
            Assert.That(
                collectedDataSet,
                Is.Null,
                "The data collect returns data for unknown DataSetName.");
        }

        [Test(Description = "Validate CollectData null dataset name.")]
        public void ValidateCollectDataNullDataSetName()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            //Arrange
            var dataCollector = new DataCollector(new UaPubSubDataStore(), telemetry);

            //Assert
            Assert.Throws<ArgumentException>(
                () => dataCollector.CollectData(null),
                "The data collect does not throw exception when null parameter.");
        }
    }
}
