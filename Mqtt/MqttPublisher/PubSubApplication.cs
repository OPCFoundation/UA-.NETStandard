using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using uPLibrary.Networking.M2Mqtt;
using Opc.Ua;
using Opc.Ua.Client;

namespace MqttSamplePublisher
{
    public class ApplicationStartSettings
    {
        public string EndpointUrl = String.Empty;
        public string ConfigFile = String.Empty;
        public int Timeout = System.Threading.Timeout.Infinite;
        public bool AutoAccept = true;
    }

    public class LogMessageEventArgs : EventArgs
    {
        public LogMessageEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }

    class PubSubApplication
    {
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
        private bool m_useSecurity;
        private int m_sequenceNumber = 0;
        private Queue<JsonNetworkMessage> m_messages;
        private MqttClient m_mqttClient;

        private Dictionary<string, PubSubConnectionDataType> m_connections;
        private Dictionary<string, WriterGroupDataType> m_writerGroups;
        private Dictionary<string, DataSetWriterDataType> m_datasetWriters;
        private Dictionary<string, PublishedDataSetDataType> m_datasets;

        private class DataSetWriterCache
        {
            public PubSubConnectionDataType Connection;
            public WriterGroupDataType WriterGroup;
            public DataSetWriterDataType DataSetWriter;
            public List<MonitoredItem> MonitoredItems;
            public PublishedDataSetDataType DataSet;
        }

        public event EventHandler<LogMessageEventArgs> LogMessage;

        public PubSubApplication()
        {
            m_messages = new Queue<JsonNetworkMessage>();
            m_connections = new Dictionary<string, PubSubConnectionDataType>();
            m_writerGroups = new Dictionary<string, WriterGroupDataType>();
            m_datasetWriters = new Dictionary<string, DataSetWriterDataType>();
            m_datasets = new Dictionary<string, PublishedDataSetDataType>();
        }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            Log("Received Certificate: {0} {1}", e.Certificate.Subject, e.Error.StatusCode);
            e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
        }

        private void Log(string format, params object[] args)
        {
            if (LogMessage != null)
            {
                string message = format;

                if (args != null && args.Length > 0)
                {
                    message = String.Format(CultureInfo.InvariantCulture, format, args);
                }

                LogMessage(this, new LogMessageEventArgs(message));
            }
        }

        private async Task<X509Certificate2> FindIssuer(ApplicationConfiguration configuration, string subjectName)
        {
            DirectoryCertificateStore store = (DirectoryCertificateStore)configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();

            foreach (var ii in await store.Enumerate())
            {
                if (Utils.CompareDistinguishedName(ii.Subject, subjectName))
                {
                    return store.LoadPrivateKey(ii.Thumbprint, null, null);
                }
            }

            return null;
        }

        private async Task GenerateCertificate(ApplicationConfiguration configuration)
        {
            Log("    INFO: Creating new application certificate: {0}", configuration.ApplicationName);

            string issuerName = "CN=TestCA,DC=" + System.Net.Dns.GetHostName();
            X509Certificate2 issuer = await FindIssuer(configuration, issuerName);

            if (issuer == null)
            {
                Log("    INFO: Creating new issuer certificate: {0}", issuerName);

                issuer = CertificateFactory.CreateCertificate(
                   configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType,
                   configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath,
                   null,
                   null,
                   null,
                   issuerName,
                   null,
                   CertificateFactory.defaultKeySize,
                   DateTime.UtcNow - TimeSpan.FromDays(1),
                   CertificateFactory.defaultLifeTime,
                   CertificateFactory.defaultHashSize,
                   true,
                   null,
                   null);
            }

            var verification = CertificateFactory.CreateCertificate(
                configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                null,
                null,
                null,
                "CN=C40538A3D342CAD24C2009C8765D6B5685D274DDAD36DE2A",
                null,
                CertificateFactory.defaultKeySize,
                DateTime.UtcNow - TimeSpan.FromDays(1),
                CertificateFactory.defaultLifeTime,
                CertificateFactory.defaultHashSize,
                false,
                issuer,
                null);

            Log("    INFO: Creating new application certificate: {0}", configuration.ApplicationName);

            X509Certificate2 certificate = CertificateFactory.CreateCertificate(
                configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                null,
                configuration.ApplicationUri,
                configuration.ApplicationName,
                configuration.SecurityConfiguration.ApplicationCertificate.SubjectName,
                null,
                CertificateFactory.defaultKeySize,
                DateTime.UtcNow - TimeSpan.FromDays(1),
                CertificateFactory.defaultLifeTime,
                CertificateFactory.defaultHashSize,
                false,
                issuer,
                null);

            configuration.SecurityConfiguration.ApplicationCertificate.Certificate = certificate;
        }

