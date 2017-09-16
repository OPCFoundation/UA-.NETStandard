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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.ComponentModel;
using Opc.Ua.Client.Controls;

using PubSubBase.Definitions;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Threading.Tasks;
using Opc.Ua.Sample.Controls;

namespace ClientAdaptor
{
    /// <summary>
    /// adaptor between UI and Client
    /// </summary>
    public class OPCUAClientAdaptor : IOPCUAClientAdaptor, INotifyPropertyChanged
    {
        #region Private Fields
        private int m_reconnectPeriod = 10;
        private SessionReconnectHandler m_reconnectHandler;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_messageContext;
        private ConfiguredEndpointCollection m_configuredEndpointCollection;
        private bool m_isOpened;
        private static long m_sessionCounter = 0;
        private readonly object m_lock = new object();
        bool m_refreshOnReconnection = false;
        bool m_activateTabsonReConnection = false;
        string m_serverStatus = String.Empty;

        #endregion

        #region Public Properties
        /// <summary>
        /// assigning session
        /// </summary>
        public Session Session
        {
            get;
            set;
        }

        /// <summary>
        /// selected end point for connection
        /// </summary>
        public string SelectedEndpoint
        {
            get;
            set;
        }
        /// <summary>
        /// Activating Tabs on Reconnect
        /// </summary>
        public bool ActivateTabsonReConnection
        {
            get
            {
                return m_activateTabsonReConnection;
            }
            set
            {
                m_activateTabsonReConnection = value;
                OnPropertyChanged("ActivateTabsonReConnection");
            }
        }
        /// <summary>
        /// Current server status
        /// </summary>
        public string ServerStatus
        {
            get { return m_serverStatus; }
            set
            {
                m_serverStatus = value;
                OnPropertyChanged("ServerStatus");
            }
        }

