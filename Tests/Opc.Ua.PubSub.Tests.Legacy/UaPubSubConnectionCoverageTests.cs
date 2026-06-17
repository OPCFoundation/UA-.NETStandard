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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class UaPubSubConnectionCoverageTests
    {
        [Test]
        public void ConstructorWithoutNameDefaultsName()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using UaPubSubApplication app = UaPubSubApplication.Create(telemetry);
            using var connection = new UdpPubSubConnection(
                app,
                new PubSubConnectionDataType
                {
                    Name = string.Empty,
                    TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                    Address = new ExtensionObject(new NetworkAddressUrlDataType
                    {
                        Url = "opc.udp://127.0.0.1:4840"
                    })
                },
                telemetry);

            Assert.That(connection.PubSubConnectionConfiguration.Name, Is.EqualTo("<connection>"));
        }

        [Test]
        public void WriterGroupAddedEventAddsPublisher()
        {
            using UaPubSubApplication app = CreateApplication("Publishers");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            int before = connection.Publishers.Count;
            uint connectionId = app.UaPubSubConfigurator.FindIdForObject(connection.PubSubConnectionConfiguration);
            StatusCode status = app.UaPubSubConfigurator.AddWriterGroup(
                connectionId,
                new WriterGroupDataType
                {
                    Name = "AddedWriterGroup",
                    WriterGroupId = 7,
                    Enabled = true,
                    DataSetWriters =
                    [
                        new DataSetWriterDataType
                        {
                            Name = "AddedWriter",
                            DataSetWriterId = 71,
                            DataSetName = "DataSet1",
                            Enabled = true
                        }
                    ]
                });

            Assert.That(status, Is.EqualTo(StatusCodes.Good));
            Assert.That(connection.Publishers, Has.Count.EqualTo(before + 1));
        }

        [Test]
        public void CanPublishReturnsTrueWhenRunningAndWriterGroupOperational()
        {
            using UaPubSubApplication app = CreateApplication("CanPublish");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            WriterGroupDataType writerGroup = connection.PubSubConnectionConfiguration.WriterGroups[0];

            app.UaPubSubConfigurator.Enable(app.UaPubSubConfigurator.PubSubConfiguration);
            app.UaPubSubConfigurator.Enable(connection.PubSubConnectionConfiguration);
            app.UaPubSubConfigurator.Enable(writerGroup);
            SetIsRunning(connection, true);

            Assert.That(connection.CanPublish(writerGroup), Is.True);
        }

        [Test]
        public void ProcessDecodedNetworkMessageMetaDataUpdatesReaderAndRaisesEvents()
        {
            using UaPubSubApplication app = CreateApplication("MetaData");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            DataSetReaderDataType reader = connection.PubSubConnectionConfiguration.ReaderGroups[0].DataSetReaders[0];
            int updatingEvents = 0;
            int metaDataEvents = 0;
            app.ConfigurationUpdating += (_, e) => updatingEvents++;
            app.MetaDataReceived += (_, e) => metaDataEvents++;

            var updatedMetaData = new DataSetMetaDataType
            {
                Name = "Updated",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 2,
                    MinorVersion = 0
                }
            };
            var message = new Opc.Ua.PubSub.Encoding.UadpNetworkMessage(
                connection.PubSubConnectionConfiguration.WriterGroups[0],
                updatedMetaData,
                NullLogger.Instance)
            {
                DataSetWriterId = reader.DataSetWriterId,
                PublisherId = Variant.From((ushort)1)
            };

            InvokeProtected(connection, "ProcessDecodedNetworkMessage", message, "source-a");

            Assert.That(updatingEvents, Is.EqualTo(1));
            Assert.That(metaDataEvents, Is.EqualTo(1));
            Assert.That(reader.DataSetMetaData?.Name, Is.EqualTo("Updated"));
        }

        [Test]
        public void ProcessDecodedNetworkMessageRespectsCancelledConfigurationUpdate()
        {
            using UaPubSubApplication app = CreateApplication("MetaDataCancel");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            DataSetReaderDataType reader = connection.PubSubConnectionConfiguration.ReaderGroups[0].DataSetReaders[0];
            DataSetMetaDataType original = reader.DataSetMetaData;
            app.ConfigurationUpdating += (_, e) => e.Cancel = true;

            var message = new Opc.Ua.PubSub.Encoding.UadpNetworkMessage(
                connection.PubSubConnectionConfiguration.WriterGroups[0],
                new DataSetMetaDataType
                {
                    Name = "Blocked",
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 3,
                        MinorVersion = 0
                    }
                },
                NullLogger.Instance)
            {
                DataSetWriterId = reader.DataSetWriterId,
                PublisherId = Variant.From((ushort)1)
            };

            InvokeProtected(connection, "ProcessDecodedNetworkMessage", message, "source-b");

            Assert.That(reader.DataSetMetaData, Is.SameAs(original));
        }

        [Test]
        public void ProcessDecodedNetworkMessageDataMessageRaisesDataReceived()
        {
            using UaPubSubApplication app = CreateApplication("Data");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            int received = 0;
            app.DataReceived += (_, e) => received++;
            var message = new TestDataNetworkMessage(connection.PubSubConnectionConfiguration.WriterGroups[0])
            {
                PublisherId = Variant.From((ushort)2)
            };

            InvokeProtected(connection, "ProcessDecodedNetworkMessage", message, "source-c");

            Assert.That(received, Is.EqualTo(1));
        }

        [Test]
        public void ProcessDecodedNetworkMessageDiscoveryResponsesRaiseSpecificEvents()
        {
            using UaPubSubApplication app = CreateApplication("Discovery");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];
            int writerConfigEvents = 0;
            int publisherEndpointEvents = 0;
            app.DataSetWriterConfigurationReceived += (_, e) => writerConfigEvents++;
            app.PublisherEndpointsReceived += (_, e) => publisherEndpointEvents++;

            ushort[] ids = [1];
            var writerConfigMessage = new Opc.Ua.PubSub.Encoding.UadpNetworkMessage(
                ids,
                connection.PubSubConnectionConfiguration.WriterGroups[0],
                [StatusCodes.Good],
                NullLogger.Instance)
            {
                PublisherId = Variant.From((ushort)3)
            };
            var endpointsMessage = new Opc.Ua.PubSub.Encoding.UadpNetworkMessage(
                [new EndpointDescription()],
                StatusCodes.Good,
                NullLogger.Instance)
            {
                PublisherId = Variant.From((ushort)4)
            };

            InvokeProtected(connection, "ProcessDecodedNetworkMessage", writerConfigMessage, "source-d");
            InvokeProtected(connection, "ProcessDecodedNetworkMessage", endpointsMessage, "source-e");

            Assert.That(writerConfigEvents, Is.EqualTo(1));
            Assert.That(publisherEndpointEvents, Is.EqualTo(1));
        }

        [Test]
        public void ProtectedDiscoveryHelpersReturnExpectedValues()
        {
            using UaPubSubApplication app = CreateApplication("Helpers");
            var connection = (UaPubSubConnection)app.PubSubConnections[0];

            List<DataSetReaderDataType> readers = InvokeProtected<List<DataSetReaderDataType>>(
                connection,
                "GetAllDataSetReaders");
            List<DataSetWriterDataType> writers = InvokeProtected<List<DataSetWriterDataType>>(
                connection,
                "GetWriterGroupsDataType");
            IList<DataSetWriterConfigurationResponse> responses =
                InvokeProtected<IList<DataSetWriterConfigurationResponse>>(
                    connection,
                    "GetDataSetWriterDiscoveryResponses",
                    new ushort[] { 1, 999 });
            double keepAlive = InvokeProtected<double>(
                connection,
                "GetWriterGroupsMaxKeepAlive");

            Assert.That(readers, Has.Count.EqualTo(1));
            Assert.That(writers, Has.Count.EqualTo(1));
            Assert.That(responses, Has.Count.EqualTo(2));
            Assert.That(responses[0].StatusCodes[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(responses[1].StatusCodes[0], Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(keepAlive, Is.EqualTo(250d));
        }

        private static UaPubSubApplication CreateApplication(string connectionName)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var readerMetaData = new DataSetMetaDataType
            {
                Name = "Original",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
            var writerGroup = new WriterGroupDataType
            {
                Name = "WriterGroup1",
                WriterGroupId = 11,
                KeepAliveTime = 250,
                Enabled = true,
                DataSetWriters =
                [
                    new DataSetWriterDataType
                    {
                        Name = "Writer1",
                        DataSetWriterId = 1,
                        DataSetName = "DataSet1",
                        Enabled = true
                    }
                ]
            };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "ReaderGroup1",
                Enabled = true,
                DataSetReaders =
                [
                    new DataSetReaderDataType
                    {
                        Name = "Reader1",
                        DataSetWriterId = 1,
                        Enabled = true,
                        DataSetMetaData = readerMetaData
                    }
                ]
            };
            var connection = new PubSubConnectionDataType
            {
                Name = connectionName,
                Enabled = true,
                PublisherId = Variant.From((ushort)1),
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                }),
                WriterGroups = [writerGroup],
                ReaderGroups = [readerGroup]
            };

            return UaPubSubApplication.Create(
                new PubSubConfigurationDataType
                {
                    Enabled = true,
                    Connections = [connection]
                },
                telemetry);
        }

        private static void SetIsRunning(UaPubSubConnection connection, bool value)
        {
            typeof(UaPubSubConnection)
                .GetField("<IsRunning>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(connection, value);
        }

        private static void InvokeProtected(UaPubSubConnection connection, string methodName, params object[] args)
        {
            typeof(UaPubSubConnection)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, args);
        }

        private static T InvokeProtected<T>(UaPubSubConnection connection, string methodName, params object[] args)
        {
            object result = typeof(UaPubSubConnection)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(connection, args);
            return (T)result!;
        }

        private sealed class TestDataNetworkMessage : UaNetworkMessage
        {
            public TestDataNetworkMessage(WriterGroupDataType writerGroup)
                : base(writerGroup, [new TestDataSetMessage()], NullLogger.Instance)
            {
            }

            public Variant PublisherId { get; set; }

            public override byte[] Encode(IServiceMessageContext messageContext)
            {
                return [];
            }

            public override void Encode(IServiceMessageContext messageContext, Stream stream)
            {
            }

            public override void Decode(
                IServiceMessageContext messageContext,
                byte[] message,
                IList<DataSetReaderDataType> dataSetReaders)
            {
            }
        }

        private sealed class TestDataSetMessage : UaDataSetMessage
        {
            public TestDataSetMessage()
                : base(NullLogger.Instance)
            {
                DataSetWriterId = 1;
                DataSet = new DataSet();
            }

            public override void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
            {
                FieldContentMask = fieldContentMask;
            }
        }
    }
}
