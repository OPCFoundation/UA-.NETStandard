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

using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Tests for the fluent surface of <see cref="PubSubConfigurationBuilder"/>
    /// and its nested PublishedDataSet, connection, group, writer and reader builders.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    public sealed class PubSubConfigurationBuilderTests
    {
        [Test]
        public void CreateProducesEnabledEmptyConfiguration()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create().Build();

            Assert.That(configuration.Enabled, Is.True);
            Assert.That(configuration.Connections.Count, Is.Zero);
            Assert.That(configuration.PublishedDataSets.Count, Is.Zero);
        }

        [Test]
        public void EnabledFalseDisablesConfiguration()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .Enabled(false)
                .Build();

            Assert.That(configuration.Enabled, Is.False);
        }

        [Test]
        public void EnabledDefaultReenablesConfiguration()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .Enabled(false)
                .Enabled()
                .Build();

            Assert.That(configuration.Enabled, Is.True);
        }

        [Test]
        public void AddPublishedDataSetWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddPublishedDataSet("DataSet", null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddConnectionWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Connection", null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddPublishedDataSetBuildsMetadataWithGeneratedFieldIds()
        {
            var classId = new Uuid("34d2e4e0-1d8a-4c1b-9f6e-2a2b3c4d5e6f");

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet("Telemetry", ds => ds
                    .WithDataSetClassId(classId)
                    .WithConfigurationVersion(3, 7)
                    .AddField("Temperature", (byte)BuiltInType.Double, DataTypeIds.Double)
                    .AddField("Counter", (byte)BuiltInType.UInt32, DataTypeIds.UInt32, ValueRanks.Scalar))
                .Build();

            Assert.That(configuration.PublishedDataSets.Count, Is.EqualTo(1));
            PublishedDataSetDataType publishedDataSet = configuration.PublishedDataSets[0];
            Assert.That(publishedDataSet.Name, Is.EqualTo("Telemetry"));

            DataSetMetaDataType metaData = publishedDataSet.DataSetMetaData;
            Assert.That(metaData.Name, Is.EqualTo("Telemetry"));
            Assert.That(metaData.DataSetClassId, Is.EqualTo(classId));
            Assert.That(metaData.ConfigurationVersion!.MajorVersion, Is.EqualTo(3u));
            Assert.That(metaData.ConfigurationVersion!.MinorVersion, Is.EqualTo(7u));
            Assert.That(metaData.Fields.Count, Is.EqualTo(2));
            Assert.That(metaData.Fields[0].Name, Is.EqualTo("Temperature"));
            Assert.That(metaData.Fields[0].BuiltInType, Is.EqualTo((byte)BuiltInType.Double));
            Assert.That(metaData.Fields[0].DataType, Is.EqualTo(DataTypeIds.Double));
            Assert.That(metaData.Fields[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(metaData.Fields[1].Name, Is.EqualTo("Counter"));
            Assert.That(metaData.Fields[1].BuiltInType, Is.EqualTo((byte)BuiltInType.UInt32));
            Assert.That(metaData.Fields[0].DataSetFieldId, Is.Not.EqualTo(Uuid.Empty));
            Assert.That(metaData.Fields[1].DataSetFieldId, Is.Not.EqualTo(Uuid.Empty));
        }

        [Test]
        public void AddPublishedDataSetWithoutFieldIdsLeavesFieldIdsEmpty()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet("NoIds", ds => ds
                    .WithoutFieldIds()
                    .AddField("Value", (byte)BuiltInType.Int32, DataTypeIds.Int32))
                .Build();

            DataSetMetaDataType metaData = configuration.PublishedDataSets[0].DataSetMetaData;
            Assert.That(metaData.Fields[0].DataSetFieldId, Is.EqualTo(Uuid.Empty));
        }

        [Test]
        public void AddPublishedDataSetWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddPublishedDataSet(string.Empty, _ => { }),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddFieldWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddPublishedDataSet("DataSet", ds => ds
                    .AddField(string.Empty, (byte)BuiltInType.Int32, DataTypeIds.Int32)),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddConnectionBuildsConnectionWithAddressAndPublisherId()
        {
            const string transportProfile =
                "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp";

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .Enabled(false)
                    .WithPublisherId(new Variant((ushort)7))
                    .WithTransportProfile(transportProfile)
                    .WithAddress("opc.udp://224.0.0.22:4840", "eth0"))
                .Build();

            Assert.That(configuration.Connections.Count, Is.EqualTo(1));
            PubSubConnectionDataType connection = configuration.Connections[0];
            Assert.That(connection.Name, Is.EqualTo("Udp"));
            Assert.That(connection.Enabled, Is.False);
            Assert.That(connection.TransportProfileUri, Is.EqualTo(transportProfile));
            Assert.That(connection.PublisherId.TryGetValue(out ushort publisherId), Is.True);
            Assert.That(publisherId, Is.EqualTo((ushort)7));
            Assert.That(connection.Address.TryGetValue(out NetworkAddressUrlDataType? address), Is.True);
            Assert.That(address, Is.Not.Null);
            Assert.That(address!.Url, Is.EqualTo("opc.udp://224.0.0.22:4840"));
            Assert.That(address.NetworkInterface, Is.EqualTo("eth0"));
            Assert.That(connection.WriterGroups.Count, Is.Zero);
            Assert.That(connection.ReaderGroups.Count, Is.Zero);
        }

        [Test]
        public void AddConnectionWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection(string.Empty, _ => { }),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddWriterGroupWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c.AddWriterGroup("Wg", null!)),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddReaderGroupWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c.AddReaderGroup("Rg", null!)),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddWriterGroupBuildsWriterGroupWithSecurityAndSettings()
        {
            var messageSettings = new UadpWriterGroupMessageDataType { NetworkMessageContentMask = 5 };
            var transportSettings = new DatagramWriterGroupTransportDataType();

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddWriterGroup("Wg1", wg => wg
                        .WithWriterGroupId(9)
                        .Enabled(false)
                        .WithPublishingInterval(200)
                        .WithMaxNetworkMessageSize(2048)
                        .WithSecurity(MessageSecurityMode.SignAndEncrypt, "grp", "opc.tcp://sks:4840")
                        .WithMessageSettings(messageSettings)
                        .WithTransportSettings(transportSettings)))
                .Build();

            Assert.That(configuration.Connections[0].WriterGroups.Count, Is.EqualTo(1));
            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            Assert.That(writerGroup.Name, Is.EqualTo("Wg1"));
            Assert.That(writerGroup.WriterGroupId, Is.EqualTo((ushort)9));
            Assert.That(writerGroup.Enabled, Is.False);
            Assert.That(writerGroup.PublishingInterval, Is.EqualTo(200.0));
            Assert.That(writerGroup.KeepAliveTime, Is.EqualTo(1000.0));
            Assert.That(writerGroup.MaxNetworkMessageSize, Is.EqualTo(2048u));
            Assert.That(writerGroup.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(writerGroup.SecurityGroupId, Is.EqualTo("grp"));
            Assert.That(writerGroup.SecurityKeyServices.Count, Is.EqualTo(1));
            Assert.That(writerGroup.SecurityKeyServices[0].EndpointUrl, Is.EqualTo("opc.tcp://sks:4840"));
            Assert.That(
                writerGroup.MessageSettings.TryGetValue(out UadpWriterGroupMessageDataType? ms),
                Is.True);
            Assert.That(ms!.NetworkMessageContentMask, Is.EqualTo(5u));
            Assert.That(
                writerGroup.TransportSettings.TryGetValue(
                    out DatagramWriterGroupTransportDataType? ts),
                Is.True);
            Assert.That(ts, Is.Not.Null);
            Assert.That(writerGroup.DataSetWriters.Count, Is.Zero);
        }

        [Test]
        public void WriterGroupWithExplicitKeepAliveUsesProvidedValue()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddWriterGroup("Wg", wg => wg.WithPublishingInterval(200, 750)))
                .Build();

            WriterGroupDataType writerGroup = configuration.Connections[0].WriterGroups[0];
            Assert.That(writerGroup.PublishingInterval, Is.EqualTo(200.0));
            Assert.That(writerGroup.KeepAliveTime, Is.EqualTo(750.0));
        }

        [Test]
        public void AddWriterGroupWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c.AddWriterGroup(string.Empty, _ => { })),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddDataSetWriterWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c
                    .AddWriterGroup("Wg", wg => wg.AddDataSetWriter("Writer", null!))),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddDataSetWriterBuildsWriterWithContentMaskAndSettings()
        {
            var messageSettings = new UadpDataSetWriterMessageDataType { DataSetMessageContentMask = 9 };
            var transportSettings = new BrokerDataSetWriterTransportDataType { QueueName = "writer-queue" };

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddWriterGroup("Wg", wg => wg
                        .AddDataSetWriter("Writer1", w => w
                            .WithDataSetWriterId(3)
                            .Enabled(false)
                            .WithDataSetName("Telemetry")
                            .WithKeyFrameCount(5)
                            .WithFieldContentMask(DataSetFieldContentMask.RawData)
                            .WithMessageSettings(messageSettings)
                            .WithTransportSettings(transportSettings))))
                .Build();

            DataSetWriterDataType writer = configuration.Connections[0].WriterGroups[0].DataSetWriters[0];
            Assert.That(writer.Name, Is.EqualTo("Writer1"));
            Assert.That(writer.DataSetWriterId, Is.EqualTo((ushort)3));
            Assert.That(writer.Enabled, Is.False);
            Assert.That(writer.DataSetName, Is.EqualTo("Telemetry"));
            Assert.That(writer.KeyFrameCount, Is.EqualTo(5u));
            Assert.That(writer.DataSetFieldContentMask, Is.EqualTo((uint)DataSetFieldContentMask.RawData));
            Assert.That(
                writer.MessageSettings.TryGetValue(out UadpDataSetWriterMessageDataType? ms),
                Is.True);
            Assert.That(ms!.DataSetMessageContentMask, Is.EqualTo(9u));
            Assert.That(
                writer.TransportSettings.TryGetValue(out BrokerDataSetWriterTransportDataType? ts),
                Is.True);
            Assert.That(ts!.QueueName, Is.EqualTo("writer-queue"));
        }

        [Test]
        public void AddDataSetWriterWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c
                    .AddWriterGroup("Wg", wg => wg.AddDataSetWriter(string.Empty, _ => { }))),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddReaderGroupBuildsReaderGroupWithReaders()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddReaderGroup("Rg1", rg => rg
                        .Enabled(false)
                        .WithMaxNetworkMessageSize(4096)
                        .WithSecurity(MessageSecurityMode.Sign, "readers")
                        .AddDataSetReader("Reader1", r => r.Enabled(false))))
                .Build();

            Assert.That(configuration.Connections[0].ReaderGroups.Count, Is.EqualTo(1));
            ReaderGroupDataType readerGroup = configuration.Connections[0].ReaderGroups[0];
            Assert.That(readerGroup.Name, Is.EqualTo("Rg1"));
            Assert.That(readerGroup.Enabled, Is.False);
            Assert.That(readerGroup.MaxNetworkMessageSize, Is.EqualTo(4096u));
            Assert.That(readerGroup.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(readerGroup.SecurityGroupId, Is.EqualTo("readers"));
            Assert.That(readerGroup.SecurityKeyServices.Count, Is.Zero);
            Assert.That(
                readerGroup.MessageSettings.TryGetValue(out ReaderGroupMessageDataType? ms),
                Is.True);
            Assert.That(ms, Is.Not.Null);
            Assert.That(readerGroup.DataSetReaders.Count, Is.EqualTo(1));
            Assert.That(readerGroup.DataSetReaders[0].Name, Is.EqualTo("Reader1"));
            Assert.That(readerGroup.DataSetReaders[0].Enabled, Is.False);
        }

        [Test]
        public void AddReaderGroupWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c.AddReaderGroup(string.Empty, _ => { })),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void AddDataSetReaderWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c
                    .AddReaderGroup("Rg", rg => rg.AddDataSetReader("Reader", null!))),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void AddDataSetReaderBuildsReaderWithFilterMetadataAndSettings()
        {
            var messageSettings = new UadpDataSetReaderMessageDataType { DataSetMessageContentMask = 4 };
            var transportSettings = new BrokerDataSetReaderTransportDataType { QueueName = "reader-queue" };

            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddReaderGroup("Rg", rg => rg
                        .AddDataSetReader("Reader1", r => r
                            .Enabled(false)
                            .WithFilter(new Variant((ushort)11), 22, 33)
                            .WithFieldContentMask(DataSetFieldContentMask.StatusCode)
                            .WithMessageReceiveTimeout(1234)
                            .WithMessageSettings(messageSettings)
                            .WithTransportSettings(transportSettings)
                            .WithDataSetMetaData("MetaMirror", ds => ds
                                .AddField("Value", (byte)BuiltInType.Int32, DataTypeIds.Int32)))))
                .Build();

            DataSetReaderDataType reader = configuration.Connections[0].ReaderGroups[0].DataSetReaders[0];
            Assert.That(reader.Name, Is.EqualTo("Reader1"));
            Assert.That(reader.Enabled, Is.False);
            Assert.That(reader.PublisherId.TryGetValue(out ushort publisherId), Is.True);
            Assert.That(publisherId, Is.EqualTo((ushort)11));
            Assert.That(reader.WriterGroupId, Is.EqualTo((ushort)22));
            Assert.That(reader.DataSetWriterId, Is.EqualTo((ushort)33));
            Assert.That(
                reader.DataSetFieldContentMask, Is.EqualTo((uint)DataSetFieldContentMask.StatusCode));
            Assert.That(reader.MessageReceiveTimeout, Is.EqualTo(1234.0));
            Assert.That(
                reader.MessageSettings.TryGetValue(out UadpDataSetReaderMessageDataType? ms),
                Is.True);
            Assert.That(ms!.DataSetMessageContentMask, Is.EqualTo(4u));
            Assert.That(
                reader.TransportSettings.TryGetValue(out BrokerDataSetReaderTransportDataType? ts),
                Is.True);
            Assert.That(ts!.QueueName, Is.EqualTo("reader-queue"));
            Assert.That(reader.DataSetMetaData.Name, Is.EqualTo("MetaMirror"));
            Assert.That(reader.DataSetMetaData.Fields.Count, Is.EqualTo(1));
            Assert.That(reader.DataSetMetaData.Fields[0].Name, Is.EqualTo("Value"));
        }

        [Test]
        public void WithMirrorSubscribedDataSetSetsMirrorParentNodeName()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .AddConnection("Udp", c => c
                    .AddReaderGroup("Rg", rg => rg
                        .AddDataSetReader("Reader", r => r
                            .WithMirrorSubscribedDataSet("MirrorRoot"))))
                .Build();

            DataSetReaderDataType reader = configuration.Connections[0].ReaderGroups[0].DataSetReaders[0];
            Assert.That(
                reader.SubscribedDataSet.TryGetValue(out SubscribedDataSetMirrorDataType? mirror),
                Is.True);
            Assert.That(mirror!.ParentNodeName, Is.EqualTo("MirrorRoot"));
        }

        [Test]
        public void AddDataSetReaderWithEmptyNameThrowsArgumentException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c
                    .AddReaderGroup("Rg", rg => rg.AddDataSetReader(string.Empty, _ => { }))),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        [Test]
        public void WithDataSetMetaDataWithNullConfigureThrowsArgumentNullException()
        {
            PubSubConfigurationBuilder builder = PubSubConfigurationBuilder.Create();

            Assert.That(
                () => builder.AddConnection("Udp", c => c
                    .AddReaderGroup("Rg", rg => rg
                        .AddDataSetReader("Reader", r => r.WithDataSetMetaData("Meta", null!)))),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        [Test]
        public void BuildWithTwoConnectionsProducesCompleteObjectGraph()
        {
            PubSubConfigurationDataType configuration = PubSubConfigurationBuilder.Create()
                .Enabled(true)
                .AddPublishedDataSet("Ds1", ds => ds
                    .AddField("A", (byte)BuiltInType.Int32, DataTypeIds.Int32))
                .AddPublishedDataSet("Ds2", ds => ds
                    .AddField("B", (byte)BuiltInType.Double, DataTypeIds.Double)
                    .AddField("C", (byte)BuiltInType.String, DataTypeIds.String))
                .AddConnection("PublisherConn", c => c
                    .WithPublisherId(new Variant((ushort)1))
                    .WithAddress("opc.udp://224.0.0.22:4840")
                    .AddWriterGroup("Wg", wg => wg
                        .WithWriterGroupId(10)
                        .AddDataSetWriter("W1", w => w.WithDataSetName("Ds1"))
                        .AddDataSetWriter("W2", w => w.WithDataSetName("Ds2"))))
                .AddConnection("SubscriberConn", c => c
                    .WithPublisherId(new Variant((ushort)2))
                    .WithAddress("opc.udp://224.0.0.23:4840")
                    .AddReaderGroup("Rg", rg => rg
                        .AddDataSetReader("R1", r => r.WithFilter(new Variant((ushort)1), 10, 0))))
                .Build();

            Assert.That(configuration.Enabled, Is.True);
            Assert.That(configuration.PublishedDataSets.Count, Is.EqualTo(2));
            Assert.That(configuration.PublishedDataSets[0].Name, Is.EqualTo("Ds1"));
            Assert.That(configuration.PublishedDataSets[1].DataSetMetaData.Fields.Count, Is.EqualTo(2));
            Assert.That(configuration.Connections.Count, Is.EqualTo(2));

            PubSubConnectionDataType publisher = configuration.Connections[0];
            Assert.That(publisher.Name, Is.EqualTo("PublisherConn"));
            Assert.That(publisher.WriterGroups.Count, Is.EqualTo(1));
            Assert.That(publisher.WriterGroups[0].WriterGroupId, Is.EqualTo((ushort)10));
            Assert.That(publisher.WriterGroups[0].DataSetWriters.Count, Is.EqualTo(2));
            Assert.That(publisher.WriterGroups[0].DataSetWriters[1].DataSetName, Is.EqualTo("Ds2"));
            Assert.That(publisher.ReaderGroups.Count, Is.Zero);

            PubSubConnectionDataType subscriber = configuration.Connections[1];
            Assert.That(subscriber.Name, Is.EqualTo("SubscriberConn"));
            Assert.That(subscriber.ReaderGroups.Count, Is.EqualTo(1));
            Assert.That(subscriber.ReaderGroups[0].DataSetReaders.Count, Is.EqualTo(1));
            Assert.That(subscriber.WriterGroups.Count, Is.Zero);
            Assert.That(subscriber.ReaderGroups[0].DataSetReaders[0].WriterGroupId, Is.EqualTo((ushort)10));
        }
    }
}
