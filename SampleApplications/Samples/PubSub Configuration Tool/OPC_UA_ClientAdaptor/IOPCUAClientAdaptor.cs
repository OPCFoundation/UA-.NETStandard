using Opc.Ua;
using Opc.Ua.Client;
using PubSubBase.Definitions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientAdaptor
{
  public  interface IOPCUAClientAdaptor
  { 
        ApplicationDescriptionCollection FindServers(string hostName);
        Session Connect(string endpointUrl, out string errorMessage, out TreeViewNode node);
        bool Disconnect();

        string AddUADPConnection(Connection connection, out NodeId connectionId);
        string AddAMQPConnection(Connection connection, out NodeId connectionId);

        string AddWriterGroup(DataSetWriterGroup _dataSetWriterGroup, out NodeId groupId);

        string AddReaderGroup(ReaderGroupDefinition _ReaderGroupDefinition, out NodeId groupId);

        string AddUADPDataSetWriter(NodeId DataSetWriterGroupNodeId, DataSetWriterDefinition _dataSetWriterDefinition,out NodeId writerNodeId,out int revisedKeyFrameCount);
        string AddAMQPDataSetWriter(NodeId DataSetWriterGroupNodeId, DataSetWriterDefinition _dataSetWriterDefinition, out NodeId writerNodeId, out int revisedKeyFrameCount);

        string RemoveDataSetWriter(DataSetWriterGroup _DataSetWriterGroup, NodeId writerNodeId);
        string RemoveDataSetReader(ReaderGroupDefinition _ReaderGroupDefinition, NodeId readerNodeId);

        string RemoveConnection(NodeId connectionId);

       string AddNewSecurityGroup(string name, out SecurityGroup SecurityGroup);

       string RemoveSecurityGroup(NodeId SecurityGroupId);

       string RemoveGroup(Connection Connection,NodeId GroupId);
    
       ObservableCollection<SecurityGroup> GetSecurityGroups();
         

       string GetSecurityKeys(string SecurityGroupId, uint FeatureKeyCount, out SecurityKeys securityKeys);
       string SetSecurityKeys(SecurityKeys securityKeys);
       Subscription CreateSubscription(string subscriptionName, int subscriptionInterval);

    //   string AddWriterGroup(string groupName,int publishingInterval,int publishingOffset,int keppAliveTime,int priority,int securityGroupId,int writerGroupId,int maxNetworkMessageSize,List<string> messageSecurityMode,List<bool> networkMessageContentmask,out NodeId groupId);  
       string EnablePubSubState(MonitorNode _MonitorNode);
       string DisablePubSubState(MonitorNode _MonitorNode);
       
       ReferenceDescriptionCollection Browse(NodeId nodeId);
        void Rebrowse(ref TreeViewNode node);
      ObservableCollection<PubSubConfiguationBase> GetPubSubConfiguation();
 
       ObservableCollection<PublishedDataSetBase>  GetPublishedDataSets();
        PublishedDataSetBase AddPublishedDataSet(string PublisherName,ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection);

        string RemovePublishedDataSet(NodeId PublishedDataSetNodeId);
        string RemovePublishedDataSetVariables(string PublisherName, NodeId PublisherNodeId, ConfigurationVersionDataType ConfigurationVersionDataType, List<UInt32> variableIndexs,out ConfigurationVersionDataType NewConfigurationVersion);

        string AddVariableToPublisher(string PublisherName, NodeId PublisherNodeId,ConfigurationVersionDataType configurationVersionDataType, ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection, out ConfigurationVersionDataType NewConfigurationVersion);

        ReferenceDescriptionCollection Browse(NodeId nodeId, BrowseDirection Direction);

        string AddDataSetReader(NodeId DataSetReaderGroupNodeId,DataSetReaderDefinition dataSetReaderDefinition);
        Subscription GetPubSubStateSubscription(string SubscriptionName);

        string AddTargetVariables(NodeId dataSetReaderNodeId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection);
        string AddAdditionalTargetVariables(NodeId ObjectId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection);

        string RemoveTargetVariable(NodeId ObjectId, ConfigurationVersionDataType VersionType, List<UInt32> TargetsToremove);

        string AddDataSetMirror(DataSetReaderDefinition _DataSetReaderDefinition, string parentName);

        StatusCodeCollection WriteValue(WriteValueCollection writeValueCollection);

        object ReadValue(NodeId nodeId);
    }
 
}
