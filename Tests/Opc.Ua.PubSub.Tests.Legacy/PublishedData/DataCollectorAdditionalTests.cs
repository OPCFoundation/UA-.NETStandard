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
    public class DataCollectorAdditionalTests
    {
        private static DataCollector CreateCollector(IUaPubSubDataStore dataStore = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new DataCollector(dataStore ?? new UaPubSubDataStore(), telemetry);
        }

        /// <summary>
        /// Validate returns true when DataSetMetaData is default-initialized
        /// </summary>
        [Test]
        public void ValidateReturnsTrueWhenDataSetIsDefaultInitialized()
        {
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "Test",
                DataSetSource = ExtensionObject.Null
            };
            bool result = collector.ValidatePublishedDataSet(pds);
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Validate returns false when PublishedData count mismatches Fields count
        /// </summary>
        [Test]
        public void ValidateReturnsFalseWhenCountMismatch()
        {
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "Test",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "Test",
                    Fields = [
                        new FieldMetaData { Name = "F1", BuiltInType = (byte)BuiltInType.Int32 },
                        new FieldMetaData { Name = "F2", BuiltInType = (byte)BuiltInType.Int32 }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [
                        new PublishedVariableDataType()
                    ]
                })
            };
            bool result = collector.ValidatePublishedDataSet(pds);
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Validate throws on null publishedDataSet
        /// </summary>
        [Test]
        public void ValidateThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.That(() => collector.ValidatePublishedDataSet(null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// AddPublishedDataSet throws on null
        /// </summary>
        [Test]
        public void AddPublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.That(() => collector.AddPublishedDataSet(null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// RemovePublishedDataSet throws on null
        /// </summary>
        [Test]
        public void RemovePublishedDataSetThrowsOnNull()
        {
            DataCollector collector = CreateCollector();
            Assert.That(() => collector.RemovePublishedDataSet(null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// GetPublishedDataSet throws on null name
        /// </summary>
        [Test]
        public void GetPublishedDataSetThrowsOnNullName()
        {
            DataCollector collector = CreateCollector();
            Assert.That(() => collector.GetPublishedDataSet(null), Throws.TypeOf<ArgumentException>());
        }

        /// <summary>
        /// GetPublishedDataSet returns null for unknown name
        /// </summary>
        [Test]
        public void GetPublishedDataSetReturnsNullForUnknownName()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType result = collector.GetPublishedDataSet("NonExistent");
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// CollectData returns null for unregistered dataset
        /// </summary>
        [Test]
        public void CollectDataReturnsNullForUnknownDataSet()
        {
            DataCollector collector = CreateCollector();
            DataSet result = collector.CollectData("NonExistent");
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// CollectData returns null when DataSetSource is null (IsNull)
        /// </summary>
        [Test]
        public void CollectDataReturnsNullWhenDataSetSourceIsNull()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateValidPds("Test", ExtensionObject.Null, BuiltInType.Int32);
            collector.AddPublishedDataSet(pds);

            DataSet result = collector.CollectData("Test");
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// AddPublishedDataSet with mismatched counts logs error and does not add
        /// </summary>
        [Test]
        public void AddInvalidPublishedDataSetDoesNotAdd()
        {
            DataCollector collector = CreateCollector();
            var pds = new PublishedDataSetDataType
            {
                Name = "Invalid",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "Invalid",
                    Fields = [
                        new FieldMetaData { Name = "F1", BuiltInType = (byte)BuiltInType.Int32 },
                        new FieldMetaData { Name = "F2", BuiltInType = (byte)BuiltInType.Int32 }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [new PublishedVariableDataType()]
                })
            };
            collector.AddPublishedDataSet(pds);

            PublishedDataSetDataType found = collector.GetPublishedDataSet("Invalid");
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// CollectData with extension field fallback
        /// </summary>
        [Test]
        public void CollectDataUsesExtensionFieldWhenDataValueIsNull()
        {
            var dataStore = new UaPubSubDataStore();
            DataCollector collector = CreateCollector(dataStore);

            var extensionFieldName = new QualifiedName("ExtField1");
            var extensionField = new KeyValuePair
            {
                Key = extensionFieldName,
                Value = new Variant(99)
            };

            var pubVar = new PublishedVariableDataType
            {
                SubstituteValue = new Variant(extensionFieldName)
            };

            var pds = new PublishedDataSetDataType
            {
                Name = "ExtTest",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "ExtTest",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                }),
                ExtensionFields = [extensionField]
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("ExtTest");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields, Has.Length.EqualTo(1));
            Assert.That(result.Fields[0].Value.WrappedValue.AsBoxedObject(), Is.EqualTo(99));
        }

        /// <summary>
        /// CollectData with no matching extension field produces Bad status
        /// </summary>
        [Test]
        public void CollectDataProducesBadWhenNoValueAndNoExtensionField()
        {
            var dataStore = new UaPubSubDataStore();
            DataCollector collector = CreateCollector(dataStore);

            var pubVar = new PublishedVariableDataType();

            var pds = new PublishedDataSetDataType
            {
                Name = "BadTest",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "BadTest",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                })
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("BadTest");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields[0].Value.StatusCode, Is.EqualTo(StatusCodes.Bad));
        }

        /// <summary>
        /// CollectData with SubstituteValue on bad status from store
        /// </summary>
        [Test]
        public void CollectDataUsesSubstituteValueOnBadStatus()
        {
            var dataStore = new UaPubSubDataStore();
            var nodeId = new NodeId(100, 2);
            dataStore.WritePublishedDataItem(nodeId, Attributes.Value, DataValue.FromStatusCode(StatusCodes.Bad));
            DataCollector collector = CreateCollector(dataStore);

            var pubVar = new PublishedVariableDataType
            {
                PublishedVariable = nodeId,
                AttributeId = Attributes.Value,
                SubstituteValue = new Variant(42)
            };

            var pds = new PublishedDataSetDataType
            {
                Name = "SubTest",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "SubTest",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                })
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("SubTest");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields[0].Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42));
            Assert.That(
                result.Fields[0].Value.StatusCode,
                Is.EqualTo(StatusCodes.UncertainSubstituteValue));
        }

        /// <summary>
        /// CollectData with string truncation
        /// </summary>
        [Test]
        public void CollectDataTruncatesStringToMaxStringLength()
        {
            var dataStore = new UaPubSubDataStore();
            var nodeId = new NodeId(200, 2);
            dataStore.WritePublishedDataItem(
                nodeId,
                Attributes.Value,
                new DataValue(new Variant("Hello World Long String")));
            DataCollector collector = CreateCollector(dataStore);

            var pubVar = new PublishedVariableDataType
            {
                PublishedVariable = nodeId,
                AttributeId = Attributes.Value
            };

            var pds = new PublishedDataSetDataType
            {
                Name = "TruncTest",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "TruncTest",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.String,
                            ValueRank = ValueRanks.Scalar,
                            MaxStringLength = 5
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                })
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("TruncTest");

            Assert.That(result, Is.Not.Null);
            string value = result.Fields[0].Value.WrappedValue.ToString();
            Assert.That(value, Has.Length.EqualTo(5));
        }

        /// <summary>
        /// CollectData with ByteString truncation
        /// </summary>
        [Test]
        public void CollectDataTruncatesByteStringToMaxStringLength()
        {
            var dataStore = new UaPubSubDataStore();
            var nodeId = new NodeId(201, 2);
            dataStore.WritePublishedDataItem(
                nodeId,
                Attributes.Value,
                new DataValue(Variant.From(ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }))));
            DataCollector collector = CreateCollector(dataStore);

            var pubVar = new PublishedVariableDataType
            {
                PublishedVariable = nodeId,
                AttributeId = Attributes.Value
            };

            var pds = new PublishedDataSetDataType
            {
                Name = "ByteTrunc",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "ByteTrunc",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.ByteString,
                            ValueRank = ValueRanks.Scalar,
                            MaxStringLength = 3
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                })
            };

            collector.AddPublishedDataSet(pds);
            DataSet result = collector.CollectData("ByteTrunc");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Fields[0].Value.WrappedValue.TryGetValue(out ByteString bs), Is.True);
            Assert.That(bs.Length, Is.EqualTo(3));
        }

        /// <summary>
        /// RemovePublishedDataSet removes correctly
        /// </summary>
        [Test]
        public void RemovePublishedDataSetSucceeds()
        {
            DataCollector collector = CreateCollector();
            PublishedDataSetDataType pds = CreateValidPds("RemoveTest", BuiltInType.Int32);
            collector.AddPublishedDataSet(pds);

            Assert.That(collector.GetPublishedDataSet("RemoveTest"), Is.Not.Null);

            collector.RemovePublishedDataSet(pds);
            Assert.That(collector.GetPublishedDataSet("RemoveTest"), Is.Null);
        }

        private static PublishedDataSetDataType CreateValidPds(
            string name,
            ExtensionObject dataSetSource,
            BuiltInType builtInType)
        {
            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = name,
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)builtInType,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                DataSetSource = dataSetSource
            };
        }

        private static PublishedDataSetDataType CreateValidPds(
            string name,
            BuiltInType builtInType)
        {
            var pubVar = new PublishedVariableDataType();
            return new PublishedDataSetDataType
            {
                Name = name,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = name,
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)builtInType,
                            ValueRank = ValueRanks.Scalar
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [pubVar]
                })
            };
        }
    }
}
