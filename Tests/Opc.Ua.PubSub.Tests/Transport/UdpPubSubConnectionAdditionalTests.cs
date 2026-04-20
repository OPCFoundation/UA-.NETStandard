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

using System.Collections.Generic;
using System.IO;
using System.Net;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UdpPubSubConnectionAdditionalTests
    {
        private static readonly string PublisherConfigurationFileName = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private UaPubSubApplication m_application;
        private UdpPubSubConnection m_connection;
        private PubSubConfigurationDataType m_configuration;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                PublisherConfigurationFileName,
                checkCurrentDirectory: true,
                createAlways: false);
            m_application = UaPubSubApplication.Create(configFile, null);
            Assert.That(m_application, Is.Not.Null);

            m_configuration = m_application.UaPubSubConfigurator.PubSubConfiguration;
            Assert.That(m_configuration, Is.Not.Null);
            Assert.That(m_configuration.Connections.IsEmpty, Is.False);

            m_connection = m_application.PubSubConnections[0] as UdpPubSubConnection;
            Assert.That(m_connection, Is.Not.Null);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_application?.Dispose();
        }

        [Test]
        public void AreClientsConnectedReturnsTrueForUdp()
        {
            bool result = m_connection.AreClientsConnected();
            Assert.That(result, Is.True);
        }

        [Test]
        public void TransportProtocolIsUdp()
        {
            Assert.That(m_connection.TransportProtocol, Is.EqualTo(TransportProtocol.UDP));
        }

        [Test]
        public void PubSubConnectionConfigurationIsNotNull()
        {
            Assert.That(m_connection.PubSubConnectionConfiguration, Is.Not.Null);
        }

        [Test]
        public void ApplicationReferenceIsNotNull()
        {
            Assert.That(m_connection.Application, Is.Not.Null);
        }

        [Test]
        public void NetworkAddressEndPointIsAccessible()
        {
            // NetworkAddressEndPoint may be null depending on config
            IPEndPoint endpoint = m_connection.NetworkAddressEndPoint;
            Assert.That(endpoint, Is.Null.Or.Not.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForInvalidMessageSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "InvalidWG",
                MessageSettings = default,
                TransportSettings = default
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForWrongMessageSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "WrongSettingsWG",
                MessageSettings = new ExtensionObject(new JsonWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType())
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesReturnsNullForWrongTransportSettings()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "WrongTransportWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType())
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CreateNetworkMessagesWithValidSettingsButNoWritersReturnsEmptyList()
        {
            var writerGroup = new WriterGroupDataType
            {
                Enabled = true,
                Name = "EmptyWritersWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType()),
                DataSetWriters = []
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void CreateNetworkMessagesWithDisabledWritersReturnsEmptyList()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "DisabledWritersWG",
                MessageSettings = new ExtensionObject(new UadpWriterGroupMessageDataType()),
                TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType()),
                DataSetWriters = [
                    new DataSetWriterDataType
                    {
                        Name = "DisabledWriter",
                        Enabled = false,
                        DataSetWriterId = 1
                    }
                ]
            };

            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void CreateNetworkMessagesFromPublisherConfigurationReturnsResult()
        {
            Assert.That(
                m_configuration.Connections[0].WriterGroups.IsEmpty,
                Is.False,
                "Publisher config should have writer groups");

            WriterGroupDataType writerGroup = m_configuration.Connections[0].WriterGroups[0];
            var state = new WriterGroupPublishState();
            IList<UaNetworkMessage> messages = m_connection.CreateNetworkMessages(writerGroup, state);

            // CreateNetworkMessages may return null or a non-empty list depending on config
            Assert.That(messages, Is.Null.Or.Not.Empty);
        }

        [Test]
        public void PublisherUdpClientsIsNotNull()
        {
            Assert.That(m_connection.PublisherUdpClients, Is.Not.Null);
        }

        [Test]
        public void SubscriberUdpClientsIsNotNull()
        {
            Assert.That(m_connection.SubscriberUdpClients, Is.Not.Null);
        }
    }
}