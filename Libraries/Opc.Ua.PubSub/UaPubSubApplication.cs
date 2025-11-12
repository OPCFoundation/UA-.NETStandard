/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Test;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// A class that runs an OPC UA PubSub application.
    /// </summary>
    public class UaPubSubApplication : IDisposable
    {
        private readonly List<IUaPubSubConnection> m_uaPubSubConnections;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives a message via its active connections
        /// </summary>
        public event EventHandler<RawDataReceivedEventArgs> RawDataReceived;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSets
        /// </summary>
        public event EventHandler<SubscribedDataEventArgs> DataReceived;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSet MetaData
        /// </summary>
        public event EventHandler<SubscribedDataEventArgs> MetaDataReceived;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSet PublisherEndpoints
        /// </summary>
        public event EventHandler<PublisherEndpointsEventArgs> PublisherEndpointsReceived;

        /// <summary>
        /// Event that is triggered before the configuration is updated with a new MetaData
        /// The configuration will not be updated if <see cref="ConfigurationUpdatingEventArgs.Cancel"/> flag is set on true.
        /// </summary>
        public event EventHandler<ConfigurationUpdatingEventArgs> ConfigurationUpdating;

        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSet MetaData
        /// </summary>
        public event EventHandler<DataSetWriterConfigurationEventArgs> DataSetWriterConfigurationReceived;

        /// <summary>
        /// Raised when the MQTT broker certificate is validated.
        /// </summary>
        /// <returns>
        /// Returns whether the broker certificate is valid and trusted.
        /// </returns>
        public ValidateBrokerCertificateHandler OnValidateBrokerCertificate;

        /// <summary>
        /// Initializes a new instance of the <see cref="UaPubSubApplication"/> class.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="dataStore">The current implementation of <see cref="IUaPubSubDataStore"/>
        /// used by this instance of pub sub application</param>
        /// <param name="applicationId"> The application id for instance.</param>
        private UaPubSubApplication(
            ITelemetryContext telemetry,
            IUaPubSubDataStore dataStore = null,
            string applicationId = null)
        {
            m_logger = telemetry.CreateLogger<UaPubSubApplication>();
            m_uaPubSubConnections = [];

            m_telemetry = telemetry;
            DataStore = dataStore ?? new UaPubSubDataStore();

            if (!string.IsNullOrEmpty(applicationId))
            {
                ApplicationId = applicationId;
            }
            else
            {
                ApplicationId = $"opcua:{System.Net.Dns.GetHostName()}:{RandomSource.Default.NextInt32(int.MaxValue):D10}";
            }

            DataCollector = new DataCollector(DataStore, m_telemetry);
            UaPubSubConfigurator = new UaPubSubConfigurator(m_telemetry);
            UaPubSubConfigurator.ConnectionAdded += UaPubSubConfigurator_ConnectionAdded;
            UaPubSubConfigurator.ConnectionRemoved += UaPubSubConfigurator_ConnectionRemoved;
            UaPubSubConfigurator.PublishedDataSetAdded
                += UaPubSubConfigurator_PublishedDataSetAdded;
            UaPubSubConfigurator.PublishedDataSetRemoved
                += UaPubSubConfigurator_PublishedDataSetRemoved;

            m_logger.LogInformation("An instance of UaPubSubApplication was created.");
        }

        /// <summary>
        /// The application id associated with the UA
        /// </summary>
        public string ApplicationId { get; set; }

        /// <summary>
        /// Get the list of SupportedTransportProfiles
        /// </summary>
        public static string[] SupportedTransportProfiles =>
            [Profiles.PubSubUdpUadpTransport, Profiles.PubSubMqttJsonTransport, Profiles
                .PubSubMqttUadpTransport];

        /// <summary>
        /// Get reference to the associated <see cref="UaPubSubConfigurator"/> instance.
        /// </summary>
        public UaPubSubConfigurator UaPubSubConfigurator { get; }

        /// <summary>
        /// Get reference to current DataStore. Write here all node values needed to be
        /// published by this PubSubApplication
        /// </summary>
        public IUaPubSubDataStore DataStore { get; }

        /// <summary>
        /// Get the read only list of <see cref="UaPubSubConnection"/> created for this
        /// Application instance
        /// </summary>
        public ReadOnlyList<IUaPubSubConnection> PubSubConnections => new(m_uaPubSubConnections);

        /// <summary>
        /// Get reference to current configured DataCollector for this UaPubSubApplication
        /// </summary>
        internal DataCollector DataCollector { get; }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> and associates it with a
        /// custom implementation of <see cref="IUaPubSubDataStore"/>.
        /// </summary>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/>
        /// used by this instance of pub sub application</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(IUaPubSubDataStore dataStore, ITelemetryContext telemetry)
        {
            return Create(new PubSubConfigurationDataType(), dataStore, telemetry);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters
        /// from the specified path.
        /// </summary>
        /// <param name="configFilePath">The path of the configuration path.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/>
        /// used by this instance of pub sub application</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        /// <exception cref="ArgumentNullException"><paramref name="configFilePath"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static UaPubSubApplication Create(
            string configFilePath,
            ITelemetryContext telemetry,
            IUaPubSubDataStore dataStore = null)
        {
            // validate input argument
            if (configFilePath == null)
            {
                throw new ArgumentNullException(nameof(configFilePath));
            }
            if (!File.Exists(configFilePath))
            {
                throw new ArgumentException(
                    "The specified file {0} does not exist",
                    configFilePath);
            }
            PubSubConfigurationDataType pubSubConfiguration =
                UaPubSubConfigurationHelper.LoadConfiguration(configFilePath, telemetry);

            return Create(pubSubConfiguration, dataStore, telemetry);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters from the
        /// specified <see cref="PubSubConfigurationDataType"/> parameter.
        /// </summary>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(
            ITelemetryContext telemetry)
        {
            return Create(null, null, telemetry);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters from the
        /// specified <see cref="PubSubConfigurationDataType"/> parameter.
        /// </summary>
        /// <param name="pubSubConfiguration">The configuration object.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(
            PubSubConfigurationDataType pubSubConfiguration,
            ITelemetryContext telemetry)
        {
            return Create(pubSubConfiguration, null, telemetry);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters from the
        /// specified <see cref="PubSubConfigurationDataType"/> parameter.
        /// </summary>
        /// <param name="pubSubConfiguration">The configuration object.</param>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/>
        /// used by this instance of pub sub application</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(
            PubSubConfigurationDataType pubSubConfiguration,
            IUaPubSubDataStore dataStore,
            ITelemetryContext telemetry)
        {
            // if no argument received, start with empty configuration
            pubSubConfiguration ??= new PubSubConfigurationDataType();

            var uaPubSubApplication = new UaPubSubApplication(telemetry, dataStore);
            uaPubSubApplication.UaPubSubConfigurator.LoadConfiguration(pubSubConfiguration);
            return uaPubSubApplication;
        }

        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Start()
        {
            m_logger.LogInformation("UaPubSubApplication is starting.");
            foreach (IUaPubSubConnection connection in m_uaPubSubConnections)
            {
                connection.Start();
            }
            m_logger.LogInformation("UaPubSubApplication was started.");
        }

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Stop()
        {
            m_logger.LogInformation("UaPubSubApplication is stopping.");
            foreach (IUaPubSubConnection connection in m_uaPubSubConnections)
            {
                connection.Stop();
            }
            m_logger.LogInformation("UaPubSubApplication is stopped.");
        }

        /// <summary>
        /// Raise <see cref="RawDataReceived"/> event
        /// </summary>
        internal void RaiseRawDataReceivedEvent(RawDataReceivedEventArgs e)
        {
            try
            {
                RawDataReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.RaiseRawDataReceivedEvent");
            }
        }

        /// <summary>
        /// Raise DataReceived event
        /// </summary>
        internal void RaiseDataReceivedEvent(SubscribedDataEventArgs e)
        {
            try
            {
                DataReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.RaiseDataReceivedEvent");
            }
        }

        /// <summary>
        /// Raise MetaDataReceived event
        /// </summary>
        internal void RaiseMetaDataReceivedEvent(SubscribedDataEventArgs e)
        {
            try
            {
                MetaDataReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.RaiseMetaDataReceivedEvent");
            }
        }

        /// <summary>
        /// Raise DatasetWriterConfigurationReceived event
        /// </summary>
        internal void RaiseDatasetWriterConfigurationReceivedEvent(
            DataSetWriterConfigurationEventArgs e)
        {
            try
            {
                DataSetWriterConfigurationReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.DatasetWriterConfigurationReceivedEvent");
            }
        }

        /// <summary>
        /// Raise PublisherEndpointsReceived event
        /// </summary>
        internal void RaisePublisherEndpointsReceivedEvent(PublisherEndpointsEventArgs e)
        {
            try
            {
                PublisherEndpointsReceived?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.RaisePublisherEndpointsReceivedEvent");
            }
        }

        /// <summary>
        /// Raise <see cref="ConfigurationUpdating"/> event
        /// </summary>
        internal void RaiseConfigurationUpdatingEvent(ConfigurationUpdatingEventArgs e)
        {
            try
            {
                ConfigurationUpdating?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UaPubSubApplication.RaiseConfigurationUpdatingEvent");
            }
        }

        /// <summary>
        /// Handler for PublishedDataSetAdded event
        /// </summary>
        private void UaPubSubConfigurator_PublishedDataSetAdded(
            object sender,
            PublishedDataSetEventArgs e)
        {
            DataCollector.AddPublishedDataSet(e.PublishedDataSetDataType);
        }

        /// <summary>
        /// Handler for PublishedDataSetRemoved event
        /// </summary>
        private void UaPubSubConfigurator_PublishedDataSetRemoved(
            object sender,
            PublishedDataSetEventArgs e)
        {
            DataCollector.RemovePublishedDataSet(e.PublishedDataSetDataType);
        }

        /// <summary>
        /// Handler for ConnectionRemoved event
        /// </summary>
        private void UaPubSubConfigurator_ConnectionRemoved(object sender, ConnectionEventArgs e)
        {
            IUaPubSubConnection removedUaPubSubConnection = null;
            foreach (IUaPubSubConnection connection in m_uaPubSubConnections)
            {
                if (connection.PubSubConnectionConfiguration.Equals(e.PubSubConnectionDataType))
                {
                    removedUaPubSubConnection = connection;
                    break;
                }
            }
            if (removedUaPubSubConnection != null)
            {
                m_uaPubSubConnections.Remove(removedUaPubSubConnection);
                removedUaPubSubConnection.Dispose();
            }
        }

        /// <summary>
        /// Handler for ConnectionAdded event
        /// </summary>
        private void UaPubSubConfigurator_ConnectionAdded(object sender, ConnectionEventArgs e)
        {
            m_uaPubSubConnections.Add(ObjectFactory.CreateConnection(
                this,
                e.PubSubConnectionDataType,
                m_telemetry));
        }

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="UaPublisher"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///  When overridden in a derived class, releases the unmanaged resources used by that class
        ///  and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"> true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                UaPubSubConfigurator.ConnectionAdded -= UaPubSubConfigurator_ConnectionAdded;
                UaPubSubConfigurator.ConnectionRemoved -= UaPubSubConfigurator_ConnectionRemoved;
                UaPubSubConfigurator.PublishedDataSetAdded
                    -= UaPubSubConfigurator_PublishedDataSetAdded;
                UaPubSubConfigurator.PublishedDataSetRemoved
                    -= UaPubSubConfigurator_PublishedDataSetRemoved;

                Stop();
                // free managed resources
                foreach (IUaPubSubConnection connection in m_uaPubSubConnections)
                {
                    connection.Dispose();
                }
                m_uaPubSubConnections.Clear();
            }
        }
    }

    /// <summary>
    /// A delegate which validates the MQTT broker certificate.
    /// </summary>
    /// <param name="brokerCertificate">The broker certificate.</param>
    /// <returns>Returns whether the broker certificate is valid and trusted.</returns>
    public delegate bool ValidateBrokerCertificateHandler(X509Certificate2 brokerCertificate);
}
