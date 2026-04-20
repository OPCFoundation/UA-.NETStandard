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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture]
    [Category("Transport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubConnectionTests
    {
        private static readonly string s_publisherConfigPath = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private static readonly string s_subscriberConfigPath = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        private UaPubSubApplication m_app;
        private UaPubSubConnection m_connection;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void Setup()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            m_telemetry = NUnitTelemetryContext.Create();
            m_app = UaPubSubApplication.Create(configFile, m_telemetry);
            m_connection = m_app.PubSubConnections[0] as UaPubSubConnection;
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            m_app?.Dispose();
        }

        [Test]
        public void ConnectionHasTransportProtocol()
        {
            Assert.That(m_connection.TransportProtocol, Is.Not.EqualTo(TransportProtocol.NotAvailable));
        }

        [Test]
        public void ConnectionHasConfiguration()
        {
            Assert.That(m_connection.PubSubConnectionConfiguration, Is.Not.Null);
        }

        [Test]
        public void ConnectionHasApplication()
        {
            Assert.That(m_connection.Application, Is.Not.Null);
            Assert.That(m_connection.Application, Is.SameAs(m_app));
        }

        [Test]
        public void ConnectionIsNotRunningByDefault()
        {
            Assert.That(m_connection.IsRunning, Is.False);
        }

        [Test]
        public void ConnectionMessageContextIsNotNull()
        {
            Assert.That(m_connection.MessageContext, Is.Not.Null);
        }

        [Test]
        public void ConnectionMessageContextCanBeSet()
        {
            IServiceMessageContext original = m_connection.MessageContext;
            try
            {
                var newContext = ServiceMessageContext.Create(m_telemetry);
                m_connection.MessageContext = newContext;
                Assert.That(m_connection.MessageContext, Is.SameAs(newContext));
            }
            finally
            {
                m_connection.MessageContext = original;
            }
        }

        [Test]
        public void ConnectionNameIsSet()
        {
            string name = m_connection.PubSubConnectionConfiguration.Name;
            Assert.That(name, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void CanPublishReturnsFalseWhenNotRunning()
        {
            Assert.That(m_connection.IsRunning, Is.False);
            var writerGroup = new WriterGroupDataType { Enabled = true };
            Assert.That(m_connection.CanPublish(writerGroup), Is.False);
        }

        [Test]
        public void CanPublishReturnsFalseForNullWriterGroup()
        {
            var writerGroup = new WriterGroupDataType { Enabled = true, Name = "NonExistent" };
            Assert.That(m_connection.CanPublish(writerGroup), Is.False);
        }

        [Test]
        public void GetOperationalDataSetReadersReturnsEmptyWhenNoReaders()
        {
            List<DataSetReaderDataType> readers = m_connection.GetOperationalDataSetReaders();
            Assert.That(readers, Is.Not.Null);
            Assert.That(readers, Is.Empty);
        }

        [Test]
        public void GetOperationalDataSetReadersFromSubscriberConfig()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            using var subscriberApp = UaPubSubApplication.Create(configFile, m_telemetry);
            Assert.That(subscriberApp.PubSubConnections.Count, Is.GreaterThan(0));

            var subscriberConnection = subscriberApp.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(subscriberConnection, Is.Not.Null);

            List<DataSetReaderDataType> readers = subscriberConnection.GetOperationalDataSetReaders();
            Assert.That(readers, Is.Not.Null);
            Assert.That(readers, Is.Not.Empty);
        }

        [Test]
        public void StartSetsIsRunning()
        {
            using UaPubSubApplication app = CreateUdpApp();
            var connection = app.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(connection, Is.Not.Null);

            app.Start();
            try
            {
                Assert.That(connection.IsRunning, Is.True);
            }
            finally
            {
                app.Stop();
            }
        }

        [Test]
        public void StopClearsIsRunning()
        {
            using UaPubSubApplication app = CreateUdpApp();
            var connection = app.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(connection, Is.Not.Null);

            app.Start();
            Assert.That(connection.IsRunning, Is.True);

            app.Stop();
            Assert.That(connection.IsRunning, Is.False);
        }

        [Test]
        public void DisposeStopsConnection()
        {
            UaPubSubApplication app = CreateUdpApp();
            var connection = app.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(connection, Is.Not.Null);

            app.Start();
            Assert.That(connection.IsRunning, Is.True);

            app.Dispose();
            Assert.That(connection.IsRunning, Is.False);
        }

        [Test]
        public void CreateConnectionFromProgrammaticConfig()
        {
            var connectionConfig = new PubSubConnectionDataType
            {
                Name = "TestConnection",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:4840"
                }),
                PublisherId = new Variant((ushort)1),
                Enabled = true,
                WriterGroups = [],
                ReaderGroups = []
            };

            var pubSubConfig = new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = [connectionConfig]
            };

            using UaPubSubApplication app = UaPubSubApplication.Create(pubSubConfig, m_telemetry);
            Assert.That(app.PubSubConnections.Count, Is.EqualTo(1));

            var connection = app.PubSubConnections[0] as UaPubSubConnection;
            Assert.That(connection, Is.Not.Null);
            Assert.That(connection.PubSubConnectionConfiguration.Name, Is.EqualTo("TestConnection"));
            Assert.That(connection.TransportProtocol, Is.EqualTo(TransportProtocol.UDP));
            Assert.That(connection.Application, Is.SameAs(app));
            Assert.That(connection.IsRunning, Is.False);
        }

        private UaPubSubApplication CreateUdpApp()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            return UaPubSubApplication.Create(configFile, m_telemetry);
        }
    }
}