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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests
{
    /// <summary>
    /// Tests for UaPubSubApplication event handlers including exception swallowing,
    /// connection/PDS add/remove handlers, and lifecycle events.
    /// </summary>
    [TestFixture(Description = "Event handler coverage tests for UaPubSubApplication")]
    [Parallelizable]
    public class UaPubSubApplicationEventTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        // RaiseRawDataReceivedEvent swallows subscriber exceptions
        [Test]
        public void RawDataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.RawDataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseRawDataReceivedEvent(new RawDataReceivedEventArgs()));
        }

        // RaiseDataReceivedEvent swallows subscriber exceptions
        [Test]
        public void DataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.DataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseDataReceivedEvent(new SubscribedDataEventArgs()));
        }

        // RaiseMetaDataReceivedEvent swallows subscriber exceptions
        [Test]
        public void MetaDataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.MetaDataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseMetaDataReceivedEvent(new SubscribedDataEventArgs()));
        }

        // RaiseDatasetWriterConfigurationReceivedEvent swallows subscriber exceptions
        [Test]
        public void DataSetWriterConfigurationReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.DataSetWriterConfigurationReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseDatasetWriterConfigurationReceivedEvent(
                    new DataSetWriterConfigurationEventArgs()));
        }

        // RaisePublisherEndpointsReceivedEvent swallows subscriber exceptions
        [Test]
        public void PublisherEndpointsReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.PublisherEndpointsReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaisePublisherEndpointsReceivedEvent(new PublisherEndpointsEventArgs()));
        }

        // RaiseConfigurationUpdatingEvent swallows subscriber exceptions
        [Test]
        public void ConfigurationUpdatingSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.ConfigurationUpdating += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseConfigurationUpdatingEvent(new ConfigurationUpdatingEventArgs()));
        }

        // Events fire with args when no exception
        [Test]
        public void RawDataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.RawDataReceived += (_, _) => fired = true;

            app.RaiseRawDataReceivedEvent(new RawDataReceivedEventArgs());
            Assert.That(fired, Is.True);
        }

        // DataReceived event fires with args when no exception
        [Test]
        public void DataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.DataReceived += (_, _) => fired = true;

            app.RaiseDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(fired, Is.True);
        }

        // MetaDataReceived event fires successfully
        [Test]
        public void MetaDataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.MetaDataReceived += (_, _) => fired = true;

            app.RaiseMetaDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(fired, Is.True);
        }

        // ConfigurationUpdating event fires successfully
        [Test]
        public void ConfigurationUpdatingEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.ConfigurationUpdating += (_, _) => fired = true;

            app.RaiseConfigurationUpdatingEvent(new ConfigurationUpdatingEventArgs());
            Assert.That(fired, Is.True);
        }

        // PDS add triggers DataCollector registration
        [Test]
        public void AddPublishedDataSetRegistersWithDataCollector()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);

            var pds = new PublishedDataSetDataType
            {
                Name = "TestPDS",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "TestPDS",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.Int32
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [new PublishedVariableDataType()]
                })
            };

            app.UaPubSubConfigurator.AddPublishedDataSet(pds);

            Opc.Ua.PubSub.PublishedData.DataCollector collector = app.DataCollector;
            PublishedDataSetDataType found = collector.GetPublishedDataSet("TestPDS");
            Assert.That(found, Is.Not.Null);
        }

        // PDS remove triggers DataCollector removal
        [Test]
        public void RemovePublishedDataSetUnregistersFromDataCollector()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);

            var pds = new PublishedDataSetDataType
            {
                Name = "TestPDS",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "TestPDS",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = "F1",
                            BuiltInType = (byte)BuiltInType.Int32
                        }
                    ]
                },
                DataSetSource = new ExtensionObject(new PublishedDataItemsDataType
                {
                    PublishedData = [new PublishedVariableDataType()]
                })
            };

            app.UaPubSubConfigurator.AddPublishedDataSet(pds);
            app.UaPubSubConfigurator.RemovePublishedDataSet(pds);

            PublishedDataSetDataType found = app.DataCollector.GetPublishedDataSet("TestPDS");
            Assert.That(found, Is.Null);
        }

        // App creates with null configuration
        [Test]
        public void CreateWithNullConfigurationSucceeds()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(
                (PubSubConfigurationDataType)null,
                null,
                m_telemetry);
            Assert.That(app, Is.Not.Null);
            Assert.That(app.ApplicationId, Is.Not.Null.And.Not.Empty);
        }

        // App create with explicit data store
        [Test]
        public void CreateWithCustomDataStore()
        {
            var dataStore = new UaPubSubDataStore();
            using UaPubSubApplication app = UaPubSubApplication.Create(dataStore, m_telemetry);
            Assert.That(app.DataStore, Is.SameAs(dataStore));
        }

        // Dispose can be called multiple times safely
        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.Dispose();
            Assert.DoesNotThrow(() => app.Dispose());
        }

        // SupportedTransportProfiles contains expected values
        [Test]
        public void SupportedTransportProfilesContainsExpectedValues()
        {
            string[] profiles = UaPubSubApplication.SupportedTransportProfiles;
            Assert.That(profiles, Is.Not.Null);
            Assert.That(profiles.Length, Is.GreaterThanOrEqualTo(3));
        }
    }
}
