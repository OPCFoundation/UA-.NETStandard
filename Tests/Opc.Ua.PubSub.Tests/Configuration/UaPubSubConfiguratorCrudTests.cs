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

using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubConfiguratorCrudTests
    {
        private static readonly string s_publisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private UaPubSubConfigurator CreateConfiguratorFromFile()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            PubSubConfigurationDataType config = UaPubSubConfigurationHelper.LoadConfiguration(
                s_publisherConfigurationFileName, telemetry);
            return new UaPubSubConfigurator(telemetry);
        }

        private static UaPubSubConfigurator CreateConfiguratorWithConfig(PubSubConfigurationDataType config)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var configurator = new UaPubSubConfigurator(telemetry);
            configurator.LoadConfiguration(config);
            return configurator;
        }

        [Test]
        public void AddConnectionWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var connection1 = new PubSubConnectionDataType { Enabled = true, Name = "TestConnection" };
            StatusCode result1 = configurator.AddConnection(connection1);
            Assert.That(StatusCode.IsGood(result1), Is.True);

            var connection2 = new PubSubConnectionDataType { Enabled = true, Name = "TestConnection" };
            StatusCode result2 = configurator.AddConnection(connection2);
            Assert.That(result2.Code, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void AddConnectionWithWriterGroupsProcessesSubGroups()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            var connection = new PubSubConnectionDataType
            {
                Enabled = true,
                Name = "TestConnection",
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup })
            };

            StatusCode result = configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void AddConnectionWithReaderGroupsProcessesSubGroups()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var readerGroup = new ReaderGroupDataType { Enabled = true, Name = "RG1" };
            var connection = new PubSubConnectionDataType
            {
                Enabled = true,
                Name = "TestConnection",
                ReaderGroups = new ArrayOf<ReaderGroupDataType>(new[] { readerGroup })
            };

            StatusCode result = configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void AddConnectionWithEmptyNamedGroups()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "" };
            var readerGroup = new ReaderGroupDataType { Enabled = true, Name = "" };
            var connection = new PubSubConnectionDataType
            {
                Enabled = true,
                Name = "TestConnection",
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup }),
                ReaderGroups = new ArrayOf<ReaderGroupDataType>(new[] { readerGroup })
            };

            StatusCode result = configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void RemoveConnectionByIdWithInvalidIdReturnsBadNodeIdUnknown()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            StatusCode result = configurator.RemoveConnection(9999);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void AddAndRemoveConnectionRoundTrip()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            bool connectionAddedFired = false;
            bool connectionRemovedFired = false;
            uint addedConnectionId = 0;

            configurator.ConnectionAdded += (s, e) =>
            {
                connectionAddedFired = true;
                addedConnectionId = e.ConnectionId;
            };
            configurator.ConnectionRemoved += (s, e) => connectionRemovedFired = true;

            var connection = new PubSubConnectionDataType { Enabled = true, Name = "MyConn" };
            StatusCode addResult = configurator.AddConnection(connection);
            Assert.That(StatusCode.IsGood(addResult), Is.True);
            Assert.That(connectionAddedFired, Is.True);

            StatusCode removeResult = configurator.RemoveConnection(addedConnectionId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
            Assert.That(connectionRemovedFired, Is.True);
        }

        [Test]
        public void AddPublishedDataSetAndRemove()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            bool addedFired = false;
            bool removedFired = false;
            uint dataSetId = 0;

            configurator.PublishedDataSetAdded += (s, e) =>
            {
                addedFired = true;
                dataSetId = e.PublishedDataSetId;
            };
            configurator.PublishedDataSetRemoved += (s, e) => removedFired = true;

            var dataSet = new PublishedDataSetDataType { Name = "DS1" };
            StatusCode addResult = configurator.AddPublishedDataSet(dataSet);
            Assert.That(StatusCode.IsGood(addResult), Is.True);
            Assert.That(addedFired, Is.True);

            StatusCode removeResult = configurator.RemovePublishedDataSet(dataSetId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
            Assert.That(removedFired, Is.True);
        }

        [Test]
        public void RemovePublishedDataSetByInvalidIdReturnsGood()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            StatusCode result = configurator.RemovePublishedDataSet(9999);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void RemovePublishedDataSetAlsoRemovesAssociatedWriters()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var dataSet = new PublishedDataSetDataType { Name = "DS1" };
            configurator.AddPublishedDataSet(dataSet);

            var writer = new DataSetWriterDataType { Enabled = true, Name = "W1", DataSetName = "DS1" };
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "WG1",
                DataSetWriters = new ArrayOf<DataSetWriterDataType>(new[] { writer })
            };
            var connection = new PubSubConnectionDataType
            {
                Enabled = true,
                Name = "C1",
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup })
            };
            configurator.AddConnection(connection);

            StatusCode result = configurator.RemovePublishedDataSet(dataSet);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void AddExtensionFieldAndRemove()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint dataSetId = 0;
            configurator.PublishedDataSetAdded += (s, e) => dataSetId = e.PublishedDataSetId;

            var dataSet = new PublishedDataSetDataType { Name = "DS1" };
            configurator.AddPublishedDataSet(dataSet);

            bool extensionAddedFired = false;
            uint extensionFieldId = 0;
            configurator.ExtensionFieldAdded += (s, e) =>
            {
                extensionAddedFired = true;
                extensionFieldId = e.ExtensionFieldId;
            };

            var field = new KeyValuePair
            {
                Key = new QualifiedName("Field1"),
                Value = "Value1"
            };
            StatusCode addResult = configurator.AddExtensionField(dataSetId, field);
            Assert.That(StatusCode.IsGood(addResult), Is.True);
            Assert.That(extensionAddedFired, Is.True);

            bool extensionRemovedFired = false;
            configurator.ExtensionFieldRemoved += (s, e) => extensionRemovedFired = true;

            StatusCode removeResult = configurator.RemoveExtensionField(dataSetId, extensionFieldId);
            Assert.That(StatusCode.IsGood(removeResult), Is.True);
            Assert.That(extensionRemovedFired, Is.True);
        }

        [Test]
        public void AddExtensionFieldWithDuplicateNameReturnsBadNodeIdExists()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint dataSetId = 0;
            configurator.PublishedDataSetAdded += (s, e) => dataSetId = e.PublishedDataSetId;

            var dataSet = new PublishedDataSetDataType { Name = "DS1" };
            configurator.AddPublishedDataSet(dataSet);

            var field1 = new KeyValuePair
            {
                Key = new QualifiedName("DupField"),
                Value = "Value1"
            };
            configurator.AddExtensionField(dataSetId, field1);

            var field2 = new KeyValuePair
            {
                Key = new QualifiedName("DupField"),
                Value = "Value2"
            };
            StatusCode result = configurator.AddExtensionField(dataSetId, field2);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void AddExtensionFieldWithInvalidDataSetIdReturnsBadNodeIdInvalid()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var field = new KeyValuePair
            {
                Key = new QualifiedName("Field1"),
                Value = "Value1"
            };
            StatusCode result = configurator.AddExtensionField(9999, field);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void RemoveExtensionFieldWithInvalidIdsReturnsBadNodeIdInvalid()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            StatusCode result = configurator.RemoveExtensionField(9999, 8888);
            Assert.That(result.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void AddPublishedDataSetWithExtensionFieldsProcessesThem()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var field = new KeyValuePair
            {
                Key = new QualifiedName("EF1"),
                Value = "ExtValue"
            };
            var dataSet = new PublishedDataSetDataType
            {
                Name = "DS1",
                ExtensionFields = new ArrayOf<KeyValuePair>(new[] { field })
            };

            StatusCode result = configurator.AddPublishedDataSet(dataSet);
            Assert.That(StatusCode.IsGood(result), Is.True);
        }

        [Test]
        public void AddPublishedDataSetWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            var ds1 = new PublishedDataSetDataType { Name = "SameName" };
            StatusCode result1 = configurator.AddPublishedDataSet(ds1);
            Assert.That(StatusCode.IsGood(result1), Is.True);

            var ds2 = new PublishedDataSetDataType { Name = "SameName" };
            StatusCode result2 = configurator.AddPublishedDataSet(ds2);
            Assert.That(result2.Code, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void AddWriterGroupWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;

            var connection = new PubSubConnectionDataType { Enabled = true, Name = "C1" };
            configurator.AddConnection(connection);

            var wg1 = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            StatusCode result1 = configurator.AddWriterGroup(connectionId, wg1);
            Assert.That(StatusCode.IsGood(result1), Is.True);

            var wg2 = new WriterGroupDataType { Enabled = true, Name = "WG1" };
            StatusCode result2 = configurator.AddWriterGroup(connectionId, wg2);
            Assert.That(result2.Code, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void AddReaderGroupWithDuplicateNameReturnsBadBrowseNameDuplicated()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;

            var connection = new PubSubConnectionDataType { Enabled = true, Name = "C1" };
            configurator.AddConnection(connection);

            var rg1 = new ReaderGroupDataType { Enabled = true, Name = "RG1" };
            StatusCode result1 = configurator.AddReaderGroup(connectionId, rg1);
            Assert.That(StatusCode.IsGood(result1), Is.True);

            var rg2 = new ReaderGroupDataType { Enabled = true, Name = "RG1" };
            StatusCode result2 = configurator.AddReaderGroup(connectionId, rg2);
            Assert.That(result2.Code, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void AddAndRemoveWriterGroupRoundTrip()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;
            uint writerGroupId = 0;
            configurator.WriterGroupAdded += (s, e) => writerGroupId = e.WriterGroupId;
            bool removedFired = false;
            configurator.WriterGroupRemoved += (s, e) => removedFired = true;

            configurator.AddConnection(new PubSubConnectionDataType { Enabled = true, Name = "C1" });
            configurator.AddWriterGroup(connectionId, new WriterGroupDataType { Enabled = true, Name = "WG1" });

            StatusCode result = configurator.RemoveWriterGroup(writerGroupId);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(removedFired, Is.True);
        }

        [Test]
        public void AddAndRemoveReaderGroupRoundTrip()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;
            uint readerGroupId = 0;
            configurator.ReaderGroupAdded += (s, e) => readerGroupId = e.ReaderGroupId;
            bool removedFired = false;
            configurator.ReaderGroupRemoved += (s, e) => removedFired = true;

            configurator.AddConnection(new PubSubConnectionDataType { Enabled = true, Name = "C1" });
            configurator.AddReaderGroup(connectionId, new ReaderGroupDataType { Enabled = true, Name = "RG1" });

            StatusCode result = configurator.RemoveReaderGroup(readerGroupId);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(removedFired, Is.True);
        }

        [Test]
        public void AddDataSetWriterToWriterGroup()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;
            uint writerGroupId = 0;
            configurator.WriterGroupAdded += (s, e) => writerGroupId = e.WriterGroupId;
            bool writerAddedFired = false;
            configurator.DataSetWriterAdded += (s, e) => writerAddedFired = true;

            configurator.AddConnection(new PubSubConnectionDataType { Enabled = true, Name = "C1" });
            configurator.AddWriterGroup(connectionId, new WriterGroupDataType { Enabled = true, Name = "WG1" });

            var writer = new DataSetWriterDataType { Enabled = true, Name = "W1", DataSetName = "DS1" };
            StatusCode result = configurator.AddDataSetWriter(writerGroupId, writer);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(writerAddedFired, Is.True);
        }

        [Test]
        public void AddDataSetReaderToReaderGroup()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            UaPubSubConfigurator configurator = CreateConfiguratorWithConfig(config);

            uint connectionId = 0;
            configurator.ConnectionAdded += (s, e) => connectionId = e.ConnectionId;
            uint readerGroupId = 0;
            configurator.ReaderGroupAdded += (s, e) => readerGroupId = e.ReaderGroupId;
            bool readerAddedFired = false;
            configurator.DataSetReaderAdded += (s, e) => readerAddedFired = true;

            configurator.AddConnection(new PubSubConnectionDataType { Enabled = true, Name = "C1" });
            configurator.AddReaderGroup(connectionId, new ReaderGroupDataType { Enabled = true, Name = "RG1" });

            var reader = new DataSetReaderDataType { Enabled = true, Name = "R1" };
            StatusCode result = configurator.AddDataSetReader(readerGroupId, reader);
            Assert.That(StatusCode.IsGood(result), Is.True);
            Assert.That(readerAddedFired, Is.True);
        }
    }
}