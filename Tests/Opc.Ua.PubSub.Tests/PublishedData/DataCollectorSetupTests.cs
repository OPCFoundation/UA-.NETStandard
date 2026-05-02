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
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.PublishedData
{
    [TestFixture]
    [Category("DataCollector")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataCollectorSetupTests
    {
        private const int NamespaceIndex = 2;

        private static DataCollector CreateCollector(IUaPubSubDataStore dataStore = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new DataCollector(dataStore ?? new UaPubSubDataStore(), telemetry);
        }

        private static PublishedDataSetDataType CreateSimpleDataSet(
            string name,
            params (string fieldName, NodeId dataType)[] fields)
        {
            var pds = new PublishedDataSetDataType { Name = name };
            var meta = new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = name,
                Fields = []
            };
            var publishedData = new PublishedDataItemsDataType { PublishedData = [] };

            foreach ((string fieldName, NodeId dataType) in fields)
            {
                byte builtInType = 0;
                if (dataType == DataTypeIds.String)
                {
                    builtInType = (byte)BuiltInType.String;
                }
                else if (dataType == DataTypeIds.ByteString)
                {
                    builtInType = (byte)BuiltInType.ByteString;
                }
                else if (dataType == DataTypeIds.Int32)
                {
                    builtInType = (byte)BuiltInType.Int32;
                }
                else if (dataType == DataTypeIds.Boolean)
                {
                    builtInType = (byte)BuiltInType.Boolean;
                }

                meta.Fields = meta.Fields.AddItem(new FieldMetaData
                {
                    Name = fieldName,
                    DataSetFieldId = Uuid.NewUuid(),
                    DataType = dataType,
                    BuiltInType = builtInType,
                    ValueRank = ValueRanks.Scalar
                });
                publishedData.PublishedData = publishedData.PublishedData.AddItem(
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId(fieldName, NamespaceIndex),
                        AttributeId = Attributes.Value
                    });
            }

            pds.DataSetMetaData = meta;
            pds.DataSetSource = new ExtensionObject(publishedData);
            return pds;
        }

        [Test]
        public void ConstructorWithValidParameters()
        {
            DataCollector collector = CreateCollector();
            Assert.That(collector, Is.Not.Null);
        }

        [Test]
        public void ValidatePublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.Throws<ArgumentException>(
                () => collector.ValidatePublishedDataSet(null));
        }

        [Test]
        public void ValidatePublishedDataSetReturnsTrueWhenMetaDataIsNull()
        {
            // Validation only fails when metadata is null AND a log is written
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType { Name = "Test" };
            bool result = collector.ValidatePublishedDataSet(pds);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidatePublishedDataSetReturnsTrueForValidDataSet()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateSimpleDataSet("Valid", ("Field1", DataTypeIds.Int32));
            bool result = collector.ValidatePublishedDataSet(pds);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidatePublishedDataSetReturnsFalseWhenFieldCountMismatch()
        {
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "Mismatch",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "Mismatch",
                    Fields =
                    [
                        new FieldMetaData { Name = "F1" },
                        new FieldMetaData { Name = "F2" }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData =
                    [
                        new PublishedVariableDataType
                        {
                            PublishedVariable = new NodeId("F1", NamespaceIndex),
                            AttributeId = Attributes.Value
                        }
                    ]
                })
            };
            bool result = collector.ValidatePublishedDataSet(pds);
            Assert.That(result, Is.False);
        }

        [Test]
        public void AddPublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.Throws<ArgumentException>(() => collector.AddPublishedDataSet(null));
        }

        [Test]
        public void AddPublishedDataSetAddsValid()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("Field1", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);
            PublishedDataSetDataType found = collector.GetPublishedDataSet("DS1");
            Assert.That(found, Is.SameAs(pds));
        }

        [Test]
        public void AddPublishedDataSetSkipsInvalidDataSet()
        {
            // A dataset with mismatched field counts is invalid and should not be registered
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "Invalid",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "Invalid",
                    Fields =
                    [
                        new FieldMetaData { Name = "F1" },
                        new FieldMetaData { Name = "F2" }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData =
                    [
                        new PublishedVariableDataType
                        {
                            PublishedVariable = new NodeId("F1", NamespaceIndex),
                            AttributeId = Attributes.Value
                        }
                    ]
                })
            };
            collector.AddPublishedDataSet(pds);
            Assert.That(collector.GetPublishedDataSet("Invalid"), Is.Null);
        }

        [Test]
        public void RemovePublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.Throws<ArgumentException>(() => collector.RemovePublishedDataSet(null));
        }

        [Test]
        public void RemovePublishedDataSetRemovesExisting()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("Field1", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);
            collector.RemovePublishedDataSet(pds);
            Assert.That(collector.CollectData("DS1"), Is.Null);
        }

        [Test]
        public void GetPublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.Throws<ArgumentException>(() => collector.GetPublishedDataSet(null));
        }

        [Test]
        public void GetPublishedDataSetReturnsNullForUnknown()
        {
            DataCollector collector = CreateCollector();
            Assert.That(collector.GetPublishedDataSet("Unknown"), Is.Null);
        }

        [Test]
        public void CollectDataReturnsNullForUnregisteredDataSet()
        {
            DataCollector collector = CreateCollector();
            Assert.That(collector.CollectData("Unknown"), Is.Null);
        }

        [Test]
        public void CollectDataThrowsOnNullName()
        {
            DataCollector collector = CreateCollector();
            Assert.Throws<ArgumentException>(() => collector.CollectData(null));
        }

        [Test]
        public void CollectDataReturnsFieldsFromDataStore()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("IntField", NamespaceIndex), Attributes.Value,
                new DataValue(new Variant(42)));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("IntField", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("DS1");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields, Has.Length.EqualTo(1));
            Assert.That(result.Fields[0].Value.WrappedValue.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void CollectDataReturnsBadValueWhenNodeMissingAndNoExtensionField()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("MissingNode", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("DS1");
            Assert.That(result, Is.Not.Null);
            Assert.That(StatusCode.IsBad(result.Fields[0].Value.StatusCode), Is.True);
        }

        [Test]
        public void CollectDataUsesSubstituteValueWhenDataValueIsBad()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("BadField", NamespaceIndex), Attributes.Value,
                DataValue.FromStatusCode(StatusCodes.Bad));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("BadField", DataTypeIds.Int32));

            // Set a substitute value on the published variable
            var publishedItems = ExtensionObject.ToEncodeable(pds.DataSetSource) as PublishedDataItemsDataType;
            publishedItems.PublishedData[0].SubstituteValue = Variant.From(999);

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("DS1");
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.Fields[0].Value.StatusCode,
                Is.EqualTo(StatusCodes.UncertainSubstituteValue));
            Assert.That(result.Fields[0].Value.WrappedValue.GetInt32(), Is.EqualTo(999));
        }

        [Test]
        public void CollectDataTruncatesStringByMaxStringLength()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("StrField", NamespaceIndex), Attributes.Value,
                new DataValue(new Variant("HelloWorldLongString")));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("StrField", DataTypeIds.String));

            // Set MaxStringLength on the field metadata
            pds.DataSetMetaData.Fields[0].MaxStringLength = 5;

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("DS1");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields[0].Value.WrappedValue.GetString(), Is.EqualTo("Hello"));
        }

        [Test]
        public void CollectDataDoesNotTruncateStringWhenMaxStringLengthIsZero()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("StrField", NamespaceIndex), Attributes.Value,
                new DataValue(new Variant("Hello")));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("StrField", DataTypeIds.String));
            pds.DataSetMetaData.Fields[0].MaxStringLength = 0;

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("DS1");
            Assert.That(result.Fields[0].Value.WrappedValue.GetString(), Is.EqualTo("Hello"));
        }

        [Test]
        public void CollectDataTruncatesByteStringByMaxStringLength()
        {
            var dataStore = new UaPubSubDataStore();
            var bytes = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            dataStore.WritePublishedDataItem(
                new NodeId("ByteField", NamespaceIndex), Attributes.Value,
                new DataValue(Variant.From(bytes)));

            DataCollector collector = CreateCollector(dataStore);
            var meta = new DataSetMetaDataType
            {
                Name = "DS1",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "ByteField",
                        DataSetFieldId = Uuid.NewUuid(),
                        DataType = DataTypeIds.ByteString,
                        BuiltInType = (byte)BuiltInType.ByteString,
                        ValueRank = ValueRanks.Scalar,
                        MaxStringLength = 3
                    }
                ]
            };
            var items = new PublishedDataItemsDataType
            {
                PublishedData =
                [
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId("ByteField", NamespaceIndex),
                        AttributeId = Attributes.Value
                    }
                ]
            };
            var pds = new PublishedDataSetDataType
            {
                Name = "DS1",
                DataSetMetaData = meta,
                DataSetSource = new ExtensionObject(items)
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("DS1");
            Assert.That(result, Is.Not.Null);
            Assert.That(
                result.Fields[0].Value.WrappedValue.GetByteString().Length,
                Is.EqualTo(3));
        }

        [Test]
        public void CollectDataSetsServerTimestampOnFields()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("Field1", NamespaceIndex), Attributes.Value,
                new DataValue(new Variant(1)));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("Field1", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("DS1");
            Assert.That(result.Fields[0].Value.ServerTimestamp, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void CollectDataSetsDataSetMetaDataOnResult()
        {
            var dataStore = new UaPubSubDataStore();
            dataStore.WritePublishedDataItem(
                new NodeId("F1", NamespaceIndex), Attributes.Value,
                new DataValue(new Variant(1)));

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("F1", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("DS1");
            Assert.That(result.DataSetMetaData, Is.SameAs(pds.DataSetMetaData));
        }

        [Test]
        public void CollectDataFromExtensionFieldsWhenVariableIsNull()
        {
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "ExtTest",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "ExtTest",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "EF1",
                            DataSetFieldId = Uuid.NewUuid(),
                            DataType = DataTypeIds.Int32,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                ExtensionFields =
                [
                    new KeyValuePair
                    {
                        Key = QualifiedName.From("EF1"),
                        Value = 55
                    }
                ],
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData =
                    [
                        new PublishedVariableDataType
                        {
                            SubstituteValue = Variant.From(QualifiedName.From("EF1"))
                        }
                    ]
                })
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("ExtTest");
            Assert.That(result, Is.Not.Null);
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(result.Fields[0].Value.Value, Is.EqualTo(55));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void CollectDataClonesDataValueFromStore()
        {
            // Verifies the collected data value is a clone, not the original
            var dataStore = new UaPubSubDataStore();
            var original = new DataValue(new Variant(42));
            dataStore.WritePublishedDataItem(
                new NodeId("F1", NamespaceIndex), Attributes.Value, original);

            DataCollector collector = CreateCollector(dataStore);
            PublishedDataSetDataType pds = CreateSimpleDataSet("DS1", ("F1", DataTypeIds.Int32));
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("DS1");
            Assert.That(result.Fields[0].Value, Is.Not.SameAs(original));
            Assert.That(result.Fields[0].Value.WrappedValue.GetInt32(), Is.EqualTo(42));
        }
    }
}
