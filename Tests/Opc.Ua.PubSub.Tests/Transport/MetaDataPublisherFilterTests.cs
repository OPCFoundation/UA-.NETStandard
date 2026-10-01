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

using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Transport
{
    /// <summary>
    /// Received DataSetMetaData is not authenticated, so it may only update
    /// the readers that receive data from the publisher that sent it.
    /// </summary>
    [TestFixture(Description = "Tests the PublisherId filter for received metadata")]
    public class MetaDataPublisherFilterTests
    {
        private const ushort kDataSetWriterId = 1;

        [Test]
        [TestCase("Publisher1", "Publisher1", true)]
        [TestCase("Publisher1", "Attacker", false)]
        [TestCase("Publisher1", null, false)]
        [TestCase(null, "Anyone", true)]
        public void JsonMetaDataUpdatesOnlyReadersOfTheSendingPublisher(
            string readerPublisherId,
            string messagePublisherId,
            bool expectUpdate)
        {
            DataSetMetaDataType metaData = CreateMetaData();
            var networkMessage = new PubSubEncoding.JsonNetworkMessage(null, metaData)
            {
                PublisherId = messagePublisherId,
                DataSetWriterId = kDataSetWriterId
            };

            DataSetReaderDataType reader = ProcessMetaData(
                readerPublisherId == null ? Variant.Null : new Variant(readerPublisherId),
                Profiles.PubSubMqttJsonTransport,
                networkMessage);

            AssertUpdated(reader, metaData, expectUpdate);
        }

        [Test]
        [TestCase((ushort)10, (ushort)10, true)]
        [TestCase((ushort)10, (ushort)11, false)]
        [TestCase((ushort)10, null, false)]
        [TestCase(null, (ushort)11, true)]
        public void UadpMetaDataUpdatesOnlyReadersOfTheSendingPublisher(
            object readerPublisherId,
            object messagePublisherId,
            bool expectUpdate)
        {
            DataSetMetaDataType metaData = CreateMetaData();
            var networkMessage = new PubSubEncoding.UadpNetworkMessage(null, metaData)
            {
                PublisherId = messagePublisherId,
                DataSetWriterId = kDataSetWriterId
            };

            DataSetReaderDataType reader = ProcessMetaData(
                readerPublisherId == null ? Variant.Null : new Variant(readerPublisherId),
                Profiles.PubSubMqttUadpTransport,
                networkMessage);

            AssertUpdated(reader, metaData, expectUpdate);
        }

        private static DataSetMetaDataType CreateMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = "DataSet1",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 1
                }
            };
        }

        private static void AssertUpdated(
            DataSetReaderDataType reader,
            DataSetMetaDataType metaData,
            bool expectUpdate)
        {
            if (expectUpdate)
            {
                Assert.That(reader.DataSetMetaData, Is.SameAs(metaData));
            }
            else
            {
                Assert.That(reader.DataSetMetaData, Is.Not.SameAs(metaData));
            }
        }

        /// <summary>
        /// Passes a received metadata message to a connection with one reader
        /// and returns the reader.
        /// </summary>
        private static DataSetReaderDataType ProcessMetaData(
            Variant readerPublisherId,
            string transportProfileUri,
            UaNetworkMessage networkMessage)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var reader = new DataSetReaderDataType
            {
                Name = "Reader1",
                Enabled = true,
                DataSetWriterId = kDataSetWriterId,
                PublisherId = readerPublisherId
            };
            var configuration = new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections =
                [
                    new PubSubConnectionDataType
                    {
                        Name = "Connection1",
                        Enabled = true,
                        PublisherId = new Variant("Subscriber1"),
                        TransportProfileUri = transportProfileUri,
                        Address = new ExtensionObject(
                            new NetworkAddressUrlDataType { Url = "mqtt://localhost:1883" }),
                        ReaderGroups =
                        [
                            new ReaderGroupDataType
                            {
                                Name = "ReaderGroup1",
                                Enabled = true,
                                DataSetReaders = [reader]
                            }
                        ]
                    }
                ]
            };
            var application = UaPubSubApplication.Create(configuration, telemetry);
            var connection = (UaPubSubConnection)application.PubSubConnections[0];

            MethodInfo process = typeof(UaPubSubConnection).GetMethod(
                "ProcessDecodedNetworkMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            process.Invoke(connection, [networkMessage, "test"]);
            return reader;
        }
    }
}
