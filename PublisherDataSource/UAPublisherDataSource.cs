using DataSource;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Client.Controls;
using System.Security.Cryptography.X509Certificates;

namespace PublisherDataSource
{
    #region InterfaceRegion
    public interface IUAPublisherDataSource
    { 
        void AddWriterGroup(WriterGroupState writerGroupState,ref List<MonitoredItem> lstMonitoredItems);
        void RemoveGroup(BaseInstanceState GroupState);
        bool Initialize(PubSubConnectionState pubSubConnectionState, IDataSource dataSource);
        void StopPublishing();
        void AddDataSetWriter(DataSetWriterState dataSetWriterState);
        void RemoveDataSetWriter(DataSetWriterState dataSetWriterState);

    }

    #endregion
    public class UAPublisherDataSource : IUAPublisherDataSource
    {
        protected bool IsRunning = true;
        protected PubSubConnectionState m_PubSubConnectionState;
        private bool UseUADPEncoder = false;
        private int SequenceNumber = 0;
        private bool IsBroker = false;
        IDataSource m_TransportdataSource { get; set; }
        protected List<DataSetWriterState> LstDataSetWriterState { get; set; }
        Dictionary<NodeId, Thread> DicWriterGroupPublisher = new Dictionary<NodeId, Thread>();
        List<MonitoredItem> LstMonitoredItems = new List<MonitoredItem>();
        X509Certificate2 m_servercertificate;

        #region Public Methods
        public UAPublisherDataSource(X509Certificate2 servercertificate)
        {
            m_servercertificate = servercertificate;
            LstDataSetWriterState = new List<DataSetWriterState>();
        }
       

        public virtual bool Initialize(PubSubConnectionState pubSubConnectionState, IDataSource dataSource)
        {
            m_PubSubConnectionState = pubSubConnectionState;
            string transportProfileUri = Convert.ToString(pubSubConnectionState.TransportProfileUri.Value).ToLower();
            if (transportProfileUri.Contains("uadp"))
            {
                UseUADPEncoder = true;
            }
            m_TransportdataSource = dataSource;

            return true;
        }
        public void AddWriterGroup(WriterGroupState writerGroupState,ref List<MonitoredItem> lstMonitoredItems)
        {
            LstMonitoredItems = lstMonitoredItems;
            Thread UDPPublisherThread = new Thread(() => this.Run(writerGroupState, writerGroupState.PublishingInterval.Value));
            UDPPublisherThread.Start();
            DicWriterGroupPublisher[writerGroupState.NodeId] = UDPPublisherThread;

        }
        public void RemoveGroup(BaseInstanceState groupState)
        {
            if (DicWriterGroupPublisher.ContainsKey(groupState.NodeId))
            {
                Thread UDPPublisherThread = DicWriterGroupPublisher[groupState.NodeId];
                try
                {
                    UDPPublisherThread.Abort();
                }
                catch (Exception ec)
                { 
                }
            }
        }
        public void AddDataSetWriter(DataSetWriterState dataSetWriterState)
        {
            LstDataSetWriterState.Add(dataSetWriterState);
           
        }
        public void RemoveDataSetWriter(DataSetWriterState dataSetWriterState)
        {
            DataSetWriterState writerState = LstDataSetWriterState.Where(i => i.NodeId == dataSetWriterState.NodeId).FirstOrDefault();
            LstDataSetWriterState.Remove(writerState);
        }
        public void StopPublishing()
        {
            IsRunning = false;
        }
        public void Run(WriterGroupState groupState, double publishingInterval)
        {
            while (IsRunning)
            {
                if (m_PubSubConnectionState.Status.State.Value == PubSubState.Operational)
                {
                     
                     if (groupState.Status.State.Value == PubSubState.Operational)
                     {
                        if (UseUADPEncoder)
                        {
                            PublishUADPData(groupState);
                        }
                        else
                        {
                            foreach (DataSetWriterState writerState in LstDataSetWriterState.ToList())
                            {

                                if (writerState.Status.State.Value == PubSubState.Operational)
                                {
                                    if (writerState.Parent.NodeId == groupState.NodeId)
                                    {
                                        PublishJsonData(writerState, groupState);
                                    }
                                }

                            }
                        }
                    }

                }
                 
                if (publishingInterval<=0)
                {
                    publishingInterval = 100;

                }
                Thread.Sleep(Convert.ToInt32(publishingInterval));
            }
        }
        #endregion

