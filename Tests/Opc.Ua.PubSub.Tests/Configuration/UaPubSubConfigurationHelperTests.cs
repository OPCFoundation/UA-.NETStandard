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
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    [TestFixture]
    [Category("Configuration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubConfigurationHelperTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "ConfigHelperTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(m_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(m_tempDir))
                {
                    Directory.Delete(m_tempDir, true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void SaveAndLoadEmptyConfiguration()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            string filePath = Path.Combine(m_tempDir, "empty_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            Assert.That(File.Exists(filePath), Is.True);

            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);
            Assert.That(loaded, Is.Not.Null);
        }

        [Test]
        public void SaveAndLoadConfigurationWithConnection()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            var connection = new PubSubConnectionDataType
            {
                Name = "TestConnection",
                Enabled = true,
                PublisherId = new Variant("Publisher1"),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://239.0.0.1:4840" })
            };
            config.Connections = config.Connections.AddItem(connection);

            string filePath = Path.Combine(m_tempDir, "conn_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Connections.Count, Is.EqualTo(1));
            Assert.That(loaded.Connections[0].Name, Is.EqualTo("TestConnection"));
        }

        [Test]
        public void SaveAndLoadConfigurationWithWriterGroup()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            var connection = new PubSubConnectionDataType
            {
                Name = "WGConn",
                Enabled = true,
                PublisherId = new Variant((ushort)100),
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://239.0.0.1:4840" })
            };

            var writerGroup = new WriterGroupDataType
            {
                Name = "WG1",
                WriterGroupId = 1,
                Enabled = true,
                PublishingInterval = 1000,
                KeepAliveTime = 5000
            };
            connection.WriterGroups = connection.WriterGroups.AddItem(writerGroup);
            config.Connections = config.Connections.AddItem(connection);

            string filePath = Path.Combine(m_tempDir, "wg_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            Assert.That(loaded.Connections[0].WriterGroups.Count, Is.EqualTo(1));
            Assert.That(loaded.Connections[0].WriterGroups[0].Name, Is.EqualTo("WG1"));
            Assert.That(loaded.Connections[0].WriterGroups[0].WriterGroupId, Is.EqualTo((ushort)1));
        }

        [Test]
        public void SaveAndLoadConfigurationWithPublishedDataSets()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            var pds = new PublishedDataSetDataType
            {
                Name = "DataSet1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DataSet1",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Temperature",
                            BuiltInType = (byte)BuiltInType.Double,
                            ValueRank = ValueRanks.Scalar
                        }
                    ],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
            config.PublishedDataSets = config.PublishedDataSets.AddItem(pds);

            string filePath = Path.Combine(m_tempDir, "pds_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            Assert.That(loaded.PublishedDataSets.Count, Is.EqualTo(1));
            Assert.That(loaded.PublishedDataSets[0].Name, Is.EqualTo("DataSet1"));
        }

        [Test]
        public void LoadConfigurationFromInvalidPathThrowsException()
        {
            string invalidPath = Path.Combine(m_tempDir, "nonexistent.xml");

            Assert.Throws<ServiceResultException>(() =>
                UaPubSubConfigurationHelper.LoadConfiguration(invalidPath, m_telemetry));
        }

        [Test]
        public void LoadConfigurationFromCorruptFileThrowsException()
        {
            string filePath = Path.Combine(m_tempDir, "corrupt.xml");
            File.WriteAllText(filePath, "this is not valid xml!!!");

            Assert.Throws<ServiceResultException>(() =>
                UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry));
        }

        [Test]
        public void SaveConfigurationOverwritesExistingFile()
        {
            string filePath = Path.Combine(m_tempDir, "overwrite.xml");

            var config1 = new PubSubConfigurationDataType { Enabled = true };
            config1.Connections = config1.Connections.AddItem(new PubSubConnectionDataType { Enabled = true, Name = "First" });
            UaPubSubConfigurationHelper.SaveConfiguration(config1, filePath, m_telemetry);

            var config2 = new PubSubConfigurationDataType { Enabled = true };
            config2.Connections = config2.Connections.AddItem(new PubSubConnectionDataType { Enabled = true, Name = "Second" });
            UaPubSubConfigurationHelper.SaveConfiguration(config2, filePath, m_telemetry);

            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);
            Assert.That(loaded.Connections[0].Name, Is.EqualTo("Second"));
        }

        [Test]
        public void SaveAndLoadConfigurationWithReaderGroup()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            var connection = new PubSubConnectionDataType
            {
                Name = "SubConn",
                Enabled = true,
                PublisherId = new Variant("Sub1"),
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://239.0.0.1:4840" })
            };

            var readerGroup = new ReaderGroupDataType
            {
                Enabled = true,
                Name = "RG1"
            };
            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "Reader1",
                PublisherId = new Variant("Publisher1"),
                WriterGroupId = 1,
                DataSetWriterId = 1,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields = [],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
            readerGroup.DataSetReaders = readerGroup.DataSetReaders.AddItem(reader);
            connection.ReaderGroups = connection.ReaderGroups.AddItem(readerGroup);
            config.Connections = config.Connections.AddItem(connection);

            string filePath = Path.Combine(m_tempDir, "reader_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            Assert.That(loaded.Connections[0].ReaderGroups.Count, Is.EqualTo(1));
            Assert.That(loaded.Connections[0].ReaderGroups[0].DataSetReaders.Count, Is.EqualTo(1));
            Assert.That(loaded.Connections[0].ReaderGroups[0].DataSetReaders[0].Name, Is.EqualTo("Reader1"));
        }

        [Test]
        public void SaveAndLoadConfigurationWithMultipleConnections()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            for (int i = 0; i < 3; i++)
            {
                config.Connections = config.Connections.AddItem(new PubSubConnectionDataType
                {
                    Name = $"Connection{i}",
                    Enabled = i % 2 == 0,
                    PublisherId = new Variant((ushort)i)
                });
            }

            string filePath = Path.Combine(m_tempDir, "multi_conn.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            Assert.That(loaded.Connections.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(loaded.Connections[i].Name, Is.EqualTo($"Connection{i}"));
            }
        }

        [Test]
        public void SaveAndLoadConfigurationPreservesDataSetWriterProperties()
        {
            var config = new PubSubConfigurationDataType { Enabled = true };
            var connection = new PubSubConnectionDataType
            {
                Name = "DswConn",
                Enabled = true,
                PublisherId = new Variant("DswPub"),
                Address = new ExtensionObject(
                    new NetworkAddressUrlDataType { Url = "opc.udp://239.0.0.1:4840" })
            };

            var writerGroup = new WriterGroupDataType
            {
                Name = "DSWWG",
                WriterGroupId = 1,
                Enabled = true
            };
            var writer = new DataSetWriterDataType
            {
                Name = "Writer1",
                DataSetWriterId = 10,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                KeyFrameCount = 5,
                DataSetName = "TestDS"
            };
            writerGroup.DataSetWriters = writerGroup.DataSetWriters.AddItem(writer);
            connection.WriterGroups = connection.WriterGroups.AddItem(writerGroup);
            config.Connections = config.Connections.AddItem(connection);

            string filePath = Path.Combine(m_tempDir, "dsw_config.xml");

            UaPubSubConfigurationHelper.SaveConfiguration(config, filePath, m_telemetry);
            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(filePath, m_telemetry);

            DataSetWriterDataType loadedWriter = loaded.Connections[0].WriterGroups[0].DataSetWriters[0];
            Assert.That(loadedWriter.Name, Is.EqualTo("Writer1"));
            Assert.That(loadedWriter.DataSetWriterId, Is.EqualTo((ushort)10));
            Assert.That(loadedWriter.KeyFrameCount, Is.EqualTo((uint)5));
        }

        [Test]
        public void LoadExistingPublisherConfiguration()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                Path.Combine("Configuration", "PublisherConfiguration.xml"),
                checkCurrentDirectory: true,
                createAlways: false);

            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(configFile, m_telemetry);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Connections.Count, Is.GreaterThan(0));
        }

        [Test]
        public void LoadExistingSubscriberConfiguration()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                Path.Combine("Configuration", "SubscriberConfiguration.xml"),
                checkCurrentDirectory: true,
                createAlways: false);

            PubSubConfigurationDataType loaded = UaPubSubConfigurationHelper.LoadConfiguration(configFile, m_telemetry);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Connections.Count, Is.GreaterThan(0));
        }
    }
}