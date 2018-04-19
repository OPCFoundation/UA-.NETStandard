/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/
using Opc.Ua;
using Opc.Ua.Client;
using PubSubBase.Definitions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ClientAdaptor
{
    public interface IOPCUAClientAdaptor
    {
        ApplicationDescriptionCollection FindServers(string hostName);
        Session Connect(string endpointUrl, out string errorMessage, out TreeViewNode node);
        bool Disconnect();
        string AddConnection(Connection connection, out NodeId connectionId);
        string AddWriterGroup(DataSetWriterGroup dataSetWriterGroup, out NodeId groupId);
        string AddReaderGroup(ReaderGroupDefinition readerGroupDefinition, out NodeId groupId);
        string AddDataSetWriter(NodeId dataSetWriterGroupNodeId, DataSetWriterDefinition dataSetWriterDefinition, out NodeId writerNodeId, out int revisedKeyFrameCount);
        string RemoveDataSetWriter(DataSetWriterGroup dataSetWriterGroup, NodeId writerNodeId);
        string RemoveDataSetReader(ReaderGroupDefinition readerGroupDefinition, NodeId readerNodeId);
        string RemoveConnection(NodeId connectionId);
        string AddNewSecurityGroup(string name, out SecurityGroup securityGroup);
        string RemoveSecurityGroup(NodeId securityGroupId);
        string RemoveGroup(Connection connection, NodeId GroupId);
        ObservableCollection<SecurityGroup> GetSecurityGroups();
        string GetSecurityKeys(string securityGroupId, uint featureKeyCount, out SecurityKeys securityKeys);
        string SetSecurityKeys(SecurityKeys securityKeys);
        Subscription CreateSubscription(string subscriptionName, int subscriptionInterval);
        string EnablePubSubState(MonitorNode monitorNode);
        string DisablePubSubState(MonitorNode monitorNode);
        ReferenceDescriptionCollection Browse(NodeId nodeId);
        void Rebrowse(ref TreeViewNode node);
        ObservableCollection<PubSubConfiguationBase> GetPubSubConfiguation();
        ObservableCollection<PublishedDataSetBase> GetPublishedDataSets();
        PublishedDataSetBase AddPublishedDataSet(string PublisherName, ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection);
        string RemovePublishedDataSet(NodeId publishedDataSetNodeId);
        string RemovePublishedDataSetVariables(string PublisherName, NodeId publisherNodeId, ConfigurationVersionDataType configurationVersionDataType, List<UInt32> variableIndexs, out ConfigurationVersionDataType newConfigurationVersion);
        string AddVariableToPublisher(string publisherName, NodeId publisherNodeId, ConfigurationVersionDataType configurationVersionDataType, ObservableCollection<PublishedDataSetItemDefinition> variableListDefinitionCollection, out ConfigurationVersionDataType newConfigurationVersion);
        ReferenceDescriptionCollection Browse(NodeId nodeId, BrowseDirection direction);
        string AddDataSetReader(NodeId dataSetReaderGroupNodeId, DataSetReaderDefinition dataSetReaderDefinition);
        Subscription GetPubSubStateSubscription(string subscriptionName);
        string AddTargetVariables(NodeId dataSetReaderNodeId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection);
        string AddAdditionalTargetVariables(NodeId objectId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection);
        string RemoveTargetVariable(NodeId objectId, ConfigurationVersionDataType versionType, List<UInt32> targetsToremove);
        string AddDataSetMirror(DataSetReaderDefinition dataSetReaderDefinition, string parentName);
        StatusCodeCollection WriteValue(WriteValueCollection writeValueCollection);
        object ReadValue(NodeId nodeId);
    }

}