        #region Private Methods
        private void PublishUADPData(WriterGroupState groupState)
        {
            List<DataSetWriterState> activeDataSetWriters = new List<DataSetWriterState>();
            foreach (DataSetWriterState writerState in LstDataSetWriterState.ToList())
            {

                if (writerState.Status.State.Value == PubSubState.Operational)
                {
                    if (writerState.Parent.NodeId == groupState.NodeId)
                    {
                        activeDataSetWriters.Add(writerState);
                    }
                }

            }
            if (activeDataSetWriters.Count <= 0)
            {
                return;
            }
            PublishUADPData(groupState, activeDataSetWriters);
          
        }
       private void PublishUADPData(WriterGroupState groupState, List<DataSetWriterState> activeDataSetWriters)
        {
            
            UadpNetworkMessage uadpNetworkMessage = new UadpNetworkMessage(new ServiceMessageContext());
            uadpNetworkMessage.NetworkContentMask = (groupState.MessageSettings as UadpWriterGroupMessageState).NetworkMessageContentMask.Value;
            uadpNetworkMessage.PublisherId = m_PubSubConnectionState.PublisherId.Value;

            uadpNetworkMessage.DataSetClassId = new Guid(((activeDataSetWriters[0].Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).DataSetClassId.GuidString);
            uadpNetworkMessage.SecurityMode = groupState.SecurityMode.Value;
            uadpNetworkMessage.WriterGroupId = groupState.WriterGroupId.Value;
            uadpNetworkMessage.NetworkMessageNumber = (activeDataSetWriters[0].MessageSettings as UadpDataSetWriterMessageState).NetworkMessageNumber.Value;
            uadpNetworkMessage.GroupVersion = (groupState.MessageSettings as UadpWriterGroupMessageState).GroupVersion.Value;
            uadpNetworkMessage.NetworkMessageSequenceNumber = (ushort)(Utils.IncrementIdentifier(ref SequenceNumber) % UInt16.MaxValue);
            uadpNetworkMessage.MessageCount =(byte) activeDataSetWriters.Count;
            foreach (DataSetWriterState writerstate in activeDataSetWriters)
            {
                uadpNetworkMessage.LstDataSetWriterId.Add(writerstate.DataSetWriterId.Value);
            }
            uadpNetworkMessage.SecurityGroupId = Convert.ToUInt32(groupState.SecurityGroupId.Value);

            //Check the contentMask
            if ((uadpNetworkMessage.NetworkContentMask & (UInt32)UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                foreach (DataSetWriterState writerState in activeDataSetWriters)
                {
                    uadpNetworkMessage.FieldMetaDataCollection = ((writerState.Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).Fields;
                }
            }
                

            uadpNetworkMessage.Encode();
            long DataSetMessageSequenceNumber = 0;
            List<MemoryStream> LstmemoryStream = new List<MemoryStream>();
            List<UInt16> DataSetMessageSizes = new List<UInt16>();
            foreach (DataSetWriterState writerState in activeDataSetWriters)
            {
                if ((writerState.TransportSettings is BrokerDataSetWriterTransportState))
                {
                    IsBroker = true;
                }
                else
                {
                    IsBroker = false;
                }
                MemoryStream memoryStream = new MemoryStream();
                UadpDataSetMessage uadpDataSetMessage = new UadpDataSetMessage(new ServiceMessageContext());
                uadpDataSetMessage.MessageContentMask = (writerState.MessageSettings as UadpDataSetWriterMessageState).DataSetMessageContentMask.Value;
                uadpDataSetMessage.FieldContentMask = writerState.DataSetFieldContentMask.Value;
                uadpDataSetMessage.DataSetMessageSequenceNumber = (ushort)(Utils.IncrementIdentifier(ref DataSetMessageSequenceNumber) % UInt16.MaxValue);
                int count = (writerState.Handle as PublishedDataItemsState).PublishedData.Value.Count();

                for (int ii = 0; ii < count; ii++)
                {
                    var field = ((writerState.Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).Fields[ii];
                    var source = ((writerState.Handle as PublishedDataItemsState).PublishedData.Value[ii]);
                    MonitoredItem monitoredItem = LstMonitoredItems.Where(i => i.ResolvedNodeId == source.PublishedVariable).FirstOrDefault();

                    if (monitoredItem == null)
                    {
                        var substituteValue = source.SubstituteValue;

                        if (substituteValue != Variant.Null)
                        {
                            var qname = substituteValue.Value as QualifiedName;

                            if (((writerState.Handle as PublishedDataItemsState).ExtensionFields) != null && qname != null)
                            {
                                //foreach (var extensionField in (writerState.Handle as PublishedDataItemsState).ExtensionFields)
                                //{
                                //    if (extensionField.Key == qname)
                                //    {
                                //        substituteValue = extensionField.Value;
                                //        break;
                                //    }
                                //}
                            }

                            uadpDataSetMessage.FieldDatas.Add(new DataValue(substituteValue, StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow));
                        }

                        continue;
                    }
                    var notification = monitoredItem.LastValue as MonitoredItemNotification;

                    if (ServiceResult.IsBad(monitoredItem.Status.Error) || ServiceResult.IsBad(notification.Value.StatusCode))
                    {
                        if (source.SubstituteValue != Variant.Null)
                        {
                            uadpDataSetMessage.FieldDatas.Add(new DataValue(source.SubstituteValue, StatusCodes.UncertainSubstituteValue, DateTime.UtcNow, DateTime.UtcNow));
                            continue;
                        }
                    }

                    if (ServiceResult.IsBad(monitoredItem.Status.Error))
                    {
                        uadpDataSetMessage.FieldDatas.Add(new DataValue(monitoredItem.Status.Error.Code, DateTime.UtcNow));
                        continue;
                    }

                    if (notification != null)
                    {

                        uadpDataSetMessage.FieldDatas.Add(notification.Value);
                    } 
                }
                uadpDataSetMessage.Encode();
                DataSetMessageSizes.Add((UInt16)(uadpDataSetMessage.BaseStream as MemoryStream).Length);
                
                using (var stream = new BinaryWriter(memoryStream))
                {
                    stream.Write((uadpDataSetMessage.BaseStream as MemoryStream).ToArray());
                }
                LstmemoryStream.Add(memoryStream);
            }
            if (uadpNetworkMessage.MessageCount > 1 && uadpNetworkMessage.IsPayloadHeaderAvailable())
            {
                foreach (UInt16 size in DataSetMessageSizes)
                {
                    uadpNetworkMessage.WriteUInt16("DataSetMessageSize", size);
                }
            }
            using (var stream = new BinaryWriter(uadpNetworkMessage.BaseStream))
            {
                foreach (var memoryStream in LstmemoryStream)
                {
                    //need to encrypt here.
                   /* if (uadpNetworkMessage.SecurityMode == MessageSecurityMode.Sign)
                    {
                      SignatureData signatureData=  SecurityPolicies.Sign(m_servercertificate, SecurityPolicies.Basic256, memoryStream.ToArray());
                        stream.Write(signatureData.Signature);
                    }
                    else if (uadpNetworkMessage.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        SignatureData signatureData = SecurityPolicies.Sign(m_servercertificate, SecurityPolicies.Basic256, memoryStream.ToArray());
                        if(signatureData.Signature!=null && signatureData.Signature.Count()>0)
                        {
                          EncryptedData encryptedData=  SecurityPolicies.Encrypt(m_servercertificate, SecurityPolicies.Basic256, signatureData.Signature);
                            stream.Write(encryptedData.Data);
                        }
                        
                    }*/
                    stream.Write(memoryStream.ToArray());
                }
               
            }
            if(IsBroker)
            { 
 			    PublishUADPMessage((groupState.TransportSettings as BrokerWriterGroupTransportState).QueueName.Value,uadpNetworkMessage);
            }
            else
            {
                PublishUADPMessage("", uadpNetworkMessage);
            }
        }
        private void PublishJsonData(DataSetWriterState writerState, WriterGroupState groupState)
        {
            Opc.Ua.JsonNetworkMessage networkMessage = new Opc.Ua.JsonNetworkMessage();
            networkMessage.MessageId = Guid.NewGuid().ToString();
            networkMessage.MessageType = "ua-data";
            networkMessage.PublisherId = m_PubSubConnectionState.PublisherId.Value.ToString();
            networkMessage.MessageContentMask = (groupState.MessageSettings as JsonWriterGroupMessageState).NetworkMessageContentMask.Value;
            networkMessage.Messages = new List<Opc.Ua.JsonDataSetMessage>();

            if (networkMessage.DataSetClassId == null)
            {
                networkMessage.DataSetClassId = ((writerState.Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).DataSetClassId.GuidString;
            }
            Opc.Ua.JsonDataSetMessage message = new Opc.Ua.JsonDataSetMessage();

            message.DataSetWriterId = writerState.DataSetWriterId.Value.ToString();
            message.MetaDataVersion = ((writerState.Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).ConfigurationVersion;
            message.FieldContentMask = writerState.DataSetFieldContentMask.Value;
            message.SequenceNumber = (ushort)(Utils.IncrementIdentifier(ref SequenceNumber) % UInt16.MaxValue);
            message.Status = StatusCodes.Good;
            message.Timestamp = DateTime.UtcNow;
            message.Payload = new Dictionary<string, DataValue>();

            networkMessage.Messages.Add(message);

            int count = (writerState.Handle as PublishedDataItemsState).PublishedData.Value.Count();

            for (int ii = 0; ii < count; ii++)
            {
                var field = ((writerState.Handle as PublishedDataItemsState).DataSetMetaData.Value as DataSetMetaDataType).Fields[ii];
                var source = ((writerState.Handle as PublishedDataItemsState).PublishedData.Value[ii]);
                MonitoredItem monitoredItem = LstMonitoredItems.Where(i => i.ResolvedNodeId == source.PublishedVariable).FirstOrDefault();

                if (monitoredItem == null)
                {
                    var substituteValue = source.SubstituteValue;

                    if (substituteValue != Variant.Null)
                    {
                        var qname = substituteValue.Value as QualifiedName;

                        if (((writerState.Handle as PublishedDataItemsState).ExtensionFields) != null && qname != null)
                        {
                            //foreach (var extensionField in (writerState.Handle as PublishedDataItemsState).ExtensionFields)
                            //{
                            //    if (extensionField.Key == qname)
                            //    {
                            //        substituteValue = extensionField.Value;
                            //        break;
                            //    }
                            //}
                        }

                        message.Payload.Add(field.Name, new DataValue(substituteValue, StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow));
                    }

                    continue;
                }
                var notification = monitoredItem.LastValue as MonitoredItemNotification;

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



                if ((networkMessage.MessageContentMask & (uint)JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {

                    PublishJsonMessage((writerState.TransportSettings as BrokerDataSetWriterTransportState).QueueName.Value, networkMessage);

                    networkMessage = new Opc.Ua.JsonNetworkMessage();

                    networkMessage.MessageId = Guid.NewGuid().ToString();
                    networkMessage.MessageType = "ua-data";
                    networkMessage.PublisherId = m_PubSubConnectionState.PublisherId.Value.ToString();
                    networkMessage.MessageContentMask = (groupState.MessageSettings as JsonWriterGroupMessageState).NetworkMessageContentMask.Value;
                    networkMessage.Messages = new List<Opc.Ua.JsonDataSetMessage>();

                    message.Payload = new Dictionary<string, DataValue>();
                    networkMessage.Messages.Add(message);

                }

            }

            if ((networkMessage.MessageContentMask & (uint)JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                PublishJsonMessage((groupState.TransportSettings as BrokerWriterGroupTransportState).QueueName.Value, networkMessage);
            }
        }
        private void PublishUADPMessage(string topic, Opc.Ua.UadpNetworkMessage networkMessage)
        {
            var ostrm = networkMessage.BaseStream as MemoryStream;
             
            Dictionary<string, object> Settings = new Dictionary<string, object>();
            Settings["topic"] = topic;

            m_TransportdataSource.SendData(ostrm.ToArray(), Settings);
            Settings = null;
        }
        private void PublishJsonMessage(string topic, Opc.Ua.JsonNetworkMessage networkMessage)
        {
            var ostrm = new MemoryStream();
            using (var stream = new StreamWriter(ostrm))
            {
                networkMessage.Encode(new ServiceMessageContext(), false, stream);
            }
            var data = ostrm.ToArray();
            Dictionary<string, object> Settings= new Dictionary<string, object>();
            Settings["topic"] = topic;
            m_TransportdataSource.SendData(data, Settings);
            Settings = null;
        }

        #endregion
    }

}

