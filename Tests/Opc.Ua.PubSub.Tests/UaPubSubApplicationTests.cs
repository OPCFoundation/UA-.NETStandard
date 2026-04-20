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
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture]
    [Category("PubSub")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubApplicationTests
    {
        private static readonly string s_publisherConfigPath =
            Path.Combine("Configuration", "PublisherConfiguration.xml");

        [Test]
        public void CreateWithDataStoreReturnsApplication()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var dataStore = new UaPubSubDataStore();
            using var app = UaPubSubApplication.Create(dataStore, telemetry);
            Assert.That(app, Is.Not.Null);
            Assert.That(app.DataStore, Is.SameAs(dataStore));
        }

        [Test]
        public void CreateWithTelemetryOnlyReturnsApplication()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app, Is.Not.Null);
        }

        [Test]
        public void CreateWithNullConfigReturnsApplication()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using UaPubSubApplication app = UaPubSubApplication.Create(
                (PubSubConfigurationDataType)null, telemetry);
            Assert.That(app, Is.Not.Null);
        }

        [Test]
        public void CreateWithEmptyConfigReturnsEmptyConnections()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var config = new PubSubConfigurationDataType { Enabled = true };
            using UaPubSubApplication app = UaPubSubApplication.Create(config, telemetry);
            Assert.That(app, Is.Not.Null);
            Assert.That(app.PubSubConnections.Count, Is.Zero);
        }

        [Test]
        public void CreateWithConfigFilePath()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            Assert.That(configFile, Is.Not.Null, "Publisher config file not found");

            using var app = UaPubSubApplication.Create(configFile, telemetry);
            Assert.That(app, Is.Not.Null);
            Assert.That(app.PubSubConnections.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void CreateWithNullFilePathThrowsArgumentNullException()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.That(
                () => UaPubSubApplication.Create((string)null, telemetry),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CreateWithNonExistentFilePathThrowsArgumentException()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.That(
                () => UaPubSubApplication.Create("NonExistentFile.xml", telemetry),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ApplicationIdIsNotNullOrEmpty()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.ApplicationId, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ApplicationIdCanBeSet()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            const string newId = "TestApplicationId";
            app.ApplicationId = newId;
            Assert.That(app.ApplicationId, Is.EqualTo(newId));
        }

        [Test]
        public void SupportedTransportProfilesHasThreeEntries()
        {
            string[] profiles = UaPubSubApplication.SupportedTransportProfiles;
            Assert.That(profiles, Is.Not.Null);
            Assert.That(profiles, Has.Length.EqualTo(3));
        }

        [Test]
        public void DataStoreIsNotNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.DataStore, Is.Not.Null);
        }

        [Test]
        public void UaPubSubConfiguratorIsNotNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.UaPubSubConfigurator, Is.Not.Null);
        }

        [Test]
        public void PubSubConnectionsIsNotNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.PubSubConnections.Count, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void StartAndStopDoNotThrowWithNoConnections()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.Start, Throws.Nothing);
            Assert.That(app.Stop, Throws.Nothing);
        }

        [Test]
        public void DisposeDoesNotThrowWithNoConnections()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            Assert.That(app.Dispose, Throws.Nothing);
        }

        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            app.Dispose();
            Assert.That(app.Dispose, Throws.Nothing);
        }

        [Test]
        public void StartAndStopWithConfiguredConnections()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string configFile = Utils.GetAbsoluteFilePath(
                s_publisherConfigPath,
                checkCurrentDirectory: true,
                createAlways: false);
            Assert.That(configFile, Is.Not.Null, "Publisher config file not found");

            using var app = UaPubSubApplication.Create(configFile, telemetry);
            Assert.That(app.Start, Throws.Nothing);
            Assert.That(app.Stop, Throws.Nothing);
        }

        [Test]
        public void DataReceivedEventCanBeSubscribed()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            bool raised = false;
            app.DataReceived += (sender, args) => raised = true;
            app.RaiseDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(raised, Is.True);
        }

        [Test]
        public void MetaDataReceivedEventCanBeSubscribed()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            bool raised = false;
            app.MetaDataReceived += (sender, args) => raised = true;
            app.RaiseMetaDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(raised, Is.True);
        }

        [Test]
        public void RawDataReceivedEventCanBeSubscribed()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            bool raised = false;
            app.RawDataReceived += (sender, args) => raised = true;
            app.RaiseRawDataReceivedEvent(new RawDataReceivedEventArgs());
            Assert.That(raised, Is.True);
        }

        [Test]
        public void ConfigurationUpdatingEventCanBeSubscribed()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var app = UaPubSubApplication.Create(telemetry);
            bool raised = false;
            app.ConfigurationUpdating += (sender, args) => raised = true;
            app.RaiseConfigurationUpdatingEvent(
                new ConfigurationUpdatingEventArgs());
            Assert.That(raised, Is.True);
        }
    }
}
