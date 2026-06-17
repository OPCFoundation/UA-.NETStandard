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

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Coverage for <see cref="PublishedDataSet"/>: constructor guards,
    /// metadata source precedence, DataSetClassId, snapshot delegation, and
    /// the RefreshMetaData change-notification path.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class PublishedDataSetTests
    {
        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------

        [Test]
        public void Constructor_NullConfiguration_ThrowsArgumentNullException()
        {
            var sourceMock = new Mock<IPublishedDataSetSource>();
            sourceMock.Setup(s => s.BuildMetaData()).Returns(new DataSetMetaDataType());

            Assert.That(
                () => new PublishedDataSet(null!, sourceMock.Object),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configuration"));
        }

        [Test]
        public void Constructor_NullSource_ThrowsArgumentNullException()
        {
            var config = new PublishedDataSetDataType { Name = "ds" };
            Assert.That(
                () => new PublishedDataSet(config, null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("source"));
        }

        // ------------------------------------------------------------------
        // Name
        // ------------------------------------------------------------------

        [Test]
        public void Constructor_WithConfigName_SetsNameProperty()
        {
            var config = new PublishedDataSetDataType { Name = "my-dataset" };
            var sourceMock = SourceReturning(new DataSetMetaDataType());

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.Name, Is.EqualTo("my-dataset"));
        }

        [Test]
        public void Constructor_WithNullConfigName_NameIsEmptyString()
        {
            var config = new PublishedDataSetDataType { Name = null };
            var sourceMock = SourceReturning(new DataSetMetaDataType());

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.Name, Is.EqualTo(string.Empty));
        }

        // ------------------------------------------------------------------
        // MetaData source precedence
        // ------------------------------------------------------------------

        [Test]
        public void Constructor_SourceMetaDataTakesPrecedenceOverConfigMetaData()
        {
            var sourceMetaData = new DataSetMetaDataType { Name = "from-source" };
            var configMetaData = new DataSetMetaDataType { Name = "from-config" };

            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetMetaData = configMetaData
            };
            var sourceMock = SourceReturning(sourceMetaData);

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.MetaData, Is.SameAs(sourceMetaData));
        }

        [Test]
        public void Constructor_WhenSourceReturnsNull_FallsBackToConfigMetaData()
        {
            var configMetaData = new DataSetMetaDataType { Name = "from-config" };
            var config = new PublishedDataSetDataType
            {
                Name = "ds",
                DataSetMetaData = configMetaData
            };
            var sourceMock = SourceReturningNull();

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.MetaData, Is.SameAs(configMetaData));
        }

        [Test]
        public void Constructor_WhenBothSourceAndConfigMetaDataAreNull_UsesNewEmptyMetaData()
        {
            var config = new PublishedDataSetDataType { Name = "ds" };
            // DataSetMetaData defaults to null; SourceReturning(null) also returns null
            var sourceMock = SourceReturningNull();

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.MetaData, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // DataSetClassId
        // ------------------------------------------------------------------

        [Test]
        public void Constructor_MetaDataHasNonEmptyDataSetClassId_PropertyReflectsIt()
        {
            var guid = Guid.NewGuid();
            var meta = new DataSetMetaDataType { DataSetClassId = new Uuid(guid) };
            var config = new PublishedDataSetDataType { Name = "ds" };
            var sourceMock = SourceReturning(meta);

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.DataSetClassId, Is.EqualTo(new Uuid(guid)));
        }

        [Test]
        public void Constructor_MetaDataHasEmptyDataSetClassId_PropertyIsEmpty()
        {
            var meta = new DataSetMetaDataType { DataSetClassId = Uuid.Empty };
            var config = new PublishedDataSetDataType { Name = "ds" };
            var sourceMock = SourceReturning(meta);

            var ds = new PublishedDataSet(config, sourceMock);

            Assert.That(ds.DataSetClassId, Is.EqualTo(Uuid.Empty));
        }

        // ------------------------------------------------------------------
        // SampleAsync
        // ------------------------------------------------------------------

        [Test]
        public async Task SampleAsync_DelegatesToSourceWithCurrentMetaDataAsync()
        {
            var meta = new DataSetMetaDataType
            {
                Name = "m",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 3 }
            };
            var snapshot = new PublishedDataSetSnapshot(
                new ConfigurationVersionDataType { MajorVersion = 3 },
                [],
                DateTimeUtc.From(DateTimeOffset.UtcNow));

            var sourceMock = new Mock<IPublishedDataSetSource>();
            sourceMock.Setup(s => s.BuildMetaData()).Returns(meta);
            sourceMock
                .Setup(s => s.SampleAsync(meta, It.IsAny<CancellationToken>()))
                .ReturnsAsync(snapshot);

            var config = new PublishedDataSetDataType { Name = "ds" };
            var ds = new PublishedDataSet(config, sourceMock.Object);

            PublishedDataSetSnapshot result =
                await ds.SampleAsync().ConfigureAwait(false);

            Assert.That(result, Is.SameAs(snapshot));
            sourceMock.Verify(
                s => s.SampleAsync(meta, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // ------------------------------------------------------------------
        // RefreshMetaData
        // ------------------------------------------------------------------

        [Test]
        public void RefreshMetaData_WhenSourceReturnsNull_IsNoOpAndDoesNotFireEvent()
        {
            var meta = new DataSetMetaDataType { Name = "v1" };
            var sourceMock = new Mock<IPublishedDataSetSource>();
            // First call at construction returns meta; subsequent calls return null
            sourceMock.SetupSequence(s => s.BuildMetaData())
                .Returns(meta)
                .Returns((DataSetMetaDataType)null!);

            var config = new PublishedDataSetDataType { Name = "ds" };
            var ds = new PublishedDataSet(config, sourceMock.Object);

            bool fired = false;
            ds.MetaDataChanged += (_, _) => fired = true;

            ds.RefreshMetaData();

            Assert.That(fired, Is.False);
            Assert.That(ds.MetaData, Is.SameAs(meta), "MetaData must remain unchanged.");
        }

        [Test]
        public void RefreshMetaData_WhenSourceReturnsSameReference_DoesNotFireEvent()
        {
            var meta = new DataSetMetaDataType { Name = "same" };
            var sourceMock = new Mock<IPublishedDataSetSource>();
            // Always returns the exact same instance
            sourceMock.Setup(s => s.BuildMetaData()).Returns(meta);

            var config = new PublishedDataSetDataType { Name = "ds" };
            var ds = new PublishedDataSet(config, sourceMock.Object);

            bool fired = false;
            ds.MetaDataChanged += (_, _) => fired = true;

            ds.RefreshMetaData();

            Assert.That(fired, Is.False);
        }

        [Test]
        public void RefreshMetaData_WhenSourceReturnsDifferentObject_FiresMetaDataChangedEvent()
        {
            var meta1 = new DataSetMetaDataType
            {
                Name = "v1",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 1 }
            };
            var meta2 = new DataSetMetaDataType
            {
                Name = "v2",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 2 }
            };

            var sourceMock = new Mock<IPublishedDataSetSource>();
            sourceMock.SetupSequence(s => s.BuildMetaData())
                .Returns(meta1)  // called at construction
                .Returns(meta2); // called at RefreshMetaData

            var config = new PublishedDataSetDataType { Name = "ds" };
            var ds = new PublishedDataSet(config, sourceMock.Object);

            DataSetMetaDataChangedEventArgs? captured = null;
            ds.MetaDataChanged += (_, e) => captured = e;

            ds.RefreshMetaData();

            Assert.That(captured, Is.Not.Null, "MetaDataChanged must fire when rebuilt object differs.");
            Assert.That(captured!.Previous, Is.SameAs(meta1));
            Assert.That(captured.Current, Is.SameAs(meta2));
            Assert.That(ds.MetaData, Is.SameAs(meta2), "MetaData property must be updated.");
        }

        [Test]
        public void RefreshMetaData_WhenSourceReturnsDifferentObject_UpdatesMetaDataProperty()
        {
            var meta1 = new DataSetMetaDataType { Name = "v1" };
            var meta2 = new DataSetMetaDataType
            {
                Name = "v2",
                ConfigurationVersion = new ConfigurationVersionDataType { MajorVersion = 5 }
            };

            var sourceMock = new Mock<IPublishedDataSetSource>();
            sourceMock.SetupSequence(s => s.BuildMetaData())
                .Returns(meta1)
                .Returns(meta2);

            var config = new PublishedDataSetDataType { Name = "ds" };
            var ds = new PublishedDataSet(config, sourceMock.Object);

            ds.RefreshMetaData();

            Assert.That(ds.MetaData, Is.SameAs(meta2));
        }

        // ------------------------------------------------------------------
        // Configuration property
        // ------------------------------------------------------------------

        [Test]
        public void Configuration_ReturnsTheSuppliedConfiguration()
        {
            var config = new PublishedDataSetDataType { Name = "check-config" };
            var ds = new PublishedDataSet(config, SourceReturning(new DataSetMetaDataType()));

            Assert.That(ds.Configuration, Is.SameAs(config));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static IPublishedDataSetSource SourceReturning(DataSetMetaDataType meta)
        {
            var mock = new Mock<IPublishedDataSetSource>();
            mock.Setup(s => s.BuildMetaData()).Returns(meta);
            mock.Setup(s => s.SampleAsync(
                    It.IsAny<DataSetMetaDataType>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishedDataSetSnapshot(
                    new ConfigurationVersionDataType(),
                    [],
                    DateTimeUtc.From(DateTimeOffset.UtcNow)));
            return mock.Object;
        }

        private static IPublishedDataSetSource SourceReturningNull()
        {
            var mock = new Mock<IPublishedDataSetSource>();
            // Intentionally return null to test null-source fallback paths.
#pragma warning disable CS8603
            mock.Setup(s => s.BuildMetaData()).Returns((DataSetMetaDataType?)null!);
#pragma warning restore CS8603
            mock.Setup(s => s.SampleAsync(
                    It.IsAny<DataSetMetaDataType>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublishedDataSetSnapshot(
                    new ConfigurationVersionDataType(),
                    [],
                    DateTimeUtc.From(DateTimeOffset.UtcNow)));
            return mock.Object;
        }
    }
}
