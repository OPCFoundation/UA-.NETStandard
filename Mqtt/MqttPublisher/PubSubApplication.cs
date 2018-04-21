using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Opc.Ua;
using Opc.Ua.Client;

namespace MqttSamplePublisher
{
    public class ApplicationStartSettings
    {
        public string EndpointUrl = String.Empty;
        public string ConfigFile = String.Empty;
        public string BrokerFile = String.Empty;
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
        private BrokerSettings m_broker;
        private ApplicationConfiguration m_configuration;
        private Session m_session;
        private Subscription m_subscription;
        private int m_sequenceNumber = 0;
        private Queue<JsonNetworkMessage> m_messages;
        private MqttClientFactory m_mqttFactory;
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
            m_mqttFactory = new MqttClientFactory();
            m_mqttFactory.LogMessage += MqttFactory_LogMessage;
        }

        private void MqttFactory_LogMessage(object sender, LogMessageEventArgs e)
        {
            LogMessage(this, e);
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

        private async Task<ApplicationConfiguration> CreateApplicationConfiguration(ApplicationStartSettings settings)
        {
            CertificateIdentifier applicationCertificate = new CertificateIdentifier
            {
                StoreType = "Directory",
                StorePath = "../../../../../pki/own",
                SubjectName = "CN=" + "MQTT Sample Publisher" + ",DC=" + Dns.GetHostName()
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
                        StorePath = "../../../../../pki/trusted"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "../../../../../pki/issuers"
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "../../../../../pki/rejected"
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
                await m_mqttFactory.CreateCertificate(config, applicationCertificate.SubjectName);
            }

            haveAppCertificate = config.SecurityConfiguration.ApplicationCertificate.Certificate != null;

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
                Log("WARN: missing application certificate, using unsecure connection.");
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

        private async void StartMqttPublishing()
        {
            m_mqttClient = await m_mqttFactory.Create(m_configuration, m_broker);

            m_mqttClient.MqttMsgPublished += Client_MqttMsgPublished;
            m_mqttClient.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
            m_mqttClient.MqttMsgSubscribed += Client_MqttMsgSubscribed;
            m_mqttClient.MqttMsgUnsubscribed += Client_MqttMsgUnsubscribed;
        }

        private void PublishMessage(string queueName, JsonNetworkMessage networkMessage)
        {
            if (m_mqttClient != null)
            {
                lock (m_messages)
                {
                    var ostrm = new MemoryStream();

                    using (var stream = new StreamWriter(ostrm))
                    {
                        networkMessage.Encode(m_session.MessageContext, false, stream);
                    }

                    var data = ostrm.ToArray();
                    m_mqttClient.Publish(m_broker.Topic, data, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
                }
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

        private async Task<BrokerSettings> LoadBrokerSettings(string filePath)
        {
            BrokerSettings settings = null;

            using (var istrm = new System.IO.StreamReader(filePath))
            {
                settings = await BrokerSettings.Decode(ServiceMessageContext.GlobalContext, istrm);
            }

            return settings;
        }

        public async Task Start(ApplicationStartSettings settings)
        {
            Log("1 - Load Broker Settings.");
            m_broker = await LoadBrokerSettings(settings.BrokerFile);

            Log("2 - Create an Application Configuration.");
            m_configuration = await CreateApplicationConfiguration(settings);

            Log("3 - Discover endpoints of {0}.", settings.EndpointUrl);
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(settings.EndpointUrl, false, settings.Timeout);
            Log("Selected endpoint uses: {0}", selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            Log("4 - Create a session with OPC UA server.");
            var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            m_session = await Session.Create(m_configuration, endpoint, false, "OPC UA Sample Publisher", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            Log("5 - Start MQTT publishing.");
            StartMqttPublishing();

            Log("6 - Create subscription.");
            await CreateSubscription();

            Log("7 - Load connection configuration.");
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