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
    public class UaPubSubConnectionAdditionalTests
    {
        private static readonly string s_publisherConfigPath = Path.Combine(
            "Configuration",
            "PublisherConfiguration.xml");

        private static readonly string s_subscriberConfigPath = Path.Combine(
            "Configuration",
            "SubscriberConfiguration.xml");

        [Test]
        public void ConnectionMessageContextIsNotNull()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection, Is.Not.Null);
            Assert.That(connection.MessageContext, Is.Not.Null);
        }

        [Test]
        public void ConnectionCanSetMessageContext()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var newContext = ServiceMessageContext.Create(telemetry);
            connection.MessageContext = newContext;

            Assert.That(connection.MessageContext, Is.SameAs(newContext));
        }

        [Test]
        public void ConnectionIsNotRunningByDefault()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection.IsRunning, Is.False);
        }

        [Test]
        public void ConnectionCanPublishReturnsFalseWhenNotRunning()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var writerGroup = new WriterGroupDataType
            {
                Name = "TestWG",
                WriterGroupId = 1,
                Enabled = true
            };

            Assert.That(connection.CanPublish(writerGroup), Is.False);
        }

        [Test]
        public void ConnectionHasApplication()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection.Application, Is.SameAs(app));
        }

        [Test]
        public void ConnectionHasTransportProtocol()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(
                connection.TransportProtocol,
                Is.Not.EqualTo(TransportProtocol.NotAvailable));
        }

        [Test]
        public void ConnectionHasConnectionConfiguration()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection.PubSubConnectionConfiguration, Is.Not.Null);
            Assert.That(
                connection.PubSubConnectionConfiguration.Name,
                Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConnectionGetOperationalDataSetReadersReturnsEmptyWhenNotOperational()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            List<DataSetReaderDataType> readers = connection.GetOperationalDataSetReaders();
            Assert.That(readers, Is.Not.Null);
        }

        [Test]
        public void SubscriberConnectionHasReaderGroups()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_subscriberConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(connection, Is.Not.Null);
            Assert.That(
                connection.PubSubConnectionConfiguration.ReaderGroups.Count,
                Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ConnectionDisposeMultipleTimesDoesNotThrow()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            app.Dispose();
            Assert.DoesNotThrow(app.Dispose);
        }

        [Test]
        public void ConnectionCanPublishReturnsFalseWithNullWriterGroup()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            var emptyWriterGroup = new WriterGroupDataType
            {
                Name = "EmptyWG",
                WriterGroupId = 999,
                Enabled = true,
                DataSetWriters = []
            };

            Assert.That(connection.CanPublish(emptyWriterGroup), Is.False);
        }

        [Test]
        public void PublisherConnectionHasWriterGroups()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(
                connection.PubSubConnectionConfiguration.WriterGroups.Count,
                Is.GreaterThan(0));
        }

        [Test]
        public void ConnectionPublisherIdIsSet()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(
                connection.PubSubConnectionConfiguration.PublisherId,
                Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void SubscriberGetOperationalDataSetReadersReturnsListWhenNotStarted()
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
        }

        [Test]
        public void ConnectionAddressIsSet()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(
                connection.PubSubConnectionConfiguration.Address.IsNull,
                Is.False);
        }

        [Test]
        public void ConnectionTransportProfileUriIsSet()
        {
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(configFile, telemetry);
            var connection = app.PubSubConnections[0] as UaPubSubConnection;

            Assert.That(
                connection.PubSubConnectionConfiguration.TransportProfileUri,
                Is.Not.Null.And.Not.Empty);
        }
    }
}