        /// <summary>
        /// Refresh on Reconnect
        /// </summary>
        public bool RefreshOnReconnection
        {
            get
            {
                return m_refreshOnReconnection;
            }
            set
            {
                m_refreshOnReconnection = value;
                OnPropertyChanged("RefreshOnReconnection");
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Defining instance of Client adaptor
        /// </summary>
        public OPCUAClientAdaptor()
        {
            Constants.Initialize();
            LoadApplicationInstance();
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Method to add security group
        /// </summary>
        /// <param name="name"></param>
        /// <param name="securityGroup"></param>
        /// <returns></returns>
        public string AddNewSecurityGroup(string name, out SecurityGroup securityGroup)
        {
            string errorMessage = string.Empty;
            securityGroup = null;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.SecurityGroupObjectId,
                    Constants.SecurityGroupAddMethodId, new object[] { name });
                securityGroup = new SecurityGroup
                {
                    GroupName = name,
                    SecurityGroupId = lstResponse[0].ToString(),
                    GroupNodeId = lstResponse[1] as NodeId
                };
                Utils.Trace(String.Format("AddNewSecurityGroup API with name {0} was Successfull", name));
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to add new AMQP connection
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public string AddAMQPConnection(Connection connection, out NodeId connectionId)
        {
            string errorMessage = string.Empty;
            connectionId = null;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishSubscribeObjectId,
                    Constants.BrokerConnectionMethodId, new object[] { connection.Name, connection.Address, connection.PublisherId });
                connectionId = lstResponse[0] as NodeId;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to add new UADP Connection
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public string AddUADPConnection(Connection connection, out NodeId connectionId)
        {
            string errorMessage = string.Empty;
            connectionId = null;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishSubscribeObjectId,
                    Constants.UADPConnectionMethodId, new object[] { connection.Name, connection.Address, connection.PublisherId });

                connectionId = lstResponse[0] as NodeId;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }


        /// <summary>
        /// Method to remove Connection
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public string RemoveConnection(NodeId connectionId)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishSubscribeObjectId,
                    Constants.RemoveConnectionMethodId, new object[] { connectionId });
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method which returns available security Groups.
        /// </summary>
        /// <returns></returns>
        public ObservableCollection<SecurityGroup> GetSecurityGroups()
        {
            ObservableCollection<SecurityGroup> securityGroups = new ObservableCollection<SecurityGroup>();
            if (Session != null && !Session.KeepAliveStopped)
            {
                try
                {
                    ReferenceDescriptionCollection referenceDescriptionCollection = BrowserNodeControl.Browser.Browse(Constants.SecurityGroups);

                    foreach (ReferenceDescription referenceDescription in referenceDescriptionCollection)
                    {
                        if (referenceDescription.TypeDefinition == Constants.SecurityGroupNameTypeDefinitionId)
                        {
                            try
                            {
                                ReferenceDescriptionCollection refDescriptionCollection = BrowserNodeControl.Browser.Browse((NodeId)referenceDescription.NodeId);
                                ReferenceDescription refDescription = refDescriptionCollection.Where(i => i.BrowseName.Name == "SecurityGroupId").FirstOrDefault();
                                securityGroups.Add(
                                                    new SecurityGroup()
                                                    {
                                                        GroupName = referenceDescription.DisplayName.Text,
                                                        SecurityGroupId = refDescription != null ? Convert.ToString(Session.ReadValue((NodeId)refDescription.NodeId).Value) : string.Empty,
                                                        GroupNodeId = (NodeId)referenceDescription.NodeId
                                                    }
                                                  );
                            }
                            catch (Exception ex)
                            {
                                Utils.Trace(ex, "OPCUAClientAdaptor.GetSecurityGroups API" + ex.Message);
                            }

                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.GetSecurityGroups API" + ex.Message);
                }
            }
            return securityGroups;
        }

        /// <summary>
        /// Method which returns current PubSubConfigurations
        /// </summary>
        /// <returns></returns>
        public ObservableCollection<PubSubConfiguationBase> GetPubSubConfiguation()
        {
            ObservableCollection<PubSubConfiguationBase> pubSubConfiguation = new ObservableCollection<PubSubConfiguationBase>();
            ReferenceDescriptionCollection referenceDescriptionCollection = Browse(Constants.PublishSubscribeObjectId);
            if (referenceDescriptionCollection != null)
            {
                foreach (ReferenceDescription _ReferenceDescription in referenceDescriptionCollection)
                {
                    try
                    {
                        if (_ReferenceDescription.TypeDefinition != Constants.AMQPTypeDefinitionNodeId && _ReferenceDescription.TypeDefinition != Constants.UADPTypeDefinitionNodeId)
                        {
                            continue;

                        }
                        Connection connection = new Connection
                        {
                            ConnectionNodeId = (NodeId)_ReferenceDescription.NodeId,
                            Name = _ReferenceDescription.DisplayName.Text
                        };
                        ReferenceDescriptionCollection refDescriptionCollection = Browse((NodeId)_ReferenceDescription.NodeId);
                        if (refDescriptionCollection != null)
                        {
                            foreach (ReferenceDescription _RefDescription in refDescriptionCollection)
                            {
                                try
                                {
                                    if (_RefDescription.TypeDefinition == Constants.PropertyNodeId)
                                    {
                                        if (_RefDescription.BrowseName.Name == "Address")
                                        {
                                            connection.Address = Session.ReadValue((NodeId)_RefDescription.NodeId).Value.ToString();
                                        }
                                        if (_RefDescription.BrowseName.Name == "PublisherId")
                                        {
                                            object value = Session.ReadValue((NodeId)_RefDescription.NodeId).Value;
                                            Type type = value.GetType();
                                            connection.PublisherDataType = 0;
                                            switch (type.Name.ToLower())
                                            {
                                                case "string":
                                                    connection.PublisherDataType = 0;
                                                    break;
                                                case "byte":
                                                    connection.PublisherDataType = 1;
                                                    break;
                                                case "uint16":
                                                    connection.PublisherDataType = 2;
                                                    break;
                                                case "uint32":
                                                    connection.PublisherDataType = 3;
                                                    break;
                                                case "uint64":
                                                    connection.PublisherDataType = 4;
                                                    break;
                                                case "guid":
                                                    connection.PublisherDataType = 5;
                                                    break;
                                            }
                                            connection.PublisherId = value;
                                        }
                                    }

                                    if (_RefDescription.TypeDefinition == Constants.ReaderGroupTypeId)
                                    {
                                        ReaderGroupDefinition readerGroup = new ReaderGroupDefinition();
                                        readerGroup.Name = readerGroup.GroupName = _RefDescription.DisplayName.Text;
                                        readerGroup.GroupId = (NodeId)_RefDescription.NodeId;
                                        readerGroup.ParentNode = connection;
                                        ReferenceDescriptionCollection refDescriptionReaderGroupCollection = Browse((NodeId)_RefDescription.NodeId);

                                        foreach (ReferenceDescription refDesc in refDescriptionReaderGroupCollection)
                                        {
                                            try
                                            {
                                                if (refDesc.TypeDefinition == Constants.DataSetReaderTypeId)
                                                {
                                                    DataSetReaderDefinition dataSetReaderDefinition =
                                                        new DataSetReaderDefinition
                                                        {
                                                            DataSetReaderNodeId =
                                                        (NodeId)refDesc.NodeId
                                                        };
                                                    dataSetReaderDefinition.Name =
                                                        dataSetReaderDefinition.DataSetReaderName =
                                                            refDesc.DisplayName.Text;
                                                    dataSetReaderDefinition.ParentNode = readerGroup;
                                                    ReferenceDescriptionCollection
                                                        refDescriptionDataSetReaderCollection =
                                                            Browse((NodeId)refDesc.NodeId);


                                                    foreach (ReferenceDescription refDsrDesc in
                                                        refDescriptionDataSetReaderCollection)
                                                    {
                                                        if (refDsrDesc.BrowseName.Name == "TransportSettings")
                                                        {
                                                            ReferenceDescriptionCollection
                                                                refDescriptionDataSetReaderGroupCollection =
                                                                    Browse((NodeId)refDsrDesc.NodeId);


                                                            foreach (ReferenceDescription refDsDesc in
                                                                refDescriptionDataSetReaderGroupCollection)
                                                            {
                                                                if (refDsDesc.BrowseName.Name ==
                                                                    "DataSetMessageContentMask")
                                                                {
                                                                    dataSetReaderDefinition.DataSetContentMask =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "DataSetWriterId")
                                                                {
                                                                    dataSetReaderDefinition.DataSetWriterId =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name ==
                                                                         "NetworkMessageContentMask")
                                                                {
                                                                    dataSetReaderDefinition.NetworkMessageContentMask =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "PublisherId")
                                                                {
                                                                    dataSetReaderDefinition.PublisherId =
                                                                        Convert.ToString(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name ==
                                                                         "PublishingInterval")
                                                                {
                                                                    dataSetReaderDefinition.PublishingInterval =
                                                                        Convert.ToDouble(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                            }
                                                        }
                                                        if (refDsrDesc.BrowseName.Name == "SubscribedDataSet")
                                                        {
                                                            ReferenceDescriptionCollection
                                                                refDescriptionDataSetReaderGroupCollection =
                                                                    Browse((NodeId)refDsrDesc.NodeId);


                                                            foreach (ReferenceDescription refDsDesc in
                                                                refDescriptionDataSetReaderGroupCollection)
                                                            {
                                                                if (refDsDesc.BrowseName.Name == "MessageReceiveTimeout"
                                                                )
                                                                {
                                                                    dataSetReaderDefinition.MessageReceiveTimeOut =
                                                                        Convert.ToDouble(Session
                                                                            .ReadValue((NodeId)refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "DataSetMetaData")
                                                                {
                                                                    try
                                                                    {
                                                                        dataSetReaderDefinition.DataSetMetaDataType =
                                                                            (DataSetMetaDataType)(Session.ReadValue(
                                                                                (NodeId)refDsDesc.NodeId,
                                                                                typeof(DataSetMetaDataType)));
                                                                    }
                                                                    catch (Exception ex)
                                                                    {
                                                                        Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration-DataSetMetaData API" + ex.Message);
                                                                    }
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "TargetVariables")
                                                                {
                                                                    SubscribedDataSetDefinition
                                                                        subscribedDataSetDefinition =
                                                                            new SubscribedDataSetDefinition
                                                                            {
                                                                                Name =
                                                                        "SubscribedDataSet",
                                                                                ParentNode =
                                                                        dataSetReaderDefinition
                                                                            };

                                                                    var fields =
                                                                        Session.ReadValue((NodeId)refDsDesc.NodeId);
                                                                    if (fields.Value == null)
                                                                    {
                                                                        continue;
                                                                    }
                                                                    ExtensionObject[] exobjectarry =
                                                                        fields.Value as ExtensionObject[];
                                                                    foreach (ExtensionObject eobj in exobjectarry)
                                                                    {
                                                                        FieldTargetDataType fieldTargetDataType =
                                                                            eobj.Body as FieldTargetDataType;
                                                                        FieldTargetVariableDefinition
                                                                            fieldTargetVariableDefinition =
                                                                                new FieldTargetVariableDefinition
                                                                                {
                                                                                    ParentNode =
                                                                            subscribedDataSetDefinition,
                                                                                    AttributeId =
                                                                            fieldTargetDataType.AttributeId,
                                                                                    DataSetFieldId =
                                                                            fieldTargetDataType.DataSetFieldId
                                                                                .GuidString,
                                                                                    Name =
                                                                            fieldTargetDataType.TargetNodeId.Identifier
                                                                                .ToString(),
                                                                                    OverrideValue =
                                                                            fieldTargetDataType.OverrideValue,
                                                                                    OverrideValueHandling =
                                                                            (int)fieldTargetDataType
                                                                                .OverrideValueHandling,
                                                                                    ReceiverIndexRange = fieldTargetDataType
                                                                            .ReceiverIndexRange,
                                                                                    TargetFieldNodeId = fieldTargetDataType
                                                                            .TargetNodeId,
                                                                                    TargetNodeId =
                                                                            fieldTargetDataType.TargetNodeId
                                                                                .ToString(),
                                                                                    WriteIndexRange =
                                                                            fieldTargetDataType.WriteIndexRange,
                                                                                    FieldTargetDataType = fieldTargetDataType
                                                                                };
                                                                        subscribedDataSetDefinition.Children.Add(
                                                                            fieldTargetVariableDefinition);
                                                                    }

                                                                    dataSetReaderDefinition.Children.Add(
                                                                        subscribedDataSetDefinition);

                                                                }
                                                                else if (refDsDesc.TypeDefinition == Constants
                                                                             .BaseDataVariableType)
                                                                {

                                                                    MirrorSubscribedDataSetDefinition
                                                                        mirrorSubscribedDataSetDefinition =
                                                                            new MirrorSubscribedDataSetDefinition
                                                                            {
                                                                                Name =
                                                                        "SubscribedDataSet",
                                                                                ParentNode =
                                                                        dataSetReaderDefinition
                                                                            };
                                                                    ReferenceDescriptionCollection refDesCollection =
                                                                        Browse((NodeId)refDsDesc.NodeId);
                                                                    foreach (ReferenceDescription _RefDesc in
                                                                        refDesCollection)
                                                                    {
                                                                        MirrorVariableDefinition
                                                                            mirrorVariableDefinition =
                                                                                new MirrorVariableDefinition
                                                                                {
                                                                                    Name = _RefDesc
                                                                            .DisplayName.Text,
                                                                                    ParentNode =
                                                                            mirrorSubscribedDataSetDefinition
                                                                                };
                                                                        mirrorSubscribedDataSetDefinition.Children.Add(
                                                                            mirrorVariableDefinition);
                                                                    }

                                                                    dataSetReaderDefinition.Children.Add(
                                                                        mirrorSubscribedDataSetDefinition);
                                                                    break;
                                                                }

                                                            }
                                                        }
                                                    }
                                                    readerGroup.Children.Add(dataSetReaderDefinition);
                                                }

                                                if (refDesc.TypeDefinition == Constants.PropertyNodeId)
                                                {
                                                    if (refDesc.BrowseName.Name == "SecurityGroupId")
                                                    {
                                                        readerGroup.SecurityGroupId =
                                                            Session.ReadValue((NodeId)refDesc.NodeId).Value.ToString();
                                                    }
                                                    if (refDesc.BrowseName.Name == "SecurityMode")
                                                    {
                                                        readerGroup.SecurityMode =
                                                            Convert.ToInt32(Session.ReadValue((NodeId)refDesc.NodeId)
                                                                .Value);
                                                    }
                                                    if (refDesc.BrowseName.Name == "MaxNetworkMessageSize")
                                                    {
                                                        readerGroup.MaxNetworkMessageSize =
                                                            Convert.ToInt32(Session.ReadValue((NodeId)refDesc.NodeId)
                                                                .Value);
                                                    }
                                                    if (refDesc.BrowseName.Name == "QueueName")
                                                    {
                                                        readerGroup.QueueName =
                                                            Session.ReadValue((NodeId)refDesc.NodeId).Value.ToString();
                                                    }
                                                }


                                            }
                                            catch (Exception ex)
                                            {
                                                Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                                            }
                                        }


                                        connection.Children.Add(readerGroup);

                                    }

                                    if (_RefDescription.TypeDefinition == Constants.WriterGroupTypeId)
                                    {
                                        DataSetWriterGroup dataSetWriterGroup = new DataSetWriterGroup();
                                        dataSetWriterGroup.Name = dataSetWriterGroup.GroupName = _RefDescription.DisplayName.Text;
                                        dataSetWriterGroup.GroupId = (NodeId)_RefDescription.NodeId;
                                        dataSetWriterGroup.ParentNode = connection;
                                        ReferenceDescriptionCollection refDescriptionWriterCollection = Browse((NodeId)_RefDescription.NodeId, BrowseDirection.Both);
                                        foreach (ReferenceDescription referenceDescription in refDescriptionWriterCollection)
                                        {
                                            try
                                            {
                                                if (referenceDescription.TypeDefinition == Constants.DataSetWriterTypeId)
                                                {
                                                    DataSetWriterDefinition dataSetWriterDefinition = new DataSetWriterDefinition();
                                                    dataSetWriterDefinition.Name = dataSetWriterDefinition.DataSetWriterName = referenceDescription.DisplayName.Text;
                                                    dataSetWriterDefinition.WriterNodeId = (NodeId)referenceDescription.NodeId;
                                                    dataSetWriterDefinition.ParentNode = dataSetWriterGroup;
                                                    ReferenceDescriptionCollection refDescWriterCollection = Browse((NodeId)referenceDescription.NodeId, BrowseDirection.Both);
                                                    foreach (ReferenceDescription refDesc in refDescWriterCollection)
                                                    {
                                                        if (refDesc.TypeDefinition == Constants.WriterToDataSetTypeDefinition)
                                                        {
                                                            dataSetWriterDefinition.PublisherDataSetNodeId =
                                                                (NodeId)refDesc.NodeId;

                                                        }
                                                        if (refDesc.BrowseName.Name == "DataSetMessageContentMask")
                                                        {
                                                            dataSetWriterDefinition.DataSetContentMask =
                                                               Convert.ToInt32(Session.ReadValue((NodeId)refDesc.NodeId).Value);

                                                        }
                                                        if (refDesc.TypeDefinition == Constants.TransPortSettingsTypeDefinition)
                                                        {
                                                            ReferenceDescriptionCollection refDescWriterPropertiesCollection = Browse((NodeId)refDesc.NodeId, BrowseDirection.Both);
                                                            foreach (ReferenceDescription refDescription in refDescWriterPropertiesCollection)
                                                            {
                                                                try
                                                                {
                                                                    if (refDescription.TypeDefinition == Constants.PropertyNodeId)
                                                                    {

                                                                        if (refDescription.BrowseName.Name == "DataSetWriterId")
                                                                        {
                                                                            dataSetWriterDefinition.DataSetWriterId =
                                                                                Convert.ToInt32(Session
                                                                                    .ReadValue((NodeId)refDescription.NodeId).Value);
                                                                        }

                                                                        else if (refDescription.BrowseName.Name == "KeyFrameCount")
                                                                        {
                                                                            dataSetWriterDefinition.KeyFrameCount =
                                                                                Convert.ToInt32(Session
                                                                                    .ReadValue((NodeId)refDescription.NodeId).Value);
                                                                        }
                                                                        else if (refDescription.BrowseName.Name == "QueueName")
                                                                        {
                                                                            dataSetWriterDefinition.QueueName =
                                                                                Session.ReadValue((NodeId)refDescription.NodeId)
                                                                                    .Value.ToString();
                                                                        }
                                                                        else if (refDescription.BrowseName.Name == "MetadataQueueName")
                                                                        {
                                                                            dataSetWriterDefinition.MetadataQueueName =
                                                                                Session.ReadValue((NodeId)refDescription.NodeId)
                                                                                    .Value.ToString();
                                                                        }
                                                                        else if (refDescription.BrowseName.Name == "MetadataUpdataTime")
                                                                        {
                                                                            dataSetWriterDefinition.MetadataUpdataTime =
                                                                                Convert.ToInt32(Session
                                                                                    .ReadValue((NodeId)refDescription.NodeId).Value);
                                                                        }
                                                                        else if (refDescription.BrowseName.Name == "MaxMessageSize")
                                                                        {
                                                                            dataSetWriterDefinition.MaxMessageSize =
                                                                                Convert.ToInt32(Session
                                                                                    .ReadValue((NodeId)refDescription.NodeId).Value);
                                                                        }
                                                                    }
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                                                                }
                                                            }
                                                        }
                                                    }

                                                    dataSetWriterGroup.Children.Add(dataSetWriterDefinition);

                                                }
                                                if (referenceDescription.TypeDefinition == Constants.PropertyNodeId)
                                                {
                                                    if (referenceDescription.BrowseName.Name == "SecurityMode")
                                                    {
                                                        dataSetWriterGroup.MessageSecurityMode = Convert.ToInt32(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "EncodingMimeType")
                                                    {
                                                        dataSetWriterGroup.EncodingMimeType = Session.ReadValue((NodeId)referenceDescription.NodeId).Value.ToString();
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "KeepAliveTime")
                                                    {
                                                        dataSetWriterGroup.KeepAliveTime = Convert.ToDouble(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "Priority")
                                                    {
                                                        dataSetWriterGroup.Priority = Convert.ToInt32(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "PublishingInterval")
                                                    {
                                                        dataSetWriterGroup.PublishingInterval = Convert.ToDouble(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "MaxNetworkMessageSize")
                                                    {
                                                        dataSetWriterGroup.MaxNetworkMessageSize = Convert.ToInt32(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "NetworkMessageContentMask")
                                                    {
                                                        dataSetWriterGroup.NetworkMessageContentMask = Convert.ToInt32(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "PublishingOffset")
                                                    {
                                                        dataSetWriterGroup.PublishingOffset = Convert.ToDouble(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "WriterGroupId")
                                                    {
                                                        dataSetWriterGroup.WriterGroupId = Convert.ToInt32(Session.ReadValue((NodeId)referenceDescription.NodeId).Value);
                                                    }
                                                    else if (referenceDescription.BrowseName.Name == "SecurityGroupId")
                                                    {
                                                        object val = Session.ReadValue((NodeId)referenceDescription.NodeId).Value;
                                                        dataSetWriterGroup.SecurityGroupId = val.ToString();

                                                    }

                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                                            }
                                        }



                                        connection.Children.Add((dataSetWriterGroup));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                                }
                            }
                        }
                        if (_ReferenceDescription.TypeDefinition == Constants.AMQPTypeDefinitionNodeId)
                        {
                            connection.ConnectionType = 1;
                        }

                        else if (_ReferenceDescription.TypeDefinition == Constants.UADPTypeDefinitionNodeId)
                        {
                            connection.ConnectionType = 0;
                        }
                        pubSubConfiguation.Add(connection);
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace(ex, "ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                    }
                }
            }
            return pubSubConfiguation;
        }

        /// <summary>
        /// Method to remove selected security group.
        /// </summary>
        /// <param name="securityGroupId"></param>
        /// <returns></returns>
        public string RemoveSecurityGroup(NodeId securityGroupId)
        {
            string errorMessage = string.Empty;

            try
            {
                IList<object> lstResponse = Session.Call(Constants.SecurityGroupObjectId,
                    Constants.SecurityGroupRemoveMethodId, new object[] { securityGroupId });

            }
            catch (ServiceResultException e)
            {
                errorMessage = e.LocalizedText.Text;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to remove selected group.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="groupId"></param>
        /// <returns></returns>
        public string RemoveGroup(Connection connection, NodeId groupId)
        {
            string errorMessage = string.Empty;
            NodeId connectionId = connection.ConnectionNodeId;

            string removeGroupId = string.Format("{0}.{1}.{2}", "PubSub", connection.Name, "RemoveGroup");

            NodeId removeGroupNodeId = new NodeId(removeGroupId, 1);
            try
            {
                IList<object> lstResponse = Session.Call(connectionId,
                    removeGroupNodeId, new object[] { groupId });
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to remove selected DataSetWriter.
        /// </summary>
        /// <param name="dataSetWriterGroup"></param>
        /// <param name="writerNodeId"></param>
        /// <returns></returns>
        public string RemoveDataSetWriter(DataSetWriterGroup dataSetWriterGroup, NodeId writerNodeId)
        {
            string errorMessage = string.Empty;
            Connection connection = dataSetWriterGroup.ParentNode as Connection;
            NodeId dataSetWriterGroupId = dataSetWriterGroup.GroupId;
            if (connection != null)
            {
                string removeDataSetWriter = string.Format("{0}.{1}.{2}.{3}", "PubSub", connection.Name, dataSetWriterGroup.Name, "RemoveDataSetWriter");
                NodeId removeDataSetWriterNodeId = new NodeId(removeDataSetWriter, 1);
                try
                {
                    IList<object> lstResponse = Session.Call(dataSetWriterGroupId,
                                                             removeDataSetWriterNodeId, new object[] { writerNodeId });
                }
                catch (Exception e)
                {
                    errorMessage = e.Message;
                }
            }
            return errorMessage;

        }

        /// <summary>
        /// Method to remove selected DataSetReader
        /// </summary>
        /// <param name="readerGroupDefinition"></param>
        /// <param name="readerNodeId"></param>
        /// <returns></returns>
        public string RemoveDataSetReader(ReaderGroupDefinition readerGroupDefinition, NodeId readerNodeId)
        {
            string errorMessage = string.Empty;
            Connection connection = readerGroupDefinition.ParentNode as Connection;
            NodeId readerGroupDefinitionNodeId = readerGroupDefinition.GroupId;
            if (connection != null)
            {
                string removeDataSetReader = string.Format("{0}.{1}.{2}.{3}", "PubSub", connection.Name, readerGroupDefinition.Name, "RemoveDataSetReader");
                NodeId removeDataSetReaderNodeId = new NodeId(removeDataSetReader, 1);
                try
                {
                    IList<object> lstResponse = Session.Call(readerGroupDefinitionNodeId,
                                                             removeDataSetReaderNodeId, new object[] { readerNodeId });
                }
                catch (Exception e)
                {
                    errorMessage = e.Message;
                }
            }
            return errorMessage;

        }

        /// <summary>
        /// Method to set Security keys.
        /// </summary>
        /// <param name="securityKeys"></param>
        /// <returns></returns>
        public string SetSecurityKeys(SecurityKeys securityKeys)
        {
            string errorMessage = string.Empty;
            try
            {
                byte[] currentkey = Encoding.UTF8.GetBytes(securityKeys.CurrentKey);
                List<Byte[]> lstFeaturekeys = new List<byte[]>();
                foreach (string fKeys in securityKeys.FeatureKeys)
                {
                    lstFeaturekeys.Add(Encoding.UTF8.GetBytes(fKeys));
                }
                IList<object> lstResponse = Session.Call(Constants.SecurityKeysObjectId,
                    Constants.SetSecurityKeysMethodId, new object[] { securityKeys.SecurityGroupId, securityKeys.SecurityPolicyUri, securityKeys.CurrentTokenId, currentkey, lstFeaturekeys.ToArray(), securityKeys.TimeToNextKey, securityKeys.KeyLifetime });

            }
            catch (ServiceResultException e)
            {
                errorMessage = e.LocalizedText.Text;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;

        }

        /// <summary>
        /// Method which returns security keys for selected groupId
        /// </summary>
        /// <param name="securityGroupId"></param>
        /// <param name="featureKeyCount"></param>
        /// <param name="securityKeys"></param>
        /// <returns></returns>
        public string GetSecurityKeys(string securityGroupId, uint featureKeyCount, out SecurityKeys securityKeys)
        {
            string errorMessage = string.Empty;
            securityKeys = new SecurityKeys();
            try
            {
                IList<object> lstResponse = Session.Call(Constants.SecurityKeysObjectId,
                Constants.GetSecurityKeysMethodId, new object[] { securityGroupId, featureKeyCount });
                securityKeys.SecurityGroupId = securityGroupId;
                securityKeys.SecurityPolicyUri = Convert.ToString(lstResponse[0]);
                securityKeys.CurrentTokenId = Convert.ToUInt32(lstResponse[1]);
                securityKeys.CurrentKey = Encoding.UTF8.GetString(lstResponse[2] as Byte[]);
                var array = (lstResponse[3] as IEnumerable).Cast<object>().Select(x => x as byte[]).ToArray();
                foreach (var featurekeybytearray in array)
                {
                    securityKeys.FeatureKeys.Add(Encoding.UTF8.GetString(featurekeybytearray as Byte[]));
                }
                securityKeys.TimeToNextKey = Convert.ToDouble(lstResponse[4]);
                securityKeys.KeyLifetime = Convert.ToDouble(lstResponse[5]);
            }
            catch (ServiceResultException e)
            {
                errorMessage = e.LocalizedText.Text;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "ClientAdaptor:GetSecurityKeys API" + e.Message);
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to Enable PubSub State for selected node.
        /// </summary>
        /// <param name="monitorNode"></param>
        /// <returns></returns>
        public string EnablePubSubState(MonitorNode monitorNode)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(monitorNode.ParentNodeId,
                      monitorNode.EnableNodeId, new object[] { });
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "ClientAdaptor:EnablePubSubState API" + ex.Message);
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to disable PubSub State for selected node.
        /// </summary>
        /// <param name="monitorNode"></param>
        /// <returns></returns>
        public string DisablePubSubState(MonitorNode monitorNode)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(monitorNode.ParentNodeId,
                    monitorNode.DisableNodeId, new object[] { });
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "ClientAdaptor:DisablePubSubState API" + ex.Message);
            }
            return errorMessage;

        }

        /// <summary>
        /// Method to add new writer group.
        /// </summary>
        /// <param name="dataSetWriterGroup"></param>
        /// <param name="groupId"></param>
        /// <returns></returns>
        public string AddWriterGroup(DataSetWriterGroup dataSetWriterGroup, out NodeId groupId)
        {
            string errorMessage = string.Empty;
            groupId = null;

            Connection connection = dataSetWriterGroup.ParentNode as Connection;
            if (connection != null)
            {
                string name = connection.Name;
            }

            if (connection != null && connection.ConnectionType == 0)
            {
                try
                {
                    NodeId writerGroupNodeId = new NodeId(connection.ConnectionNodeId.Identifier.ToString() + ".AddWriterGroup", 1);
                    IList<object> lstResponse = Session.Call(connection.ConnectionNodeId,
                        writerGroupNodeId,
                        new object[]
                        {
                                dataSetWriterGroup.GroupName, dataSetWriterGroup.PublishingInterval,
                                dataSetWriterGroup.PublishingOffset, dataSetWriterGroup.KeepAliveTime,
                                dataSetWriterGroup.Priority, dataSetWriterGroup.MessageSecurityMode,
                                dataSetWriterGroup.SecurityGroupId, dataSetWriterGroup.WriterGroupId,
                                dataSetWriterGroup.MaxNetworkMessageSize,dataSetWriterGroup.NetworkMessageContentMask

                        });
                    groupId = lstResponse[0] as NodeId;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }
            else if (connection != null && connection.ConnectionType == 1)
            {
                try
                {
                    NodeId writerGroupNodeId = new NodeId(connection.ConnectionNodeId.Identifier.ToString() + ".AddWriterGroup", 1);
                    IList<object> lstResponse = Session.Call(connection.ConnectionNodeId,
                        writerGroupNodeId,
                        new object[]
                        {
                                dataSetWriterGroup.GroupName, dataSetWriterGroup.KeepAliveTime,
                                dataSetWriterGroup.MessageSecurityMode, dataSetWriterGroup.EncodingMimeType,
                                dataSetWriterGroup.QueueName, dataSetWriterGroup.Priority,
                                dataSetWriterGroup.PublishingInterval, dataSetWriterGroup.SecurityGroupId
                        });

                    groupId = lstResponse[0] as NodeId;
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to add new reader group.
        /// </summary>
        /// <param name="readerGroupDefinition"></param>
        /// <param name="groupId"></param>
        /// <returns></returns>
        public string AddReaderGroup(ReaderGroupDefinition readerGroupDefinition, out NodeId groupId)
        {
            string errorMessage = string.Empty;
            groupId = null;

            Connection connection = readerGroupDefinition.ParentNode as Connection;
            if (connection != null)
            {
                string name = connection.Name;
            }

            if (connection != null)
            {
                NodeId connectionNodeId = connection.ConnectionNodeId;
                NodeId readerGroupNodeId = null;
                ReferenceDescriptionCollection referenceDescriptionReaderCollection = BrowserNodeControl.Browser.Browse(connectionNodeId);
                foreach (ReferenceDescription referenceReaderDescription in referenceDescriptionReaderCollection)
                {
                    if (referenceReaderDescription.BrowseName == "AddReaderGroup")
                    {
                        readerGroupNodeId = (NodeId)referenceReaderDescription.NodeId;
                    }
                }
                if (connection.ConnectionType == 0)
                {
                    try
                    {
                        IList<object> lstResponse = Session.Call(connectionNodeId,
                                                                 readerGroupNodeId,
                                                                 new object[]
                                                                 {
                                                                         readerGroupDefinition.GroupName, readerGroupDefinition.SecurityMode,
                                                                         readerGroupDefinition.SecurityGroupId,readerGroupDefinition.MaxNetworkMessageSize
                                                                 });
                        groupId = lstResponse[0] as NodeId;
                    }
                    catch (Exception ex)
                    {
                        errorMessage = ex.Message;
                    }
                }
                else if (connection.ConnectionType == 1)
                {
                    try
                    {
                        IList<object> lstResponse = Session.Call(connectionNodeId,
                                                                 readerGroupNodeId,
                                                                 new object[]
                                                                 {
                                                                         readerGroupDefinition.GroupName, readerGroupDefinition.SecurityMode,
                                                                         readerGroupDefinition.SecurityGroupId,readerGroupDefinition.QueueName
                                                                 });
                        readerGroupDefinition.GroupId = lstResponse[0] as NodeId;
                    }
                    catch (Exception ex)
                    {
                        errorMessage = ex.Message;
                    }
                }
            }

            return errorMessage;

        }

        /// <summary>
        /// Method to add new data set reader.
        /// </summary>
        /// <param name="dataSetReaderGroupNodeId"></param>
        /// <param name="dataSetReaderDefinition"></param>
        /// <returns></returns>
        public string AddDataSetReader(NodeId dataSetReaderGroupNodeId, DataSetReaderDefinition dataSetReaderDefinition)
        {
            string retMsg = string.Empty;
            try
            {
                NodeId addDataSetReaderMethodNodeId = new NodeId(dataSetReaderGroupNodeId.Identifier.ToString() + ".AddDataSetReader", 1);
                IList<object> lstResponse = Session.Call(dataSetReaderGroupNodeId,
                       addDataSetReaderMethodNodeId,
                       new object[]
                       {
                            dataSetReaderDefinition.DataSetReaderName,dataSetReaderDefinition.PublisherId,dataSetReaderDefinition.DataSetWriterId,dataSetReaderDefinition.DataSetMetaDataType,dataSetReaderDefinition.MessageReceiveTimeOut,dataSetReaderDefinition.NetworkMessageContentMask,dataSetReaderDefinition.DataSetContentMask,dataSetReaderDefinition.PublishingInterval

                       });

                dataSetReaderDefinition.DataSetReaderNodeId = lstResponse[0] as NodeId;
                dataSetReaderDefinition.MessageReceiveTimeOut = Convert.ToInt32(lstResponse[1]);
            }
            catch (Exception ex)
            {
                retMsg = ex.Message;

            }
            return retMsg;
        }

        /// <summary>
        /// Method to add new UADP DataSetWriter.
        /// </summary>
        /// <param name="dataSetWriterGroupNodeId"></param>
        /// <param name="dataSetWriterDefinition"></param>
        /// <param name="writerNodeId"></param>
        /// <param name="revisedKeyFrameCount"></param>
        /// <returns></returns>
        public string AddUADPDataSetWriter(NodeId dataSetWriterGroupNodeId, DataSetWriterDefinition dataSetWriterDefinition, out NodeId writerNodeId,
            out int revisedKeyFrameCount)
        {
            string errorMessage = string.Empty;
            writerNodeId = null;
            revisedKeyFrameCount = 0;

            try
            {

                NodeId addDataSetWriterNodeId = new NodeId(dataSetWriterGroupNodeId.Identifier.ToString() + ".AddDataSetWriter", 1);

                IList<object> lstResponse = Session.Call(dataSetWriterGroupNodeId,
                    addDataSetWriterNodeId,
                    new object[]
                    {
                            dataSetWriterDefinition.DataSetWriterName,dataSetWriterDefinition.DataSetWriterId,dataSetWriterDefinition.PublisherDataSetNodeId,dataSetWriterDefinition.DataSetContentMask,dataSetWriterDefinition.KeyFrameCount

                    });

                writerNodeId = lstResponse[0] as NodeId;
                revisedKeyFrameCount = Convert.ToInt32(lstResponse[1]);
            }

            catch (Exception ex)
            {
                errorMessage = ex.Message;

            }

            return errorMessage;

        }

        /// <summary>
        /// Method to add new AMQP DataSetWriter.
        /// </summary>
        /// <param name="dataSetWriterGroupNodeId"></param>
        /// <param name="dataSetWriterDefinition"></param>
        /// <param name="writerNodeId"></param>
        /// <param name="revisedMaxMessageSize"></param>
        /// <returns></returns>
        public string AddAMQPDataSetWriter(NodeId dataSetWriterGroupNodeId, DataSetWriterDefinition dataSetWriterDefinition, out NodeId writerNodeId,
            out int revisedMaxMessageSize)
        {
            string errorMessage = string.Empty;
            writerNodeId = null;
            revisedMaxMessageSize = 0;


            try
            {
                NodeId addDataSetWriterNodeId = new NodeId(dataSetWriterGroupNodeId.Identifier.ToString() + ".AddDataSetwriter", 1);
                IList<object> lstResponse = Session.Call(dataSetWriterGroupNodeId,
                    addDataSetWriterNodeId,
                    new object[]
                    {
                                dataSetWriterDefinition.DataSetWriterName,dataSetWriterDefinition.PublisherDataSetId,dataSetWriterDefinition.QueueName,dataSetWriterDefinition.MetadataQueueName,dataSetWriterDefinition.MetadataUpdataTime,dataSetWriterDefinition.MaxMessageSize
                    });

                writerNodeId = lstResponse[0] as NodeId;
                revisedMaxMessageSize = Convert.ToInt32(lstResponse[1]);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            return errorMessage;

        }

        /// <summary>
        /// Method which finds available servers in selected host.
        /// </summary>
        /// <param name="hostName"></param>
        /// <returns></returns>
        public ApplicationDescriptionCollection FindServers(string hostName)
        {
            try
            {
                // Cursor = Cursors.WaitCursor;

                // set a short timeout because this is happening in the drop down event.
                var configuration = EndpointConfiguration.Create();
                configuration.OperationTimeout = 20000;

                // Connect to the local discovery server and find the available servers.
                using (var client = DiscoveryClient.Create(new Uri(Utils.Format("opc.tcp://{0}:4840", hostName)),
                                                             configuration))
                {
                    return client.FindServers(null);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace("Error Establishing a connection. " + ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Method to connect the server.
        /// </summary>
        /// <param name="endpointUrl"></param>
        /// <param name="errorMessage"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public Session Connect(string endpointUrl, out string errorMessage, out TreeViewNode node)
        {

            node = new TreeViewNode();
            errorMessage = string.Empty;
            try
            {
                var endpoint = CreateConfiguredEndpoint(endpointUrl);

                if (endpoint == null)
                {
                    errorMessage = "Couldn't open endpoint";
                    return null;
                }
                if (endpoint.UpdateBeforeConnect)
                {
                    ConfiguredServerDlg configuredServerDlg = new ConfiguredServerDlg();
                    endpoint = configuredServerDlg.ShowDialog(endpoint, m_configuration);

                    if (endpoint == null)
                    {
                        errorMessage = configuredServerDlg.StatusText;
                        return null;
                    }
                    SelectedEndpoint = endpoint.ToString();
                }

                X509Certificate2 clientCertificate = null;
                if (endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
                {
                    if (m_configuration.SecurityConfiguration.ApplicationCertificate == null)
                    {
                        errorMessage = "Application certificate is empty";
                        return null;
                    }

                    Task<X509Certificate2> taskclientCertificate = m_configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
                    if (taskclientCertificate == null)
                    {
                        errorMessage = "Couldn't able to find the client certificate";
                        return null;
                    }
                    clientCertificate = taskclientCertificate.Result as X509Certificate2;
                }

                var channel = SessionChannel.Create(m_configuration, endpoint.Description, endpoint.Configuration,
                                                   clientCertificate, m_messageContext);
                Session = new Session(channel, m_configuration, endpoint, clientCertificate)
                {
                    ReturnDiagnostics = DiagnosticsMasks.All
                };
                if (!new SessionOpenDlg().ShowDialog(Session, null))
                {
                    return null;
                }
                Session.KeepAliveInterval = 10000;
                Session.KeepAlive += Session_KeepAlive;
                errorMessage = string.Empty;

                BrowserNodeControl = new BrowseNodeControl(Session);
                BrowserNodeControl.InitializeBrowserView(BrowseViewType.Objects, null);

                node.IsRoot = true;
                BrowserNodeControl.Browse(ref node);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.Connect API" + ex.Message);
            }
            //  channel = null; ToDO: Do we need to close the channel
            return Session;
        }

        /// <summary>
        /// Rebrowse the selected node in the tree.
        /// </summary>
        /// <param name="node"></param>
        public void Rebrowse(ref TreeViewNode node)
        {
            BrowserNodeControl.Browse(ref node);
        }

        /// <summary>
        /// Creates configures endpoint
        /// </summary>
        /// <param name="serviceUrl"></param>
        /// <returns></returns>
        private ConfiguredEndpoint CreateConfiguredEndpoint(string serviceUrl)
        {
            //Check for security parameters appended to the URL
            string m_parameters = null;
            var m_index = serviceUrl.IndexOf("- [", StringComparison.Ordinal);
            if (m_index != -1)
            {
                m_parameters = serviceUrl.Substring(m_index + 3);
                serviceUrl = serviceUrl.Substring(0, m_index).Trim();
            }

            var m_useBinaryEncoding = true;
            if (!string.IsNullOrEmpty(m_parameters))
            {
                var fields = m_parameters.Split(new[] { '-', '[', ':', ']' }, StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    if (fields.Length > 2) m_useBinaryEncoding = fields[2] == "Binary";
                    else m_useBinaryEncoding = false;
                }
                catch
                {
                    m_useBinaryEncoding = false;
                }
            }
            Uri m_uri = null;
            try
            {
                m_uri = new Uri(serviceUrl);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.CreateConfiguredEndpoint API" + ex.Message);
                return null;
            }
            var m_description = new EndpointDescription
            {
                EndpointUrl = m_uri.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None
            };

            m_description.Server.ApplicationUri = Utils.UpdateInstanceUri(m_uri.ToString());
            m_description.Server.ApplicationName = m_uri.AbsolutePath;

            if (m_description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                m_description.TransportProfileUri = Profiles.UaTcpTransport;
                m_description.Server.DiscoveryUrls.Add(m_description.EndpointUrl);
            }
            else if (m_description.EndpointUrl.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
            {
                m_description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                m_description.Server.DiscoveryUrls.Add(m_description.EndpointUrl);
            }

            var m_configuredEndpointCollection = new ConfiguredEndpointCollection();
            var m_endpoint = new ConfiguredEndpoint(m_configuredEndpointCollection, m_description, null);
            m_endpoint.Configuration.UseBinaryEncoding = m_useBinaryEncoding;
            m_endpoint.UpdateBeforeConnect = true;
            return m_endpoint;
        }

        /// <summary>
        /// Handles a keep alive event from a session.
        /// </summary>
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(session, Session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    UpdateStatus(true, e.CurrentTime, "Communication Error ({0}), Reconnecting in {1}s", e.Status, m_reconnectPeriod);

                    if (m_reconnectHandler == null)
                    {
                        ActivateTabsonReConnection = false;
                        RefreshOnReconnection = false;
                        m_reconnectHandler = new SessionReconnectHandler();
                        m_reconnectHandler.BeginReconnect(Session, m_reconnectPeriod * 1000, Server_ReconnectComplete);
                    }

                    return;
                }
                // update status.
                UpdateStatus(false, e.CurrentTime, "Connected [{0}]", session.Endpoint.EndpointUrl);




            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("OPC UA PubSub Client", exception);
            }
        }

        /// <summary>
        /// Handles a reconnect event complete from the reconnect handler.
        /// </summary>
        private void Server_ReconnectComplete(object sender, EventArgs e)
        {
            try
            {
                // ignore callbacks from discarded objects.
                if (!Object.ReferenceEquals(sender, m_reconnectHandler))
                {
                    return;
                }
                Session = m_reconnectHandler.Session;
                Session.KeepAlive += Session_KeepAlive;
                m_reconnectHandler.Dispose();
                m_reconnectHandler = null;
                BrowserNodeControl = new BrowseNodeControl(Session);
                BrowserNodeControl.InitializeBrowserView(BrowseViewType.Objects, null);
                try
                {
                    RefreshOnReconnection = true;
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.Server_ReconnectComplete API" + ex.Message);
                }
                try
                {
                    ActivateTabsonReConnection = true;
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.Server_ReconnectComplete API" + ex.Message);
                }



            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("OPC UA PubSub Client", exception);
            }
        }

        /// <summary>
        /// updates the server status.
        /// </summary>
        /// <param name="error"></param>
        /// <param name="time"></param>
        /// <param name="status"></param>
        /// <param name="args"></param>
        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            ServerStatus = String.Format(status, args);
        }

        /// <summary>
        /// Method to select the endpoint
        /// </summary>
        /// <param name="discoveryUrl"></param>
        /// <param name="useSecurity"></param>
        /// <returns></returns>
        private static EndpointDescription SelectEndpoint(string discoveryUrl, bool useSecurity)
        {
            // needs to add the '/discovery' back onto non-UA TCP URLs.
            if (!discoveryUrl.StartsWith(Utils.UriSchemeOpcTcp))
                if (!discoveryUrl.EndsWith("/discovery")) discoveryUrl += "/discovery";

            // parse the selected URL.
            var uri = new Uri(discoveryUrl);

            // set a short timeout because this is happening in the drop down event.
            var configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;

            EndpointDescription m_selectedEndpoint = null;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (var m_client = DiscoveryClient.Create(uri, configuration))
            {
                var m_endpoints = m_client.GetEndpoints(null);

                // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
                for (var ii = 0; ii < m_endpoints.Count; ii++)
                {
                    var endpoint = m_endpoints[ii];

                    // check for a match on the URL scheme.
                    if (endpoint.EndpointUrl.StartsWith(uri.Scheme))
                    {
                        // check if security was requested.
                        if (useSecurity)
                        {
                            if (endpoint.SecurityMode == MessageSecurityMode.None) continue;
                        }
                        else
                        {
                            if (endpoint.SecurityMode != MessageSecurityMode.None) continue;
                        }

                        // pick the first available endpoint by default.
                        if (m_selectedEndpoint == null) m_selectedEndpoint = endpoint;

                        // The security level is a relative measure assigned by the server to the 
                        // endpoints that it returns. Clients should always pick the highest level
                        // unless they have a reason not too.
                        if (endpoint.SecurityLevel > m_selectedEndpoint.SecurityLevel) m_selectedEndpoint = endpoint;
                    }
                }

                // pick the first available endpoint by default.
                if (m_selectedEndpoint == null && m_endpoints.Count > 0) m_selectedEndpoint = m_endpoints[2];
            }

            // if a server is behind a firewall it may return URLs that are not accessible to the client.
            // This problem can be avoided by assuming that the domain in the URL used to call 
            // GetEndpoints can be used to access any of the endpoints. This code makes that conversion.
            // Note that the conversion only makes sense if discovery uses the same protocol as the endpoint.

            if (m_selectedEndpoint != null)
            {
                var m_endpointUrl = Utils.ParseUri(m_selectedEndpoint.EndpointUrl);

                if (m_endpointUrl != null && m_endpointUrl.Scheme == uri.Scheme)
                {
                    var m_builder = new UriBuilder(m_endpointUrl)
                    {
                        Host = uri.DnsSafeHost,
                        Port = uri.Port
                    };
                    m_selectedEndpoint.EndpointUrl = m_builder.ToString();
                }
            }

            // return the selected endpoint.
            return m_selectedEndpoint;
        }

        /// <summary>
        /// Disconnect the session and delete the subscription in the session
        /// </summary>
        /// <returns></returns>
        public bool Disconnect()
        {
            if (Session != null)
            {
                try
                {
                    if (Session.Subscriptions != null)
                    {
                        List<Subscription> m_subscriptionCollection = Session.Subscriptions.ToList();
                        foreach (var m_item in m_subscriptionCollection)
                        {
                            DeleteSubscription(m_item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.Disconnect API" + ex.Message);
                }
                try
                {
                    Session.Close();
                    return true;
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.Disconnect API" + ex.Message);
                }
            }
            return false;
        }

        /// <summary>
        /// Method to create session
        /// </summary>
        /// <param name="session"></param>
        /// <param name="preferredLocales"></param>
        /// <returns></returns>
        private bool CreateSession(Session session, IList<string> preferredLocales)
        {
            m_isOpened = false;
            try
            {
                var m_identity = new UserIdentity();
                var m_sessionName = Utils.Format("MySession {0}", Utils.IncrementIdentifier(ref m_sessionCounter));

                if (session.ConfiguredEndpoint.SelectedUserTokenPolicy.TokenType == UserTokenType.Anonymous)
                    m_identity = new UserIdentity();

                else if (session.ConfiguredEndpoint.SelectedUserTokenPolicy.TokenType == UserTokenType.UserName)
                    if (session.ConfiguredEndpoint.UserIdentity != null)
                    {
                        if ((session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).Password != null &&
                             (session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).UserName != null)
                        {
                            var m_password =
                            Encoding.UTF8.GetString((session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken)
                                                     .Password);
                            var m_username = (session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).UserName;
                            m_identity = new UserIdentity(m_username, m_password);
                        }
                        else
                        {
                            MessageBox.Show("UserName or Password is incorrect.", "OPC PubSub Configuration Tool",
                                             MessageBoxButton.OK, MessageBoxImage.Error);
                            m_isOpened = false;
                            return m_isOpened;
                        }
                    }
                    else
                    {
                        MessageBox.Show("UserIdentity not Provided.", "OPC PubSub Configuration Tool", MessageBoxButton.OK,
                                         MessageBoxImage.Error);
                        m_isOpened = false;
                        return m_isOpened;
                    }

                object[] m_dataList = { session, m_sessionName, m_identity, preferredLocales };



                Open(m_dataList);
            }
            catch (NullReferenceException exe)
            {
                Utils.Trace(exe, "OPCUAClientAdaptor.CreateSession API" + exe.Message);
            }
            finally
            {

            }
            return m_isOpened;
        }

        /// <summary>
        /// Method to open session
        /// </summary>
        /// <param name="state"></param>
        private void Open(object state)
        {
            var m_session = ((object[])state)[0] as Session;
            var m_sessionName = ((object[])state)[1] as string;
            var m_identity = ((object[])state)[2] as UserIdentity;
            var m_preferredLocales = ((object[])state)[3] as IList<string>;

            try
            {
                m_session.Open(m_sessionName, (uint)m_session.SessionTimeout, m_identity, m_preferredLocales);
                m_isOpened = true;
            }
            catch (Exception exp)
            {
                MessageBox.Show(exp.Message, "OPC PubSub Configuration Tool", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Method to delete subscription.
        /// </summary>
        /// <param name="subscription"></param>
        /// <returns></returns>
        public bool DeleteSubscription(Subscription subscription)
        {
            if (subscription != null)
            {
                Session.RemoveSubscription(subscription);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Method to get subscription state for selected subscription.
        /// </summary>
        /// <param name="subscriptionName"></param>
        /// <returns></returns>
        public Subscription GetPubSubStateSubscription(string subscriptionName)
        {
            List<Subscription> m_SubscriptionCollection = Session.Subscriptions.ToList();
            Subscription m_subscription = m_SubscriptionCollection.Where(i => i.DisplayName == subscriptionName).FirstOrDefault();
            if (m_subscription != null)
            {
                return m_subscription;
            }
            return null;
        }

        /// <summary>
        /// Method to create Subscription.
        /// </summary>
        /// <param name="subscriptionName"></param>
        /// <param name="subscriptionInterval"></param>
        /// <returns></returns>
        public Subscription CreateSubscription(string subscriptionName, int subscriptionInterval)
        {
            Subscription m_subscription = new Subscription
            {
                DisplayName = subscriptionName,
                PublishingInterval = subscriptionInterval,
                PublishingEnabled = true,
                Priority = 0,
                KeepAliveCount = 1000,
                LifetimeCount = 1000,
                MaxNotificationsPerPublish = 1000
            };

            if (m_subscription.Created == false)
            {
                Session.AddSubscription(m_subscription);
                try
                {
                    m_subscription.Create();
                }
                catch (Exception exe)
                {
                    Utils.Trace(exe, "OPCUAClientAdaptor.CreateSubscription API" + exe.Message);
                }
                //AddLog("New subscription " + "'" + subscriptionName + "'" + " created successfully.");
            }

            return m_subscription;
        }

        /// <summary>
        /// Method to load application instance.
        /// </summary>
        private void LoadApplicationInstance()
        {
            ApplicationInstance m_applicationInst = new ApplicationInstance
            {
                ApplicationName = "OPCUA_PubSub_Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "OPCUAClient_PubSubConfig"
            };

            try
            {
                m_applicationInst.LoadApplicationConfiguration(false).Wait();
                bool m_certOK = m_applicationInst.CheckApplicationInstanceCertificate(false, 0).Result;
                if (!m_certOK)
                {
                    //log
                    throw new Exception("Application instance certificate invalid!");
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.LoadApplicationInstance API" + ex.Message);
            }

            //ApplicationInstance.SetUaValidationForHttps(applicationInst
            //                                             .ApplicationConfiguration.CertificateValidator);
            // ApplicationTitle = ApplicationInst.ApplicationName;

            m_configuration = m_applicationInst.ApplicationConfiguration;
            m_configuredEndpointCollection = m_configuration.LoadCachedEndpoints(true);
            m_configuredEndpointCollection.DiscoveryUrls = m_configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            m_messageContext = m_configuration.CreateMessageContext();

            if (!m_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                m_configuration.CertificateValidator.CertificateValidation += OnCertificateValidation;
        }

        /// <summary>
        /// Method to validate certificate.
        /// </summary>
        /// <param name="validator"></param>
        /// <param name="e"></param>
        private void OnCertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

        /// <summary>
        /// Method to browse the selected node
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public ReferenceDescriptionCollection Browse(NodeId nodeId)
        {
            ReferenceDescriptionCollection m_referenceDescriptionCollection = null;
            if (BrowserNodeControl != null)
            {
                m_referenceDescriptionCollection = BrowserNodeControl.Browser.Browse(nodeId);
            }
            return m_referenceDescriptionCollection;
        }

        /// <summary>
        /// Method to browse the node based on the direction specified.
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="Direction"></param>
        /// <returns></returns>
        public ReferenceDescriptionCollection Browse(NodeId nodeId, BrowseDirection Direction)
        {
            ReferenceDescriptionCollection m_referenceDescriptionCollection = null;
            if (BrowserNodeControl != null)
            {
                BrowserNodeControl.Browser.BrowseDirection = Direction;
                m_referenceDescriptionCollection = BrowserNodeControl.Browser.Browse(nodeId);
                BrowserNodeControl.Browser.BrowseDirection = BrowseDirection.Forward;
            }
            return m_referenceDescriptionCollection;
        }

        /// <summary>
        /// Method to get published DataSets.
        /// </summary>
        /// <returns></returns>
        public ObservableCollection<PublishedDataSetBase> GetPublishedDataSets()
        {
            ObservableCollection<PublishedDataSetBase> m_publishedDataSetBaseCollection = new ObservableCollection<PublishedDataSetBase>();
            ReferenceDescriptionCollection m_referenceDescriptionCollection = Browse(Constants.PublishedDataSetsNodeId);
            foreach (ReferenceDescription m_referenceDescription in m_referenceDescriptionCollection)
            {
                if (m_referenceDescription.TypeDefinition == ObjectTypeIds.PublishedDataItemsType)
                {
                    m_publishedDataSetBaseCollection.Add(LoadPublishedData(m_referenceDescription.DisplayName.Text, (NodeId)m_referenceDescription.NodeId));
                }
            }
            return m_publishedDataSetBaseCollection;
        }

        /// <summary>
        /// Method to load published data with configuration version and DataSetMetadata.
        /// </summary>
        /// <param name="publisherName"></param>
        /// <param name="publisherNodeId"></param>
        /// <returns></returns>
        private PublishedDataSetDefinition LoadPublishedData(string publisherName, NodeId publisherNodeId)
        {
            PublishedDataSetDefinition m_publishedDataSetDefinition = new PublishedDataSetDefinition
            {
                Name = publisherName,
                PublishedDataSetNodeId = (NodeId)publisherNodeId
            };
            DataSetMetaDataDefinition m_dataSetMetaDataDefinition = new DataSetMetaDataDefinition(m_publishedDataSetDefinition) { Name = "DataSetMetaData" };
            PublishedDataDefinition m_publishedDataDefinition = new PublishedDataDefinition(m_publishedDataSetDefinition) { Name = "PublishedData" };
            m_publishedDataSetDefinition.Children.Add(m_dataSetMetaDataDefinition);
            m_publishedDataSetDefinition.Children.Add(m_publishedDataDefinition);
            ReferenceDescriptionCollection m_refDescriptionCollection = Browse(publisherNodeId);
            foreach (ReferenceDescription m_refDescription in m_refDescriptionCollection)
            {
                try
                {
                    if (m_refDescription.BrowseName.Name == "ConfigurationVersion")
                    {
                        ConfigurationVersionDataType m_configurationVersionDataType = (ConfigurationVersionDataType)Session.ReadValue((NodeId)m_refDescription.NodeId, typeof(ConfigurationVersionDataType));
                        if (m_configurationVersionDataType != null)
                        {
                            m_publishedDataSetDefinition.ConfigurationVersionDataType = m_configurationVersionDataType;
                        }
                    }
                    else if (m_refDescription.BrowseName.Name == "DataSetMetaData")
                    {
                        try
                        {
                            DataSetMetaDataType m_dataSetMetaDataType = (DataSetMetaDataType)Session.ReadValue((NodeId)m_refDescription.NodeId, typeof(DataSetMetaDataType));
                            if (m_dataSetMetaDataType != null)
                            {
                                m_dataSetMetaDataDefinition.DataSetMetaDataType = m_dataSetMetaDataType;
                            }
                        }
                        catch (Exception ex)
                        {
                            Utils.Trace(ex, "ClientAdaptor:LoadPublishedData-DataSetMetaData API" + ex.Message);
                        }
                    }
                    else if (m_refDescription.BrowseName.Name == "PublishedData")
                    {
                        DataValue m_datavalue = Session.ReadValue((NodeId)m_refDescription.NodeId);
                        if (m_datavalue == null || m_datavalue.Value == null)
                        {
                            continue;
                        }
                        ExtensionObject[] m_extensionObjectArray = m_datavalue.Value as ExtensionObject[];
                        PublishedVariableDataTypeCollection m_publishedVariableDataTypeArray = new PublishedVariableDataTypeCollection();
                        foreach (ExtensionObject ex_object in m_extensionObjectArray)
                        {
                            m_publishedVariableDataTypeArray.Add(ex_object.Body as PublishedVariableDataType);
                        }
                        if (m_publishedVariableDataTypeArray != null)
                        {
                            // int count = 0;
                            List<FieldMetaData> m_lstfieldmetaData = m_dataSetMetaDataDefinition.DataSetMetaDataType.Fields.ToList();
                            foreach (PublishedVariableDataType _PublishedVariableDataType in m_publishedVariableDataTypeArray)
                            {
                                PublishedDataSetItemDefinition m_publishedDataSetItemDefinition = new PublishedDataSetItemDefinition(m_publishedDataDefinition) { PublishedVariableDataType = _PublishedVariableDataType };
                                try
                                {
                                    m_publishedDataSetItemDefinition.PublishVariableNodeId = _PublishedVariableDataType.PublishedVariable;
                                    m_publishedDataSetItemDefinition.Name = _PublishedVariableDataType.PublishedVariable.Identifier.ToString();
                                    m_publishedDataSetItemDefinition.Attribute = _PublishedVariableDataType.AttributeId;
                                    m_publishedDataSetItemDefinition.SamplingInterval = _PublishedVariableDataType.SamplingIntervalHint;
                                    m_publishedDataSetItemDefinition.DeadbandType = _PublishedVariableDataType.DeadbandType;
                                    m_publishedDataSetItemDefinition.DeadbandValue = _PublishedVariableDataType.DeadbandValue;
                                    m_publishedDataSetItemDefinition.Indexrange = _PublishedVariableDataType.IndexRange;
                                    m_publishedDataSetItemDefinition.SubstituteValue = _PublishedVariableDataType.SubstituteValue;
                                    m_publishedDataSetItemDefinition.FieldMetaDataProperties = _PublishedVariableDataType.MetaDataProperties;

                                }
                                catch (Exception ex)
                                {
                                    Utils.Trace(ex, "OPCUAClientAdaptor.LoadPublishedData API" + ex.Message);
                                }
                                m_publishedDataDefinition.Children.Add(m_publishedDataSetItemDefinition);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "OPCUAClientAdaptor.LoadPublishedData API" + ex.Message);
                }
            }
            return m_publishedDataSetDefinition;
        }

        /// <summary>
        /// Method to add published dataSet.
        /// </summary>
        /// <param name="PublisherName"></param>
        /// <param name="VariableListDefinitionCollection"></param>
        /// <returns></returns>
        public PublishedDataSetBase AddPublishedDataSet(string publisherName, ObservableCollection<PublishedDataSetItemDefinition> variableListDefinitionCollection)
        {
            PublishedDataSetBase _PublishedDataSetBase = null;
            try
            {
                List<string> fieldNameAliases = new List<string>();
                List<bool>  promotedFields = new List<bool>();
                List<PublishedVariableDataType>  variablesToAdd = new List<PublishedVariableDataType>();
                foreach (PublishedDataSetItemDefinition  publishedDataSetItemDefinition in variableListDefinitionCollection)
                {
                     fieldNameAliases.Add(publishedDataSetItemDefinition.Name);
                     promotedFields.Add(false);
                    PublishedVariableDataType publishedVariableDataType = new PublishedVariableDataType
                    {
                        AttributeId = publishedDataSetItemDefinition.Attribute,
                        DeadbandType = publishedDataSetItemDefinition.DeadbandType,
                        DeadbandValue = publishedDataSetItemDefinition.DeadbandValue,
                        IndexRange = publishedDataSetItemDefinition.Indexrange,
                        MetaDataProperties = publishedDataSetItemDefinition.FieldMetaDataProperties,
                        PublishedVariable = publishedDataSetItemDefinition.PublishVariableNodeId,
                        SamplingIntervalHint = publishedDataSetItemDefinition.SamplingInterval,
                        SubstituteValue = publishedDataSetItemDefinition.SubstituteValue
                    };

                    variablesToAdd.Add(publishedVariableDataType);
                }
                IList<object> lstResponse = Session.Call(Constants.PublishedDataSetsNodeId,
                    Constants.AddPublishedDataSetsNodeId, new object[] { publisherName, fieldNameAliases, promotedFields, variablesToAdd });

                NodeId publisherId = lstResponse[0] as NodeId;
                return LoadPublishedData(publisherName, publisherId);

            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.AddPublishedDataSet API" + ex.Message);
            }
            return _PublishedDataSetBase;
        }

        /// <summary>
        /// Method to add variable to selected publisher.
        /// </summary>
        /// <param name="publisherName"></param>
        /// <param name="PublisherNodeId"></param>
        /// <param name="configurationVersionDataType"></param>
        /// <param name="VariableListDefinitionCollection"></param>
        /// <param name="NewConfigurationVersion"></param>
        /// <returns></returns>
        public string AddVariableToPublisher(string publisherName, NodeId PublisherNodeId, ConfigurationVersionDataType configurationVersionDataType, ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection, out ConfigurationVersionDataType NewConfigurationVersion)
        {
            string errorMessage = string.Empty;
            NewConfigurationVersion = null;
            try
            {
                List<string> fieldNameAliases = new List<string>();
                List<bool> promotedFields = new List<bool>();
                List<PublishedVariableDataType> variablesToAdd = new List<PublishedVariableDataType>();
                foreach (PublishedDataSetItemDefinition publishedDataSetItemDefinition in VariableListDefinitionCollection)
                {
                    try
                    {
                        fieldNameAliases.Add(publishedDataSetItemDefinition.Name);
                        promotedFields.Add(false);
                        PublishedVariableDataType publishedVariableDataType = new PublishedVariableDataType
                        {
                            AttributeId = publishedDataSetItemDefinition.Attribute,
                            DeadbandType = publishedDataSetItemDefinition.DeadbandType,
                            DeadbandValue = publishedDataSetItemDefinition.DeadbandValue,
                            IndexRange = publishedDataSetItemDefinition.Indexrange,
                            MetaDataProperties = publishedDataSetItemDefinition.FieldMetaDataProperties,
                            PublishedVariable = new NodeId(publishedDataSetItemDefinition.Name, 1),
                            SamplingIntervalHint = publishedDataSetItemDefinition.SamplingInterval,
                            SubstituteValue = publishedDataSetItemDefinition.SubstituteValue
                        };

                        variablesToAdd.Add(publishedVariableDataType);
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace(ex, "OPCUAClientAdaptor.AddVariableToPublisher API" + ex.Message);
                    }

                }
                NodeId addVariableToPublisherNodeId = new NodeId(string.Format("PubSub.DataSets.{0}.AddVariables", publisherName), 1);
                IList<object> lstResponse = Session.Call(PublisherNodeId,
                       addVariableToPublisherNodeId, new object[] { configurationVersionDataType, fieldNameAliases, promotedFields, variablesToAdd });
                NewConfigurationVersion = lstResponse[0] as ConfigurationVersionDataType;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.AddVariableToPublisher API" + ex.Message);
                errorMessage = ex.Message;

            }

            return errorMessage;
        }

        /// <summary>
        /// Method to remove Published dataset.
        /// </summary>
        /// <param name="PublishedDataSetNodeId"></param>
        /// <returns></returns>
        public string RemovePublishedDataSet(NodeId PublishedDataSetNodeId)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishedDataSetsNodeId,
                  Constants.RemovePublishedDataSetsNodeId, new object[] { PublishedDataSetNodeId });
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.RemovePublishedDataSet API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to remove published dataset variables.
        /// </summary>
        /// <param name="PublisherName"></param>
        /// <param name="PublisherNodeId"></param>
        /// <param name="ConfigurationVersionDataType"></param>
        /// <param name="variableIndexs"></param>
        /// <param name="NewConfigurationVersion"></param>
        /// <returns></returns>
        public string RemovePublishedDataSetVariables(string PublisherName, NodeId PublisherNodeId, ConfigurationVersionDataType ConfigurationVersionDataType, List<UInt32> variableIndexs, out ConfigurationVersionDataType NewConfigurationVersion)
        {
            string errorMessage = string.Empty;
            NewConfigurationVersion = null;
            try
            {
                NodeId RemoveVariables_PublisherNodeId = new NodeId(string.Format("PubSub.DataSets.{0}.RemoveVariables", PublisherName), 1);
                IList<object> lstResponse = Session.Call(PublisherNodeId,
                  RemoveVariables_PublisherNodeId, new object[] { ConfigurationVersionDataType, variableIndexs });
                NewConfigurationVersion = lstResponse[0] as ConfigurationVersionDataType;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.RemovePublishedDataSetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to add Target Variables.
        /// </summary>
        /// <param name="dataSetReaderNodeId"></param>
        /// <param name="minorVersion"></param>
        /// <param name="majorVersion"></param>
        /// <param name="variableListDefinitionCollection"></param>
        /// <returns></returns>
        public string AddTargetVariables(NodeId dataSetReaderNodeId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection)
        {
            string errorMessage = string.Empty;
            try
            {
                ConfigurationVersionDataType configurationVersionDataType = new ConfigurationVersionDataType() { MinorVersion = minorVersion, MajorVersion = majorVersion };
                FieldTargetDataTypeCollection fieldTargetDataTypeCollection = new FieldTargetDataTypeCollection();
                foreach (FieldTargetVariableDefinition fieldTargetVariableDefinition in variableListDefinitionCollection)
                {
                    FieldTargetDataType fieldTargetDataType = new FieldTargetDataType
                    {
                        AttributeId = fieldTargetVariableDefinition.AttributeId,
                        DataSetFieldId = new Uuid(new Guid(fieldTargetVariableDefinition.DataSetFieldId)),
                        OverrideValue = new Variant(fieldTargetVariableDefinition.OverrideValue),
                        OverrideValueHandling = fieldTargetVariableDefinition.OverrideValueHandling == 0 ? OverrideValueHandling.Disabled : fieldTargetVariableDefinition.OverrideValueHandling == 1 ? OverrideValueHandling.LastUseableValue : OverrideValueHandling.OverrideValue,
                        ReceiverIndexRange = fieldTargetVariableDefinition.ReceiverIndexRange,
                        TargetNodeId = fieldTargetVariableDefinition.TargetNodeId,
                        WriteIndexRange = fieldTargetVariableDefinition.WriteIndexRange
                    };
                    fieldTargetDataTypeCollection.Add(fieldTargetDataType);
                }
                NodeId addTargetVariablesMethodNodeId = new NodeId(dataSetReaderNodeId.Identifier.ToString() + ".CreateTargetVariables", 1);
                IList<object> lstResponse = Session.Call(dataSetReaderNodeId,
                  addTargetVariablesMethodNodeId, new object[] { configurationVersionDataType, fieldTargetDataTypeCollection });

            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.AddTargetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to add DataSetMirror to DataSetReader
        /// </summary>
        /// <param name="_DataSetReaderDefinition"></param>
        /// <param name="parentName"></param>
        /// <returns></returns>
        public string AddDataSetMirror(DataSetReaderDefinition _DataSetReaderDefinition, string parentName)
        {
            string errMsg = string.Empty;
            NodeId methodId = new NodeId(_DataSetReaderDefinition.DataSetReaderNodeId.Identifier + ".CreateDataSetMirror", 1);
            try
            {
                List<RolePermissionType> lstRolepermission = new List<RolePermissionType>();
                IList<object> lstResponse = Session.Call(_DataSetReaderDefinition.DataSetReaderNodeId,
                     methodId, new object[] { parentName, lstRolepermission });
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.AddDataSetMirror API" + ex.Message);
                errMsg = ex.Message;
            }
            return errMsg;
        }

        /// <summary>
        /// Method to add additional TargetVariables.
        /// </summary>
        /// <param name="ObjectId"></param>
        /// <param name="minorVersion"></param>
        /// <param name="majorVersion"></param>
        /// <param name="variableListDefinitionCollection"></param>
        /// <returns></returns>
        public string AddAdditionalTargetVariables(NodeId ObjectId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection)
        {
            string errorMessage = string.Empty;
            try
            {
                ConfigurationVersionDataType configurationVersionDataType = new ConfigurationVersionDataType() { MinorVersion = minorVersion, MajorVersion = majorVersion };
                FieldTargetDataTypeCollection fieldTargetDataTypeCollection = new FieldTargetDataTypeCollection();
                foreach (FieldTargetVariableDefinition fieldTargetVariableDefinition in variableListDefinitionCollection)
                {
                    FieldTargetDataType fieldTargetDataType = new FieldTargetDataType
                    {
                        AttributeId = fieldTargetVariableDefinition.AttributeId,
                        DataSetFieldId = new Uuid(new Guid(fieldTargetVariableDefinition.DataSetFieldId)),
                        OverrideValue = new Variant(fieldTargetVariableDefinition.OverrideValue),
                        OverrideValueHandling = fieldTargetVariableDefinition.OverrideValueHandling == 0 ? OverrideValueHandling.Disabled : fieldTargetVariableDefinition.OverrideValueHandling == 1 ? OverrideValueHandling.LastUseableValue : OverrideValueHandling.OverrideValue,
                        ReceiverIndexRange = fieldTargetVariableDefinition.ReceiverIndexRange,
                        TargetNodeId = fieldTargetVariableDefinition.TargetNodeId,
                        WriteIndexRange = fieldTargetVariableDefinition.WriteIndexRange
                    };
                    fieldTargetDataTypeCollection.Add(fieldTargetDataType);
                }
                NodeId addTargetVariablesMethodNodeId = new NodeId(ObjectId.Identifier.ToString() + ".AddTargetVariables", 1);
                IList<object> lstResponse = Session.Call(ObjectId,
                  addTargetVariablesMethodNodeId, new object[] { configurationVersionDataType, fieldTargetDataTypeCollection });
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.AddAdditionalTargetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }

        /// <summary>
        /// Method to remove target variable.
        /// </summary>
        /// <param name="ObjectId"></param>
        /// <param name="VersionType"></param>
        /// <param name="TargetsToremove"></param>
        /// <returns></returns>
        public string RemoveTargetVariable(NodeId ObjectId, ConfigurationVersionDataType VersionType, List<UInt32> TargetsToremove)
        {
            string errmsg = string.Empty;
            try
            {
                NodeId methodId = new NodeId(ObjectId.Identifier.ToString() + "." + "RemoveTargetVariables", 1);
                IList<object> lstResponse = Session.Call(ObjectId,
                 methodId, new object[] { VersionType, TargetsToremove });
            }
            catch (Exception e)
            {
                Utils.Trace(e, "OPCUAClientAdaptor.RemoveTargetVariable API" + e.Message);
                errmsg = e.Message;
            }
            return errmsg;
        }

        /// <summary>
        /// Method to write value for node.
        /// </summary>
        /// <param name="writeValueCollection"></param>
        /// <returns></returns>
        public StatusCodeCollection WriteValue(WriteValueCollection writeValueCollection)
        {
            StatusCodeCollection results = new StatusCodeCollection();
            try
            {
                Session.Write(new RequestHeader(), writeValueCollection, out results, out DiagnosticInfoCollection diagnosticsInfo);
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.WriteValue API" + ex.Message);
            }
            return results;
        }

        /// <summary>
        /// Method to read value for node
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public object ReadValue(NodeId nodeId)
        {
            try
            {
                DataValue datavalue = Session.ReadValue(nodeId);
                if (datavalue != null)
                {
                    return datavalue.Value;
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "OPCUAClientAdaptor.ReadValue API" + ex.Message);
            }
            return null;
        }
        #endregion

        #region Public Fields
        public BrowseNodeControl BrowserNodeControl;
        #endregion

        #region INotifyPropertyChanged Members

        private PropertyChangedEventHandler _handler;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add { _handler += value; }
            remove
            {
                // ReSharper disable once DelegateSubtraction
                if (_handler != null) _handler -= value;
            }
        }

        protected void OnPropertyChanged(string info)
        {
            _handler?.Invoke(this, new PropertyChangedEventArgs(info));
        }


        #endregion
    }

    /// <summary>
    ///  Class for Constants declaration
    /// </summary>
    public static class Constants
    {
        #region Private fields
        private static Dictionary<string, NodeId> m_dicNodeId = new Dictionary<string, NodeId>();
        #endregion

        #region Public Method
        public static void Initialize()
        {

        }
        #endregion

        #region Public Constructor
        static Constants()
        {
            //"SecurityGroupAddMethodId", "SecurityGroupRemoveMethodId", "SecurityGroups", "SecurityGroupNameTypeDefinitionId", "SecurityKeysObjectId", "SetSecurityKeysMethodId", "GetSecurityKeysMethodId", "PubSubStateNodeId", "PubSubStateObjectId", "PubSubStateEnableMethodId", "PubSubStateDisableMethodId",
            //"PublishSubscribeObjectId","BrokerConnectionMethodId","UADPConnectionMethodId","RemoveConnectionMethodId","UADPTypeDefinitionNodeId","AMQPTypeDefinitionNodeId",
            foreach (string nodeId in ConfigurationManager.AppSettings.Keys)
            {
                string id = ConfigurationManager.AppSettings[nodeId];
                if (id != null)
                {

                    string[] stringarray = id.Split(';');
                    string namespaceIdentifier = stringarray[1];
                    ushort namespaceIndex = Convert.ToUInt16(namespaceIdentifier.Split('=')[1]);
                    string identifier = stringarray[0];
                    string[] identifierarray = identifier.Split('=');
                    if (identifierarray[0] == "i")
                    {
                        m_dicNodeId[nodeId] = new NodeId(Convert.ToUInt16(identifierarray[1]), namespaceIndex);

                    }
                    else
                    {
                        m_dicNodeId[nodeId] = new NodeId(identifierarray[1], namespaceIndex);
                    }


                }
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Unique Security Group Object ID 
        /// </summary>
        public static NodeId SecurityGroupObjectId
        {
            get
            {
                return m_dicNodeId["SecurityGroupObjectId"];
                //return new NodeId(15443, 0);
            }
        }
        /// <summary>
        /// Unique Security Group Method ID
        /// </summary>
        public static NodeId SecurityGroupAddMethodId
        {
            get
            {
                return m_dicNodeId["SecurityGroupAddMethodId"];

            }
        }
        /// <summary>
        /// Unique Security Group Method Remove ID
        /// </summary>
        public static NodeId SecurityGroupRemoveMethodId
        {
            get
            {
                return m_dicNodeId["SecurityGroupRemoveMethodId"];

            }
        }
        /// <summary>
        /// Security Groups
        /// </summary>
        public static NodeId SecurityGroups
        {
            get
            {
                return m_dicNodeId["SecurityGroups"];

            }
        }
        /// <summary>
        /// Security Group Type Definition ID
        /// </summary>
        public static NodeId SecurityGroupNameTypeDefinitionId
        {
            get
            {
                return m_dicNodeId["SecurityGroupNameTypeDefinitionId"];

            }
        }
        /// <summary>
        /// Security key Object ID
        /// </summary>
        public static NodeId SecurityKeysObjectId
        {
            get
            {
                return m_dicNodeId["SecurityKeysObjectId"];

            }
        }
        /// <summary>
        /// Security key set method ID
        /// </summary>
        public static NodeId SetSecurityKeysMethodId
        {
            get
            {
                return m_dicNodeId["SetSecurityKeysMethodId"];

            }
        }
        /// <summary>
        /// Security key get method ID
        /// </summary>
        public static NodeId GetSecurityKeysMethodId
        {
            get
            {
                return m_dicNodeId["GetSecurityKeysMethodId"];

            }
        }
        /// <summary>
        /// Pub Sub state node ID
        /// </summary>
        public static NodeId PubSubStateNodeId
        {
            get
            {
                return m_dicNodeId["PubSubStateNodeId"];

            }
        }
        /// <summary>
        /// Pub Sub State Object ID
        /// </summary>
        public static NodeId PubSubStateObjectId
        {
            get
            {
                return m_dicNodeId["PubSubStateObjectId"];

            }
        }
        /// <summary>
        /// Pub Sub State Enabled Method ID
        /// </summary>
        public static NodeId PubSubStateEnableMethodId
        {
            get
            {
                return m_dicNodeId["PubSubStateEnableMethodId"];

            }
        }
        /// <summary>
        /// Pu Sub State Disable Method ID
        /// </summary>
        public static NodeId PubSubStateDisableMethodId
        {
            get
            {
                return m_dicNodeId["PubSubStateDisableMethodId"];

            }
        }
        /// <summary>
        /// Publish Subscriber Object ID
        /// </summary>
        public static NodeId PublishSubscribeObjectId
        {
            get
            {
                return m_dicNodeId["PublishSubscribeObjectId"];

            }
        }
        /// <summary>
        /// Broker Connection Method ID
        /// </summary>
        public static NodeId BrokerConnectionMethodId
        {
            get
            {
                return m_dicNodeId["BrokerConnectionMethodId"];

            }
        }
        /// <summary>
        /// UADP Connection Method ID
        /// </summary>
        public static NodeId UADPConnectionMethodId
        {
            get
            {
                return m_dicNodeId["UADPConnectionMethodId"];

            }
        }
        /// <summary>
        /// Remove Connection Method ID
        /// </summary>
        public static NodeId RemoveConnectionMethodId
        {
            get
            {
                return m_dicNodeId["RemoveConnectionMethodId"];

            }
        }
        /// <summary>
        /// UADP Type Definition Node ID
        /// </summary>
        public static NodeId UADPTypeDefinitionNodeId
        {
            get
            {
                return m_dicNodeId["UADPTypeDefinitionNodeId"];

            }
        }
        /// <summary>
        /// AMQP type Definition Node ID
        /// </summary>
        public static NodeId AMQPTypeDefinitionNodeId
        {
            get
            {
                return m_dicNodeId["AMQPTypeDefinitionNodeId"];

            }
        }
        /// <summary>
        /// Connection Type Definition Node Id
        /// </summary>
        public static NodeId ConnectionTypeDefinitionNodeId
        {
            get
            {
                return m_dicNodeId["ConnectionTypeDefinitionNodeId"];

            }
        }
        /// <summary>
        /// Property Node ID
        /// </summary>
        public static NodeId PropertyNodeId
        {
            get
            {
                return m_dicNodeId["PropertyNodeId"];

            }
        }
        /// <summary>
        /// Writer Group Type ID
        /// </summary>
        public static NodeId WriterGroupTypeId
        {
            get
            {
                return m_dicNodeId["WriterGroupTypeId"];

            }
        }
        /// <summary>
        /// Reader Group Type ID
        /// </summary>
        public static NodeId ReaderGroupTypeId
        {
            get
            {
                return m_dicNodeId["ReaderGroupTypeId"];

            }
        }
        /// <summary>
        /// Data Set Writer Type ID
        /// </summary>
        public static NodeId DataSetWriterTypeId
        {
            get
            {
                return m_dicNodeId["DataSetWriterTypeId"];

            }
        }
        /// <summary>
        /// Pub Sub Type Defintion ID
        /// </summary>
        public static NodeId PubSubTypeDefinitionId
        {
            get
            {
                return m_dicNodeId["PubSubTypeDefinitionId"];

            }
        }
        /// <summary>
        /// Published Data Sets Node ID
        /// </summary>
        public static NodeId PublishedDataSetsNodeId
        {
            get
            {
                return m_dicNodeId["PublishedDataSetsNodeId"];

            }
        }
        /// <summary>
        /// Added Published Data Set Node ID
        /// </summary>
        public static NodeId AddPublishedDataSetsNodeId
        {
            get
            {
                return m_dicNodeId["AddPublishedDataSetsNodeId"];

            }
        }
        /// <summary>
        /// Removed Published Data Set Node ID
        /// </summary>
        public static NodeId RemovePublishedDataSetsNodeId
        {
            get
            {
                return m_dicNodeId["RemovePublishedDataSetsNodeId"];

            }
        }
        /// <summary>
        /// Removing group ID
        /// </summary>
        public static NodeId RemoveGroupID
        {
            get
            {
                return m_dicNodeId["RemoveGroupID"];

            }
        }
        /// <summary>
        /// Writer to DataSet Type Definition ID
        /// </summary>
        public static NodeId WriterToDataSetTypeDefinition
        {
            get
            {
                return m_dicNodeId["WriterToDataSetTypeDefinition"];

            }
        }
        /// <summary>
        /// Data set Reader Type ID
        /// </summary>
        public static NodeId DataSetReaderTypeId
        {
            get
            {
                return m_dicNodeId["DataSetReaderTypeId"];

            }
        }
        /// <summary>
        /// Transport Settings Type Definition ID
        /// </summary>
        public static NodeId TransPortSettingsTypeDefinition
        {
            get
            {
                return m_dicNodeId["TransPortSettingsTypeDefinition"];

            }
        }
        /// <summary>
        /// Pub Sub State Type ID
        /// </summary>
        public static NodeId PubSubStateTypeId
        {
            get
            {
                return m_dicNodeId["PubSubStateTypeId"];

            }
        }
        /// <summary>
        /// Base Data Variable Type
        /// </summary>
        public static NodeId BaseDataVariableType
        {
            get
            {
                return m_dicNodeId["BaseDataVariableType"];

            }
        }

        #endregion
    }

}
