using DataSource;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SubscriberDataSource
{
    public interface IUASubscriberDataSource
    {
         
        bool Initialize(PubSubConnectionState pubSubConnectionState, IDataSource dataSource);
        void StopSubscribing();
        void AddDataSetReader(DataSetReaderState dataSetReaderState, Opc.Ua.Core.SubscriberDelegate subscriberDelegate);
        void RemoveDataSetReader(DataSetReaderState dataSetReaderState);
        void CreateTargetVariables(NodeId connectionStateNodeId,NodeId readerStateNodeId, FieldTargetDataType[] fieldTargetDataTypes);
        void RemoveFieldTargetDataType(NodeId readerStateNodeId);

    }

    public class UASubscriberDataSource : IUASubscriberDataSource
    {
        Dictionary<NodeId, IUASubscriberDecoder> Dic_DataSetReader = new Dictionary<NodeId, IUASubscriberDecoder>();
         
        protected PubSubConnectionState m_PubSubConnectionState;
        private bool UseUADPEncoder = false;
        
        protected bool IsRunning = true;
        IDataSource m_TransportdataSource { get; set; }
        X509Certificate2 m_servercertificate;

        #region PrivateMethods

        private void DataReceivedMessage(object data)
        {
            if (UseUADPEncoder)
            {
                UadpNetworkMessageDecoder uadpNetworkMessage = new UadpNetworkMessageDecoder(data as byte[]);
                uadpNetworkMessage.Decode();
                foreach (UInt16 dataSetWriterId in uadpNetworkMessage.DataSetWriterIds)
                {
                    foreach (IUASubscriberDecoder _UASubscriberDataSetReader in Dic_DataSetReader.Values.ToList())
                    {
                        if (_UASubscriberDataSetReader.DataSetWriterId == dataSetWriterId)
                        {
                            UadpDataSetMessageDecoder uadpDataSetMessageDecoder = new UadpDataSetMessageDecoder(uadpNetworkMessage.BaseStream, _UASubscriberDataSetReader.DataSetMetaDataType);
                            uadpDataSetMessageDecoder.Decode();
                            uadpNetworkMessage.DicDataSetWiter_Message[dataSetWriterId] = uadpDataSetMessageDecoder;

                            break;
                        }
                    }
                }
                Dictionary<string, object> DicNetworkMessage = new Dictionary<string, object>();
                DicNetworkMessage["NetworkMessage"] = uadpNetworkMessage;
                foreach (IUASubscriberDecoder _UASubscriberDataSetReader in Dic_DataSetReader.Values.ToList())
                {

                    Task t = new Task(() => _UASubscriberDataSetReader.UpdateTargetVariables(DicNetworkMessage));
                    t.Start();
                }
            }
            else
            {
                string ReceivedMessage = Encoding.UTF8.GetString(data as byte[]);
                var json = ReceivedMessage.ToString();
                JsonNetworkMessage networkMessage = new JsonNetworkMessage();
                Dictionary<string, object> Dic_NetworkMessage = networkMessage.Decode(json);
                foreach (IUASubscriberDecoder _UASubscriberDataSetReader in Dic_DataSetReader.Values.ToList())
                {

                    Task t = new Task(() => _UASubscriberDataSetReader.UpdateTargetVariables(Dic_NetworkMessage));
                    t.Start();
                }
            }
        }

        private void Data_DataReceived(object receivedData)
        {

            DataReceivedMessage(receivedData);
        }

        #endregion

        #region Public Methods

        public UASubscriberDataSource(X509Certificate2 servercertificate)
        {
            m_servercertificate = servercertificate;
        }
        public void AddDataSetReader(DataSetReaderState dataSetReaderState, Opc.Ua.Core.SubscriberDelegate subscriberDelegate)
        {
            if (UseUADPEncoder)
            {
                UASubscriberUADPDecoder uASubscriberUADPDecoder = new UASubscriberUADPDecoder(dataSetReaderState, subscriberDelegate);
                Dic_DataSetReader[dataSetReaderState.NodeId] = uASubscriberUADPDecoder;
                bool isSucess = true;
                if ((dataSetReaderState.TransportSettings is BrokerDataSetReaderTransportState))
                {
                    if (((dataSetReaderState.TransportSettings as BrokerDataSetReaderTransportState).QueueName.Value) != null)
                    {
                        isSucess= m_TransportdataSource.ReceiveData((dataSetReaderState.TransportSettings as BrokerDataSetReaderTransportState).QueueName.Value);
                    }
                }
                else
                {
                    isSucess= m_TransportdataSource.ReceiveData("");
                }
               if(m_PubSubConnectionState!=null)
                {
                    if(!isSucess)
                    {
                        m_PubSubConnectionState.Status.State.Value = PubSubState.Error;
                    }
                }
            }
            else
            {
                UASubscriberJsonDecoder uASubscriberDataSetReader = new UASubscriberJsonDecoder(dataSetReaderState, subscriberDelegate);
                Dic_DataSetReader[dataSetReaderState.NodeId] = uASubscriberDataSetReader;

                m_TransportdataSource.ReceiveData((dataSetReaderState.TransportSettings as BrokerDataSetReaderTransportState).QueueName.Value);
            }
        }

        public bool Initialize(PubSubConnectionState pubSubConnectionState, IDataSource dataSource)
        {
            m_PubSubConnectionState = pubSubConnectionState;
            string transportProfileUri = Convert.ToString(pubSubConnectionState.TransportProfileUri.Value).ToLower();
            if (transportProfileUri.Contains("uadp"))
            {
                UseUADPEncoder = true;
            }
            m_TransportdataSource = dataSource;
            dataSource.DataReceived += Data_DataReceived;
            return true;
        }
        public void CreateTargetVariables(NodeId connectionStateNodeId, NodeId readerStateNodeId, FieldTargetDataType[] fieldTargetDataTypes)
        {
            foreach (NodeId key in Dic_DataSetReader.Keys)
            {
                if (key == readerStateNodeId)
                {
                    IUASubscriberDecoder _UASubscriberDataSetReader = Dic_DataSetReader[key];
                    _UASubscriberDataSetReader.UpdateFieldTargetDataType(fieldTargetDataTypes);
                    break;
                }

            }
        }
        public void RemoveFieldTargetDataType(NodeId readerStateNodeId)
        {
            foreach (NodeId key in Dic_DataSetReader.Keys)
            {
                if (key == readerStateNodeId)
                {
                    IUASubscriberDecoder _UASubscriberDataSetReader = Dic_DataSetReader[key];
                    _UASubscriberDataSetReader.RemoveFieldTargetDataType();
                    break;
                }

            }
        }
        public void RemoveDataSetReader(DataSetReaderState dataSetReaderState)
        {
            Dic_DataSetReader.Remove(dataSetReaderState.NodeId);

        }

        public void StopSubscribing()
        {
            IsRunning = false;
        }

        #endregion


    }
}
