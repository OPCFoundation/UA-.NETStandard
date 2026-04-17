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
    [TestFixture]
    [Category("PubSub")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UaPubSubApplicationEventTests
    {
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        /// <summary>
        /// RaiseRawDataReceivedEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void RawDataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.RawDataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseRawDataReceivedEvent(new RawDataReceivedEventArgs()));
        }

        /// <summary>
        /// RaiseDataReceivedEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void DataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.DataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseDataReceivedEvent(new SubscribedDataEventArgs()));
        }

        /// <summary>
        /// RaiseMetaDataReceivedEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void MetaDataReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.MetaDataReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseMetaDataReceivedEvent(new SubscribedDataEventArgs()));
        }

        /// <summary>
        /// RaiseDatasetWriterConfigurationReceivedEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void DataSetWriterConfigurationReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.DataSetWriterConfigurationReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseDatasetWriterConfigurationReceivedEvent(
                    new DataSetWriterConfigurationEventArgs()));
        }

        /// <summary>
        /// RaisePublisherEndpointsReceivedEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void PublisherEndpointsReceivedSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.PublisherEndpointsReceived += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaisePublisherEndpointsReceivedEvent(new PublisherEndpointsEventArgs()));
        }

        /// <summary>
        /// RaiseConfigurationUpdatingEvent swallows subscriber exceptions
        /// </summary>
        [Test]
        public void ConfigurationUpdatingSwallowsSubscriberException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.ConfigurationUpdating += (_, _) => throw new InvalidOperationException("test");

            Assert.DoesNotThrow(() =>
                app.RaiseConfigurationUpdatingEvent(new ConfigurationUpdatingEventArgs()));
        }

        /// <summary>
        /// Events fire with args when no exception
        /// </summary>
        [Test]
        public void RawDataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.RawDataReceived += (_, _) => fired = true;

            app.RaiseRawDataReceivedEvent(new RawDataReceivedEventArgs());
            Assert.That(fired, Is.True);
        }

        /// <summary>
        /// DataReceived event fires with args when no exception
        /// </summary>
        [Test]
        public void DataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.DataReceived += (_, _) => fired = true;

            app.RaiseDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(fired, Is.True);
        }

        /// <summary>
        /// MetaDataReceived event fires successfully
        /// </summary>
        [Test]
        public void MetaDataReceivedEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.MetaDataReceived += (_, _) => fired = true;

            app.RaiseMetaDataReceivedEvent(new SubscribedDataEventArgs());
            Assert.That(fired, Is.True);
        }

        /// <summary>
        /// ConfigurationUpdating event fires successfully
        /// </summary>
        [Test]
        public void ConfigurationUpdatingEventFiresSuccessfully()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            bool fired = false;
            app.ConfigurationUpdating += (_, _) => fired = true;

            app.RaiseConfigurationUpdatingEvent(new ConfigurationUpdatingEventArgs());
            Assert.That(fired, Is.True);
        }

        /// <summary>
        /// PDS add triggers DataCollector registration
        /// </summary>
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

        /// <summary>
        /// PDS remove triggers DataCollector removal
        /// </summary>
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

        /// <summary>
        /// App creates with null configuration
        /// </summary>
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

        /// <summary>
        /// App create with explicit data store
        /// </summary>
        [Test]
        public void CreateWithCustomDataStore()
        {
            var dataStore = new UaPubSubDataStore();
            using UaPubSubApplication app = UaPubSubApplication.Create(dataStore, m_telemetry);
            Assert.That(app.DataStore, Is.SameAs(dataStore));
        }

        /// <summary>
        /// Dispose can be called multiple times safely
        /// </summary>
        [Test]
        public void DisposeCanBeCalledMultipleTimes()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(m_telemetry);
            app.Dispose();
            Assert.DoesNotThrow(app.Dispose);
        }

        /// <summary>
        /// SupportedTransportProfiles contains expected values
        /// </summary>
        [Test]
        public void SupportedTransportProfilesContainsExpectedValues()
        {
            string[] profiles = UaPubSubApplication.SupportedTransportProfiles;
            Assert.That(profiles, Is.Not.Null);
            Assert.That(profiles, Has.Length.GreaterThanOrEqualTo(3));
        }
    }
}
