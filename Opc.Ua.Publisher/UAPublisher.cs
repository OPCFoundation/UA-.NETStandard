using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using DataSource;
using Opc.Ua.Client;
using PublisherDataSource;

namespace Opc.Ua.Publisher
{
    public class UAPublisher: IUAPublisher
    {
        X509Certificate2 m_servercertificate;
        public UAPublisher(X509Certificate2 servercertificate)
        {
            m_servercertificate = servercertificate;
        }
        Dictionary<NodeId, IUAPublisherDataSource> DicUAPublisherDataSource = new Dictionary<NodeId, IUAPublisherDataSource>();

        public void AddConnection(PubSubConnectionState pubSubConnectionState, IDataSource dataSource)
        {

            IUAPublisherDataSource m_UAPublisherDataSource = null ;
            
            m_UAPublisherDataSource = new UAPublisherDataSource(m_servercertificate);
            DicUAPublisherDataSource[pubSubConnectionState.NodeId] = m_UAPublisherDataSource;
            m_UAPublisherDataSource.Initialize(pubSubConnectionState, dataSource);
        }
        public  void AddWriterGroup(WriterGroupState writerGroupState,ref List<MonitoredItem> lstMonitoredItems)
        {
            IUAPublisherDataSource m_UAPublisherDataSource=  DicUAPublisherDataSource[writerGroupState.Parent.NodeId];
            m_UAPublisherDataSource.AddWriterGroup(writerGroupState,ref lstMonitoredItems);
        }
        public void RemoveGroup(BaseInstanceState  GroupState)
        {
            IUAPublisherDataSource m_UAPublisherDataSource = DicUAPublisherDataSource[GroupState.Parent.NodeId];
            m_UAPublisherDataSource.RemoveGroup(GroupState);
        }
        public  void RemoveConnection(PubSubConnectionState pubSubConnectionState)
        {
            IUAPublisherDataSource m_UAPublisherDataSource = DicUAPublisherDataSource[pubSubConnectionState.NodeId];
            m_UAPublisherDataSource.StopPublishing();
            DicUAPublisherDataSource.Remove(pubSubConnectionState.NodeId);
        }

        public void AddDataSetWriter(DataSetWriterState dataSetWriterState)
        {
            IUAPublisherDataSource m_UAPublisherDataSource = DicUAPublisherDataSource[(dataSetWriterState.Parent as BaseInstanceState).Parent.NodeId];
            m_UAPublisherDataSource.AddDataSetWriter(dataSetWriterState);
        }

        public void RemoveDataSetWriter(DataSetWriterState dataSetWriterState)
        {
            IUAPublisherDataSource m_UAPublisherDataSource = DicUAPublisherDataSource[(dataSetWriterState.Parent as BaseInstanceState).Parent.NodeId];
            m_UAPublisherDataSource.RemoveDataSetWriter(dataSetWriterState);
        }
    }
}
