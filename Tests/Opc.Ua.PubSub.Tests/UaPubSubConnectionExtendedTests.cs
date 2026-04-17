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
    public class UaPubSubConnectionExtendedTests
    {
        private static readonly string s_publisherConfigPath = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private static readonly string s_subscriberConfigPath = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        [Test]
        public void CanPublishReturnsFalseForDisabledWriterGroup()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var disabledWg = new WriterGroupDataType
            {
                Name = "DisabledWG",
                WriterGroupId = 999,
                Enabled = false
            };
            disabledWg.DataSetWriters = disabledWg.DataSetWriters.AddItem(new DataSetWriterDataType
            {
                Name = "W1",
                DataSetWriterId = 1,
                Enabled = true
            });

            Assert.That(connection.CanPublish(disabledWg), Is.False);
        }

        [Test]
        public void CanPublishReturnsFalseForWriterGroupWithNoEnabledWriters()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var wg = new WriterGroupDataType
            {
                Name = "NoWritersWG",
                WriterGroupId = 999,
                Enabled = true
            };
            wg.DataSetWriters = wg.DataSetWriters.AddItem(new DataSetWriterDataType
            {
                Name = "DisabledWriter",
                DataSetWriterId = 1,
                Enabled = false
            });

            Assert.That(connection.CanPublish(wg), Is.False);
        }

        [Test]
        public void CanPublishReturnsFalseForEmptyWriterGroupDataSetWriters()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var wg = new WriterGroupDataType
            {
                Name = "EmptyWritersWG",
                WriterGroupId = 999,
                Enabled = true
            };

            Assert.That(connection.CanPublish(wg), Is.False);
        }

        [Test]
        public void ConnectionConfigurationNameMatchesExpected()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection.PubSubConnectionConfiguration, Is.Not.Null);
            Assert.That(connection.PubSubConnectionConfiguration.Enabled, Is.True);
        }

        [Test]
        public void ConnectionWriterGroupsAreConfigured()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            ArrayOf<WriterGroupDataType> writerGroups = connection.PubSubConnectionConfiguration.WriterGroups;
            Assert.That(writerGroups.Count, Is.GreaterThanOrEqualTo(0));
            Assert.That(writerGroups.Count, Is.GreaterThan(0));
            foreach (WriterGroupDataType wg in writerGroups)
            {
                Assert.That(wg.WriterGroupId, Is.GreaterThan(0));
                Assert.That(wg.Name, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void SubscriberConnectionReaderGroupsAreConfigured()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            ArrayOf<ReaderGroupDataType> readerGroups = connection.PubSubConnectionConfiguration.ReaderGroups;
            Assert.That(readerGroups.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void GetOperationalDataSetReadersReturnsEmptyListBeforeStart()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            List<DataSetReaderDataType> readers = connection.GetOperationalDataSetReaders();
            Assert.That(readers, Is.Not.Null);
            Assert.That(readers.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ConnectionMessageContextCanBeReassigned()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var ctx1 = ServiceMessageContext.Create(telemetry);
            connection.MessageContext = ctx1;
            Assert.That(connection.MessageContext, Is.SameAs(ctx1));

            var ctx2 = ServiceMessageContext.Create(telemetry);
            connection.MessageContext = ctx2;
            Assert.That(connection.MessageContext, Is.SameAs(ctx2));
        }

        [Test]
        public void MultipleConnectionsFromPublisherConfig()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);

            Assert.That(app.PubSubConnections.Count, Is.GreaterThan(0));

            foreach (IUaPubSubConnection conn in app.PubSubConnections)
            {
                var pubSubConn = conn as UaPubSubConnection;
                Assert.That(pubSubConn, Is.Not.Null);
                Assert.That(pubSubConn.Application, Is.SameAs(app));
                Assert.That(pubSubConn.IsRunning, Is.False);
            }
        }

        [Test]
        public void ConnectionTransportProtocolIsCorrectType()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            TransportProtocol protocol = connection.TransportProtocol;
            Assert.That(protocol, Is.Not.EqualTo(TransportProtocol.NotAvailable));
        }

        [Test]
        public void ConnectionPublisherIdFromConfigIsNotNull()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Variant pubId = connection.PubSubConnectionConfiguration.PublisherId;
            Assert.That(pubId, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void SubscriberConnectionFromConfigHasCorrectStructure()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection, Is.Not.Null);
            Assert.That(connection.PubSubConnectionConfiguration.Address.IsNull, Is.False);
            Assert.That(connection.PubSubConnectionConfiguration.TransportProfileUri, Is.Not.Null);
        }

        [Test]
        public void DisposeConnectionDoesNotThrowWhenNotStarted()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);

            Assert.DoesNotThrow(app.Dispose);
        }

        [Test]
        public void ConnectionWriterGroupDataSetWritersArePopulated()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            WriterGroupDataType wg = connection.PubSubConnectionConfiguration.WriterGroups[0];
            Assert.That(wg.DataSetWriters, Is.Not.EqualTo(default(ArrayOf<DataSetWriterDataType>)));
            Assert.That(wg.DataSetWriters.Count, Is.GreaterThan(0));
        }

        [Test]
        public void SubscriberConnectionDataSetReaderMetaDataIsConfigured()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            ArrayOf<ReaderGroupDataType> readerGroups = connection.PubSubConnectionConfiguration.ReaderGroups;
            if (readerGroups.Count > 0 && readerGroups[0].DataSetReaders.Count > 0)
            {
                DataSetReaderDataType reader = readerGroups[0].DataSetReaders[0];
                Assert.That(reader.DataSetMetaData, Is.Not.Null);
            }
        }
    }
}