        private async Task<ApplicationConfiguration> CreateApplicationConfiguration(ApplicationStartSettings settings)
        {
            CertificateIdentifier applicationCertificate = new CertificateIdentifier
            {
                StoreType = "Directory",
                StorePath = "pki/own",
                SubjectName = "UA Core Sample Client"
            };

            Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);

            var config = new ApplicationConfiguration()
            {
                ApplicationName = "MQTT Sample Publisher",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:" + Utils.GetHostName() + ":OPCFoundation:MqttSamplePublisher",
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = applicationCertificate,
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/trusted"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/issuers"
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "pki/rejected"
                    },
                    NonceLength = 32,
                    AutoAcceptUntrustedCertificates = settings.AutoAccept
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };

            await config.Validate(ApplicationType.Client);

            bool haveAppCertificate = config.SecurityConfiguration.ApplicationCertificate.Certificate != null;

            if (!haveAppCertificate)
            {
                await GenerateCertificate(config);
            }

            m_useSecurity = haveAppCertificate = config.SecurityConfiguration.ApplicationCertificate.Certificate != null;

            if (haveAppCertificate)
            {
                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);

                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
                }
            }
            else
            {
                Log("    WARN: missing application certificate, using unsecure connection.");
            }

            Utils.SetTraceMask(Utils.TraceMasks.None);

            return config;
        }

        private Task CreateSubscription()
        {
            Subscription subscription = m_subscription = new Subscription();

            subscription.PublishingInterval = 100;
            subscription.PublishingEnabled = true;
            subscription.KeepAliveCount = 100;
            subscription.LifetimeCount = 1000;
            subscription.MaxNotificationsPerPublish = 10000;

            m_session.AddSubscription(subscription);
            subscription.Create();

            return Task.CompletedTask;
        }

        private async Task<PublishedDataSetDataType> LoadDataSet(string name)
        {
            PublishedDataSetDataType dataset = null;

            using (var istrm = new System.IO.StreamReader(name + ".json"))
            {
                var json = await istrm.ReadToEndAsync();

                using (JsonDecoder decoder = new JsonDecoder(json, ServiceMessageContext.GlobalContext))
                {
                    dataset = (PublishedDataSetDataType)decoder.ReadEncodeable(null, typeof(PublishedDataSetDataType));
                    decoder.Close();
                }
            }

            m_datasets[dataset.Name] = dataset;

            return dataset;
        }

        private void CretePublishedDataItems(DataSetWriterCache cache, PublishedDataItemsDataType datasource)
        {
            List<MonitoredItem> monitoredItems = new List<MonitoredItem>();

            foreach (var ii in datasource.PublishedData)
            {
                if (NodeId.IsNull(ii.PublishedVariable))
                {
                    monitoredItems.Add(null);
                    continue;
                }

                var monitoredItem = new MonitoredItem()
                {
                    StartNodeId = ii.PublishedVariable,
                    AttributeId = (ii.AttributeId == 0) ? Attributes.Value : ii.AttributeId,
                    MonitoringMode = MonitoringMode.Reporting,
                    SamplingInterval = (int)((ii.SamplingIntervalHint < 0) ? cache.WriterGroup.PublishingInterval : ii.SamplingIntervalHint),
                    DiscardOldest = true,
                    QueueSize = 0,
                    Handle = ii
                };

                if (ii.DeadbandType != (uint)DeadbandType.None)
                {
                    monitoredItem.Filter = new DataChangeFilter
                    {
                        DeadbandType = ii.DeadbandType,
                        DeadbandValue = ii.DeadbandValue
                    };
                }

                monitoredItems.Add(monitoredItem);
            }

            cache.MonitoredItems = monitoredItems;

            foreach (var ii in monitoredItems)
            {
                if (ii != null)
                {
                    m_subscription.AddItem(ii);
                }
            }

            m_subscription.ApplyChanges();
        }

        private async Task LoadConnection(string configFilePath)
        {
            PubSubConnectionDataType connection = null;

            using (var istrm = new System.IO.StreamReader(configFilePath))
            {
                var json = await istrm.ReadToEndAsync();

                using (JsonDecoder decoder = new JsonDecoder(json, ServiceMessageContext.GlobalContext))
                {
                    connection = (PubSubConnectionDataType)decoder.ReadEncodeable(null, typeof(PubSubConnectionDataType));
                    decoder.Close();
                }
            }

            m_connections[connection.Name] = connection;

            foreach (var group in connection.WriterGroups)
            {
                m_writerGroups[group.Name] = group;

                foreach (var writer in group.DataSetWriters)
                {
                    m_datasetWriters[writer.Name] = writer;

                    if (!writer.Enabled || !group.Enabled || !connection.Enabled)
                    {
                        continue;
                    }

                    PublishedDataSetDataType dataset = null;

                    if (!m_datasets.TryGetValue(writer.DataSetName, out dataset))
                    {
                        dataset = await LoadDataSet(writer.DataSetName);
                    }

                    PublishedDataItemsDataType datasource = ExtensionObject.ToEncodeable(dataset.DataSetSource) as PublishedDataItemsDataType;

                    if (datasource != null)
                    {
                        DataSetWriterCache cache = new DataSetWriterCache()
                        {
                            Connection = connection,
                            WriterGroup = group,
                            DataSetWriter = writer,
                            DataSet = dataset
                        };

                        CretePublishedDataItems(cache, datasource);
                        writer.Handle = cache;
                    }
                }

                if (group.Enabled && connection.Enabled)
                {
                    group.Handle = new GroupState()
                    {
                        Connection = connection,
                        Timer = new Timer(OnWriterGroupPublish, group, 1000, (int)group.PublishingInterval)
                    };
                }
            }
        }
        private class GroupState
        {
            public Timer Timer;
            public PubSubConnectionDataType Connection;
        }

        private void OnWriterGroupPublish(object state)
        {
            WriterGroupDataType group = (WriterGroupDataType)state;
            PubSubConnectionDataType connection = ((GroupState)group.Handle).Connection;

            var messageSettings = (JsonWriterGroupMessageDataType)ExtensionObject.ToEncodeable(group.MessageSettings);
            var transportSettings = (BrokerWriterGroupTransportDataType)ExtensionObject.ToEncodeable(group.TransportSettings);

            JsonNetworkMessage networkMessage = new JsonNetworkMessage();

            networkMessage.MessageId = Guid.NewGuid().ToString();
            networkMessage.MessageType = "ua-data";
            networkMessage.PublisherId = connection.PublisherId.ToString();
            networkMessage.MessageContentMask = messageSettings.NetworkMessageContentMask;
            networkMessage.Messages = new List<JsonDataSetMessage>();

            foreach (var writer in group.DataSetWriters)
            {
                if (!writer.Enabled)
                {
                    continue;
                }

                DataSetWriterCache cache = (DataSetWriterCache)writer.Handle;

                if (networkMessage.DataSetClassId == null)
                {
                    networkMessage.DataSetClassId = cache.DataSet.DataSetMetaData.DataSetClassId.ToString();
                }

                JsonDataSetMessage message = new JsonDataSetMessage();

                message.DataSetWriterId = writer.DataSetWriterId.ToString();
                message.MetaDataVersion = cache.DataSet.DataSetMetaData.ConfigurationVersion;
                message.FieldContentMask = writer.DataSetFieldContentMask;
                message.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref m_sequenceNumber) % UInt16.MaxValue);
                message.Status = StatusCodes.Good;
                message.Timestamp = DateTime.UtcNow;
                message.Payload = new Dictionary<string, DataValue>();

                networkMessage.Messages.Add(message);

                var datasource = (PublishedDataItemsDataType)ExtensionObject.ToEncodeable(cache.DataSet.DataSetSource);

                for (int ii = 0; ii < datasource.PublishedData.Count; ii++)
                {
                    var field = cache.DataSet.DataSetMetaData.Fields[ii];
                    var source = datasource.PublishedData[ii];
                    var monitoredItem = cache.MonitoredItems[ii];

                    if (monitoredItem == null)
                    {
                        var substituteValue = source.SubstituteValue;

                        if (substituteValue != Variant.Null)
                        {
                            var qname = substituteValue.Value as QualifiedName;

                            if (cache.DataSet.ExtensionFields != null && qname != null)
                            {
                                foreach (var extensionField in cache.DataSet.ExtensionFields)
                                {
                                    if (extensionField.Key == qname)
                                    {
                                        substituteValue = extensionField.Value;
                                        break;
                                    }
                                }
                            }

                            message.Payload.Add(field.Name, new DataValue(substituteValue, StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow));
                        }

                        continue;
                    }

                    var notification = monitoredItem.LastValue as MonitoredItemNotification;

                    if (notification == null)
                    {
                        continue;
                    }

                    if (ServiceResult.IsBad(monitoredItem.Status.Error) || ServiceResult.IsBad(notification.Value.StatusCode))
                    {
                        if (source.SubstituteValue != Variant.Null)
                        {
                            message.Payload.Add(field.Name, new DataValue(source.SubstituteValue, StatusCodes.UncertainSubstituteValue, DateTime.UtcNow, DateTime.UtcNow));
                            continue;
                        }
                    }

                    if (ServiceResult.IsBad(monitoredItem.Status.Error))
                    {
                        message.Payload.Add(field.Name, new DataValue(monitoredItem.Status.Error.Code, DateTime.UtcNow));
                        continue;
                    }

                    if (notification != null)
                    {
                        message.Payload.Add(field.Name, notification.Value);
                    }
                }

                if ((networkMessage.MessageContentMask &  JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    PublishMessage(transportSettings.QueueName, networkMessage);

                    networkMessage = new JsonNetworkMessage();

                    networkMessage.MessageId = Guid.NewGuid().ToString();
                    networkMessage.MessageType = "ua-data";
                    networkMessage.PublisherId = connection.PublisherId.ToString();
                    networkMessage.MessageContentMask = messageSettings.NetworkMessageContentMask;
                    networkMessage.Messages = new List<JsonDataSetMessage>();
                }
            }

            if ((networkMessage.MessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                PublishMessage(transportSettings.QueueName, networkMessage);
            }
        }

        private bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        X509Certificate ClientCertificateSelectionCallback(object sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers)
        {
            return m_configuration.SecurityConfiguration.ApplicationCertificate.Certificate;
        }

        void MqttStackTrace(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        const string iotHubId = "opcf-prototype-iothub.azure-devices.net";
        const string deviceId = "mqtt-prototype-redopal-sparhawksoftware-com";

        private async void StartMqttPublishing()
        { 
            X509Certificate2 issuer = await FindIssuer(m_configuration, m_configuration.SecurityConfiguration.ApplicationCertificate.Certificate.Issuer);

            m_mqttClient = new MqttClient(
                iotHubId, 
                8883, 
                true, 
                issuer,
                m_configuration.SecurityConfiguration.ApplicationCertificate.Certificate,
                MqttSslProtocols.TLSv1_2);

            const string userName = iotHubId + "/" + deviceId;
            const string password = "";
            
            int result = m_mqttClient.Connect(deviceId, userName, password);

            m_mqttClient.MqttMsgPublished += Client_MqttMsgPublished;
            m_mqttClient.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
            m_mqttClient.MqttMsgSubscribed += Client_MqttMsgSubscribed;
            m_mqttClient.MqttMsgUnsubscribed += Client_MqttMsgUnsubscribed;
        }

        private void PublishMessage(string queueName, JsonNetworkMessage networkMessage)
        {
            lock (m_messages)
            {
                var ostrm = new MemoryStream();

                using (var stream = new StreamWriter(ostrm))
                {
                    networkMessage.Encode(m_session.MessageContext, false, stream);
                }

                var array = ostrm.ToArray();
                m_mqttClient.Publish("devices/" + deviceId + "/messages/events/", array, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
            }
        }


        private void StopMqttPublishing()
        {
            m_mqttClient.Disconnect();
        }

        private void Client_MqttMsgUnsubscribed(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgUnsubscribedEventArgs e)
        {
            Log("MQTT: Unsubscribe: {0}", e.MessageId);
        }

        private void Client_MqttMsgSubscribed(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgSubscribedEventArgs e)
        {
            Log("MQTT: Subscribe: {0}", e.MessageId);
        }

        private void Client_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            Log("MQTT: Publish Received: {0} {1}", e.Topic, e.DupFlag);
        }

        private void Client_MqttMsgPublished(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishedEventArgs e)
        {
            Log("MQTT: Published: {0} {1}", e.MessageId, e.IsPublished);
        }

        public async Task Start(ApplicationStartSettings settings)
        {
            Log("1 - Create an Application Configuration.");
            m_configuration = await CreateApplicationConfiguration(settings);

            m_useSecurity = false;

            Log("2 - Discover endpoints of {0}.", settings.EndpointUrl);
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(settings.EndpointUrl, m_useSecurity, settings.Timeout);
            Log("    Selected endpoint uses: {0}", selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Log("3 - Create a session with OPC UA server.");
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            m_session = await Session.Create(m_configuration, endpoint, false, "OPC UA Sample Publisher", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            Log("4 - Start MQTT publishing.");
            StartMqttPublishing();

            Log("5 - Create subscription.");
            await CreateSubscription();

            Log("6 - Load connection configuration.");
            await LoadConnection(settings.ConfigFile);
        }

        public Task Stop()
        {
            if (m_session != null)
            {
                m_session.Close(5000);
                m_session = null;
            }

            return Task.CompletedTask;
        }
    }
}