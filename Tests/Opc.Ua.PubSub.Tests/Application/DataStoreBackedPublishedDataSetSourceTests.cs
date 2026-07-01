/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

// DataStoreBackedPublishedDataSetSource is an internal shim that adapts the
// legacy IUaPubSubDataStore (UA0023) to the new IPublishedDataSetSource
// contract. Suppress the obsolete diagnostic throughout this test file.
#pragma warning disable UA0023
#pragma warning disable CS0618

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Coverage for <see cref="DataStoreBackedPublishedDataSetSource"/>:
    /// constructor guards, metadata build, and field-sampling behaviour
    /// exercised entirely in-memory without touching a real OPC UA server.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class DataStoreBackedPublishedDataSetSourceTests
    {
        [Test]
        public void Constructor_NullDataStore_ThrowsArgumentNullException()
        {
            var config = new PublishedDataSetDataType { Name = "ds" };
            Assert.That(
                () => new DataStoreBackedPublishedDataSetSource(null!, config),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("dataStore"));
        }

        [Test]
        public void Constructor_NullConfiguration_ThrowsArgumentNullException()
        {
            var store = new Mock<IUaPubSubDataStore>().Object;
            Assert.That(
                () => new DataStoreBackedPublishedDataSetSource(store, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void BuildMetaData_WhenConfigHasMetaData_ReturnsSameInstance()
        {
            var meta = new DataSetMetaDataType { Name = "my-meta" };
            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetMetaData = meta
            };
            var source = NewSource(config);

            DataSetMetaDataType result = source.BuildMetaData();

            Assert.That(result, Is.SameAs(meta));
        }

        [Test]
        public void BuildMetaData_WhenConfigMetaDataIsNull_ReturnsNewEmptyInstance()
        {
            var config = new PublishedDataSetDataType
            {
                Name = "ds"
                // DataSetMetaData left as null (default)
            };
            var source = NewSource(config);

            DataSetMetaDataType result = source.BuildMetaData();

            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public async Task SampleAsync_WithCancelledToken_ThrowsOperationCanceledExceptionAsync()
        {
            var config = new PublishedDataSetDataType { Name = "ds" };
            var source = NewSource(config);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await source.SampleAsync(new DataSetMetaDataType(), cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task SampleAsync_WithNullDataSetSource_ReturnsEmptyFieldsAsync()
        {
            var config = new PublishedDataSetDataType { Name = "ds" };
            var source = NewSource(config);

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(new DataSetMetaDataType()).ConfigureAwait(false);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Is.Empty);
        }

        [Test]
        public async Task SampleAsync_WithEmptyExtensionObjectDataSetSource_ReturnsEmptyFieldsAsync()
        {
            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject()
            };
            var source = NewSource(config);

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(null!).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Is.Empty);
        }

        [Test]
        public async Task SampleAsync_WithItemsAndMetaData_MapsFieldNamesFromMetaDataAsync()
        {
            var nodeId = new NodeId(1u);
            DataValue returnValue = new DataValue(new Variant(99.0));
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(true);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        new PublishedVariableDataType
                        {
                            PublishedVariable = nodeId,
                            AttributeId = Attributes.Value
                        }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            var metaData = new DataSetMetaDataType
            {
                Fields = new ArrayOf<FieldMetaData>(
                    new FieldMetaData[] { new FieldMetaData { Name = "Temperature" } })
            };

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(metaData).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Has.Length.EqualTo(1));
            Assert.That(snapshot.Fields[0].Name, Is.EqualTo("Temperature"));
        }

        [Test]
        public async Task SampleAsync_WithItemsBeyondMetaDataCount_UsesEmptyFieldNameAsync()
        {
            DataValue returnValue = default;
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(false);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        new PublishedVariableDataType { PublishedVariable = new NodeId(1u) },
                        new PublishedVariableDataType { PublishedVariable = new NodeId(2u) }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            // MetaData only has one field → second item falls back to empty name
            var metaData = new DataSetMetaDataType
            {
                Fields = new ArrayOf<FieldMetaData>(
                    new FieldMetaData[] { new FieldMetaData { Name = "OnlyOne" } })
            };

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(metaData).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Has.Length.EqualTo(2));
            Assert.That(snapshot.Fields[0].Name, Is.EqualTo("OnlyOne"));
            Assert.That(snapshot.Fields[1].Name, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task SampleAsync_WithDefaultNodeIdPublishedVariable_CallsDataStoreAsync()
        {
            // NodeId is a struct — we use a zero/default NodeId (NodeId.Empty) to
            // verify that TryReadPublishedDataItem is still called for any valid pv.
            DataValue returnValue = default;
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(false);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        // NodeId is a readonly struct; use NodeId.Null (zero NodeId)
                        new PublishedVariableDataType
                        {
                            PublishedVariable = NodeId.Null,
                            AttributeId = Attributes.Value
                        }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(new DataSetMetaDataType()).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Has.Length.EqualTo(1));
            storeMock.Verify(
                m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out It.Ref<DataValue>.IsAny),
                Times.Once);
        }

        [Test]
        public async Task SampleAsync_WithMinValueSourceTimestamp_StoresDefaultSourceTimestampAsync()
        {
            // The default DataValue constructor sets SourceTimestamp = DateTimeUtc.MinValue.
            // The production code maps DateTimeUtc.MinValue → default(DateTimeUtc).
            DataValue returnValue = new DataValue(new Variant(1.0));
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(true);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        new PublishedVariableDataType { PublishedVariable = new NodeId(1u) }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(new DataSetMetaDataType()).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Has.Length.EqualTo(1));
            // DateTimeUtc.MinValue SourceTimestamp is mapped to default(DateTimeUtc)
            Assert.That(snapshot.Fields[0].SourceTimestamp, Is.Default);
        }

        [Test]
        public async Task SampleAsync_WithValidSourceTimestamp_PreservesTimestampAsync()
        {
            DateTime ts = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            DataValue returnValue = new DataValue(
                new Variant(7.0),
                StatusCodes.Good,
                DateTimeUtc.From(ts));
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(true);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        new PublishedVariableDataType { PublishedVariable = new NodeId(1u) }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(null!).ConfigureAwait(false);

            Assert.That(snapshot.Fields[0].SourceTimestamp,
                Is.EqualTo(DateTimeUtc.From(ts)));
        }

        [Test]
        public async Task SampleAsync_WithNullMetaData_UsesEmptyFieldNamesAsync()
        {
            DataValue returnValue = default;
            var storeMock = new Mock<IUaPubSubDataStore>();
            storeMock
                .Setup(m => m.TryReadPublishedDataItem(
                    It.IsAny<NodeId>(),
                    It.IsAny<uint>(),
                    out returnValue))
                .Returns(false);

            var items = new PublishedDataItemsDataType
            {
                PublishedData = new ArrayOf<PublishedVariableDataType>(
                    new PublishedVariableDataType[]
                    {
                        new PublishedVariableDataType { PublishedVariable = new NodeId(5u) }
                    })
            };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetSource = new ExtensionObject(items)
            };
            var source = new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);

            // Empty DataSetMetaDataType (no fields) → field name must fall back to ""
            // Same effect as null since Fields.IsNull → the name-lookup branch is skipped
            PublishedDataSetSnapshot snapshot =
                await source.SampleAsync(new DataSetMetaDataType()).ConfigureAwait(false);

            Assert.That(((DataSetField[]?)snapshot.Fields) ?? [], Has.Length.EqualTo(1));
            Assert.That(snapshot.Fields[0].Name, Is.EqualTo(string.Empty));
        }

        private static DataStoreBackedPublishedDataSetSource NewSource(
            PublishedDataSetDataType config)
        {
            var storeMock = new Mock<IUaPubSubDataStore>();
            return new DataStoreBackedPublishedDataSetSource(storeMock.Object, config);
        }
    }
}
