using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using DataSource;
using SubscriberDataSource;

namespace Opc.Ua.Subscriber
{
    public class UASubscriber : IUASubscriber
    {
        X509Certificate2 m_servercertificate;
        public UASubscriber(X509Certificate2 servercertificate)
        {
            m_servercertificate = servercertificate;
        }
        Dictionary<NodeId, IUASubscriberDataSource> DicUASubscriberDataSource = new Dictionary<NodeId, IUASubscriberDataSource>();
        public void AddConnection(PubSubConnectionState pubSubConnectionState, IDataSource dataSource)
        {
            IUASubscriberDataSource m_UASubscriberDataSource = null;
            m_UASubscriberDataSource = new UASubscriberDataSource(m_servercertificate);
            DicUASubscriberDataSource[pubSubConnectionState.NodeId] = m_UASubscriberDataSource;
            m_UASubscriberDataSource.Initialize(pubSubConnectionState, dataSource);
        }

        public void AddDataSetReader(DataSetReaderState dataSetReaderState,Opc.Ua.Core.SubscriberDelegate subscriberDelegate)
        {
            IUASubscriberDataSource m_UASubscriberDataSource = DicUASubscriberDataSource[(dataSetReaderState.Parent as BaseInstanceState).Parent.NodeId];
            m_UASubscriberDataSource.AddDataSetReader(dataSetReaderState, subscriberDelegate);
        }
         

        public void RemoveConnection(PubSubConnectionState pubSubConnectionState)
        {
            IUASubscriberDataSource m_UASubscriberDataSource = DicUASubscriberDataSource[pubSubConnectionState.NodeId];
            m_UASubscriberDataSource.StopSubscribing();
            DicUASubscriberDataSource.Remove(pubSubConnectionState.NodeId);
        }

        public void RemoveDataSetReader(DataSetReaderState dataSetReaderState)
        {
            IUASubscriberDataSource m_UAPublisherDataSource = DicUASubscriberDataSource[(dataSetReaderState.Parent as BaseInstanceState).Parent.NodeId];
            m_UAPublisherDataSource.RemoveDataSetReader(dataSetReaderState);
        }
        public void CreateTargetVariables(NodeId connectionStateNodeId,NodeId readerStateNodeId, FieldTargetDataType[] fieldTargetDataTypes)
        {
            IUASubscriberDataSource m_UAPublisherDataSource = DicUASubscriberDataSource[connectionStateNodeId];
            m_UAPublisherDataSource.CreateTargetVariables(connectionStateNodeId,readerStateNodeId, fieldTargetDataTypes);
        }
        public void RemoveFieldTargetDataType(NodeId readerStateNodeId)
        {
            IUASubscriberDataSource m_UAPublisherDataSource = DicUASubscriberDataSource[readerStateNodeId];
            m_UAPublisherDataSource.RemoveFieldTargetDataType(readerStateNodeId);
        }
    }
}
