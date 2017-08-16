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
    public class OPCUAClientAdaptor : IOPCUAClientAdaptor, INotifyPropertyChanged
    {
        #region Private Members
        private int m_reconnectPeriod = 10;
        private SessionReconnectHandler _mReconnectHandler;
        private ApplicationConfiguration _configuration;
        // Session _ClientSession = null;       
        private ServiceMessageContext _messageContext;
        private ConfiguredEndpointCollection _configuredEndpointCollection;
        private bool _isOpened;
        private static long sessionCounter;
        private readonly object _lock = new object();
        private bool _sessionStatus = false;
        


        public Session Session
        {
            get;
            set;
        }

        public string SelectedEndpoint
        {
            get;
            set;
        }
        public BrowseNodeControl _browserNodeControl;

        bool _refreshOnReconnection = false;
        public bool RefreshOnReconnection
        {
            get
            {
                return _refreshOnReconnection;
            }
            set
            {
                _refreshOnReconnection = value;
                OnPropertyChanged("RefreshOnReconnection");
            }
        }
        bool _activateTabsonReConnection = false;
        public bool ActivateTabsonReConnection
        {
            get
            {
                return _activateTabsonReConnection;
            }
            set
            {
                _activateTabsonReConnection = value;
                OnPropertyChanged("ActivateTabsonReConnection");
            }
        }
        #endregion

        public OPCUAClientAdaptor()
        {
            Constants.Initialize();
            LoadApplicationInstance();
        }
      
        public string AddNewSecurityGroup(string name, out SecurityGroup securityGroup)
        {
            string errorMessage = string.Empty;
              securityGroup = null;
            try
            {
                     
                IList<object> lstResponse = Session.Call(Constants.SecurityGroupObjectId,
                    Constants.SecurityGroupAddMethodId, new object[] {name});
                securityGroup = new SecurityGroup();
                securityGroup.GroupName = name;

                securityGroup.SecurityGroupId = lstResponse[0].ToString();
                securityGroup.GroupNodeId = lstResponse[1] as NodeId;
                NLogManager.Log.Info(String.Format("AddNewSecurityGroup API with name {0} was Successfull", name)); 
            } 
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }

        public string AddAMQPConnection(Connection connection,out NodeId connectionId)
        {
            string errorMessage = string.Empty;
            
            connectionId = null;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishSubscribeObjectId,
                    Constants.BrokerConnectionMethodId, new object[] { connection.Name,connection.Address,connection.PublisherId});
                 
                connectionId = lstResponse[0] as NodeId;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }
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

        public string RemoveConnection(NodeId connectionId)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishSubscribeObjectId,
                    Constants.RemoveConnectionMethodId, new object[] { connectionId });

            }
            //catch (ServiceResultException e)
            //{
            //    errorMessage = e.LocalizedText.Text;
            //}
            catch (Exception e)
            {
                errorMessage = e.Message;
            }
            return errorMessage;
        }
         
        public ObservableCollection<SecurityGroup> GetSecurityGroups()
        {
            ObservableCollection<SecurityGroup> securityGroups = new ObservableCollection<SecurityGroup>();
            if (Session != null && !Session.KeepAliveStopped)
            {
                try
                {
                    ReferenceDescriptionCollection referenceDescriptionCollection = _browserNodeControl.Browser.Browse(Constants.SecurityGroups);

                    foreach (ReferenceDescription referenceDescription in referenceDescriptionCollection)
                    {
                        if (referenceDescription.TypeDefinition == Constants.SecurityGroupNameTypeDefinitionId)
                        {
                            try
                            {
                                ReferenceDescriptionCollection refDescriptionCollection = _browserNodeControl.Browser.Browse((NodeId)referenceDescription.NodeId);
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
                            catch(Exception ex)
                            {
                                NLogManager.Log.Error("OPCUAClientAdaptor.GetSecurityGroups API" + ex.Message);
                            }

                        }
                    }
                }
                catch(Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.GetSecurityGroups API" + ex.Message);
                }

            }
            

            return securityGroups;
        }

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
                        Connection connection = new Connection();
                        connection.ConnectionNodeId = (NodeId)_ReferenceDescription.NodeId;
                        connection.Name = _ReferenceDescription.DisplayName.Text;
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
                                          Type type=  value.GetType();
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
                                            //  UInt16.TryParse(value.ToString(), out PublisherId);
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
                                                        new DataSetReaderDefinition();
                                                    dataSetReaderDefinition.DataSetReaderNodeId =
                                                        (NodeId) refDesc.NodeId;
                                                    dataSetReaderDefinition.Name =
                                                        dataSetReaderDefinition.DataSetReaderName =
                                                            refDesc.DisplayName.Text;
                                                    dataSetReaderDefinition.ParentNode = readerGroup;
                                                    ReferenceDescriptionCollection
                                                        refDescriptionDataSetReaderCollection =
                                                            Browse((NodeId) refDesc.NodeId);


                                                    foreach (ReferenceDescription refDsrDesc in
                                                        refDescriptionDataSetReaderCollection)
                                                    {
                                                        if (refDsrDesc.BrowseName.Name == "TransportSettings")
                                                        {
                                                            ReferenceDescriptionCollection
                                                                refDescriptionDataSetReaderGroupCollection =
                                                                    Browse((NodeId) refDsrDesc.NodeId);


                                                            foreach (ReferenceDescription refDsDesc in
                                                                refDescriptionDataSetReaderGroupCollection)
                                                            {
                                                                if (refDsDesc.BrowseName.Name ==
                                                                    "DataSetMessageContentMask")
                                                                {
                                                                    dataSetReaderDefinition.DataSetContentMask =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "DataSetWriterId")
                                                                {
                                                                    dataSetReaderDefinition.DataSetWriterId =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name ==
                                                                         "NetworkMessageContentMask")
                                                                {
                                                                    dataSetReaderDefinition.NetworkMessageContentMask =
                                                                        Convert.ToInt32(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "PublisherId")
                                                                {
                                                                    dataSetReaderDefinition.PublisherId =
                                                                        Convert.ToString(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                                else if (refDsDesc.BrowseName.Name ==
                                                                         "PublishingInterval")
                                                                {
                                                                    dataSetReaderDefinition.PublishingInterval =
                                                                        Convert.ToDouble(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
                                                                            .Value);
                                                                }
                                                            }
                                                        }
                                                        if (refDsrDesc.BrowseName.Name == "SubscribedDataSet")
                                                        {
                                                            ReferenceDescriptionCollection
                                                                refDescriptionDataSetReaderGroupCollection =
                                                                    Browse((NodeId) refDsrDesc.NodeId);


                                                            foreach (ReferenceDescription refDsDesc in
                                                                refDescriptionDataSetReaderGroupCollection)
                                                            {
                                                                if (refDsDesc.BrowseName.Name == "MessageReceiveTimeout"
                                                                )
                                                                {
                                                                    dataSetReaderDefinition.MessageReceiveTimeOut =
                                                                        Convert.ToDouble(Session
                                                                            .ReadValue((NodeId) refDsDesc.NodeId)
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
                                                                    catch(Exception ex)
                                                                    {

                                                                    }
                                                                }
                                                                else if (refDsDesc.BrowseName.Name == "TargetVariables")
                                                                {
                                                                    SubscribedDataSetDefinition
                                                                        subscribedDataSetDefinition =
                                                                            new SubscribedDataSetDefinition();
                                                                    subscribedDataSetDefinition.Name =
                                                                        "SubscribedDataSet";
                                                                    subscribedDataSetDefinition.ParentNode =
                                                                        dataSetReaderDefinition;

                                                                    var fields =
                                                                        Session.ReadValue((NodeId) refDsDesc.NodeId);
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
                                                                                new FieldTargetVariableDefinition();
                                                                        fieldTargetVariableDefinition.ParentNode =
                                                                            subscribedDataSetDefinition;
                                                                        fieldTargetVariableDefinition.AttributeId =
                                                                            fieldTargetDataType.AttributeId;
                                                                        fieldTargetVariableDefinition.DataSetFieldId =
                                                                            fieldTargetDataType.DataSetFieldId
                                                                                .GuidString;
                                                                        fieldTargetVariableDefinition.Name =
                                                                            fieldTargetDataType.TargetNodeId.Identifier
                                                                                .ToString();
                                                                        fieldTargetVariableDefinition.OverrideValue =
                                                                            fieldTargetDataType.OverrideValue;
                                                                        fieldTargetVariableDefinition
                                                                                .OverrideValueHandling =
                                                                            (int) fieldTargetDataType
                                                                                .OverrideValueHandling;
                                                                        fieldTargetVariableDefinition
                                                                            .ReceiverIndexRange = fieldTargetDataType
                                                                            .ReceiverIndexRange;
                                                                        fieldTargetVariableDefinition
                                                                            .TargetFieldNodeId = fieldTargetDataType
                                                                            .TargetNodeId;
                                                                        fieldTargetVariableDefinition.TargetNodeId =
                                                                            fieldTargetDataType.TargetNodeId
                                                                                .ToString();
                                                                        fieldTargetVariableDefinition.WriteIndexRange =
                                                                            fieldTargetDataType.WriteIndexRange;
                                                                        fieldTargetVariableDefinition
                                                                            .FieldTargetDataType = fieldTargetDataType;
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
                                                                            new MirrorSubscribedDataSetDefinition();
                                                                    mirrorSubscribedDataSetDefinition.Name =
                                                                        "SubscribedDataSet";
                                                                    mirrorSubscribedDataSetDefinition.ParentNode =
                                                                        dataSetReaderDefinition;
                                                                    ReferenceDescriptionCollection refDesCollection =
                                                                        Browse((NodeId) refDsDesc.NodeId);
                                                                    foreach (ReferenceDescription _RefDesc in
                                                                        refDesCollection)
                                                                    {
                                                                        MirrorVariableDefinition
                                                                            mirrorVariableDefinition =
                                                                                new MirrorVariableDefinition();
                                                                        mirrorVariableDefinition.Name = _RefDesc
                                                                            .DisplayName.Text;
                                                                        mirrorVariableDefinition.ParentNode =
                                                                            mirrorSubscribedDataSetDefinition;
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
                                                            Session.ReadValue((NodeId) refDesc.NodeId).Value.ToString();
                                                    }
                                                    if (refDesc.BrowseName.Name == "SecurityMode")
                                                    {
                                                        readerGroup.SecurityMode =
                                                            Convert.ToInt32(Session.ReadValue((NodeId) refDesc.NodeId)
                                                                .Value);
                                                    }
                                                    if (refDesc.BrowseName.Name == "MaxNetworkMessageSize")
                                                    {
                                                        readerGroup.MaxNetworkMessageSize =
                                                            Convert.ToInt32(Session.ReadValue((NodeId) refDesc.NodeId)
                                                                .Value);
                                                    }
                                                    if (refDesc.BrowseName.Name == "QueueName")
                                                    {
                                                        readerGroup.QueueName =
                                                            Session.ReadValue((NodeId) refDesc.NodeId).Value.ToString();
                                                    }
                                                }


                                            }
                                            catch (Exception ex)
                                            {
                                                NLogManager.Log.Error("ClientAdaptor:GetPubSubConfiguration API" +  ex.Message);
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
                                                                catch(Exception ex)
                                                                {
                                                                    NLogManager.Log.Error("ClientAdaptor:GetPubSubConfiguration API"+ ex.Message);
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
                                                        // dataSetWriterGroup.SecurityGroupId = !string.IsNullOrWhiteSpace(val.ToString()) ? Convert.ToInt32(val) : 0;
                                                        dataSetWriterGroup.SecurityGroupId = val.ToString();

                                                    }

                                                }
                                            }
                                            catch(Exception ex)
                                            {
                                                NLogManager.Log.Error("ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                                            }
                                        }



                                        connection.Children.Add((dataSetWriterGroup));
                                    }
                                }
                                catch(Exception ex)
                                {
                                    NLogManager.Log.Error("ClientAdaptor:GetPubSubConfiguration API" +  ex.Message);
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
                        NLogManager.Log.Error("ClientAdaptor:GetPubSubConfiguration API" + ex.Message);
                    }
                }
            }
            return pubSubConfiguation;
        }
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

        public string RemoveGroup(Connection connection,NodeId groupId)
        { 
            string errorMessage = string.Empty;
            NodeId connectionId = connection.ConnectionNodeId;
          
            string removeGroupId = string.Format("{0}.{1}.{2}", "PubSub", connection.Name, "RemoveGroup");

            NodeId removeGroupNodeId = new NodeId(removeGroupId,1);


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

        public string RemoveDataSetWriter(DataSetWriterGroup dataSetWriterGroup, NodeId writerNodeId)
        {
            string errorMessage = string.Empty;
            Connection connection = dataSetWriterGroup.ParentNode as Connection;
            NodeId dataSetWriterGroupId = dataSetWriterGroup.GroupId;
            if ( connection != null )
            {
                string removeDataSetWriter = string.Format("{0}.{1}.{2}.{3}", "PubSub", connection.Name, dataSetWriterGroup.Name,"RemoveDataSetWriter");
                NodeId removeDataSetWriterNodeId = new NodeId(removeDataSetWriter,1);
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

        public string RemoveDataSetReader(ReaderGroupDefinition readerGroupDefinition, NodeId readerNodeId)
        {
            string errorMessage = string.Empty;
            Connection connection = readerGroupDefinition.ParentNode as Connection;
            NodeId readerGroupDefinitionNodeId = readerGroupDefinition.GroupId;
            if ( connection != null )
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


        //public bool UpdateSecurityGroup(string name,NodeId nodeId)
        //{
        //    bool isSuccess = true;
        //    try
        //    {
        //        WriteValueCollection ValueCollection = new WriteValueCollection();
        //        WriteValue w_Value = new WriteValue();
        //        w_Value.NodeId = nodeId;
        //        w_Value.Value = new DataValue() { Value = new QualifiedName(name, nodeId.NamespaceIndex) };
        //        w_Value.AttributeId = Attributes.BrowseName;
        //        ValueCollection.Add(w_Value);

        //        w_Value = new WriteValue();
        //        w_Value.NodeId = nodeId;
        //        w_Value.Value = new DataValue() { Value = new LocalizedText(name) };
        //        w_Value.AttributeId = Attributes.DisplayName;
        //        ValueCollection.Add(w_Value);
        //        StatusCodeCollection Results;
        //        DiagnosticInfoCollection diagnosticInfos;
        //        Session.Write(new RequestHeader(), ValueCollection, out Results, out diagnosticInfos);
        //        if(Results!=null)
        //        {

        //            foreach (StatusCode code in Results)
        //            {
        //                if(!StatusCode.IsGood(code))
        //                {
        //                    isSuccess = false;
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //    catch(Exception ex)
        //    {
        //        isSuccess = false;
        //    }
        //    return isSuccess;
        //}
        public string SetSecurityKeys(SecurityKeys securityKeys)
        {
            string errorMessage = string.Empty;
            try
            {
                byte[] currentkey =Encoding.UTF8.GetBytes( securityKeys.CurrentKey);
                List<Byte[]> lstFeaturekeys = new List<byte[]>();
                foreach(string fKeys in securityKeys.FeatureKeys)
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
        public string GetSecurityKeys(string securityGroupId,uint FeatureKeyCount, out SecurityKeys securityKeys)
        {
            string errorMessage = string.Empty;
            securityKeys = new SecurityKeys();
            try
            {
                IList<object> lstResponse = Session.Call(Constants.SecurityKeysObjectId,
                    Constants.GetSecurityKeysMethodId, new object[] { securityGroupId, FeatureKeyCount });
                securityKeys.SecurityGroupId = securityGroupId;
                securityKeys.SecurityPolicyUri =Convert.ToString( lstResponse[0]);
                securityKeys.CurrentTokenId = Convert.ToUInt32(lstResponse[1]);
                securityKeys.CurrentKey = Encoding.UTF8.GetString(lstResponse[2] as Byte[]);
                var array = (lstResponse[3] as IEnumerable).Cast<object>().Select(x => x as byte[]).ToArray();
                foreach (var featurekeybytearray in array)
                {
                    securityKeys.FeatureKeys.Add(Encoding.UTF8.GetString(featurekeybytearray as Byte[]));
                }
                securityKeys.TimeToNextKey = Convert.ToDouble(lstResponse[4]);
                securityKeys.KeyLifetime= Convert.ToDouble(lstResponse[5]);
            }
            catch (ServiceResultException e)
            {
                errorMessage = e.LocalizedText.Text;
            }
            catch (Exception e)
            {
                NLogManager.Log.Error("ClientAdaptor:GetSecurityKeys API"+ e.Message);
            }
            return errorMessage;
        }

        public string EnablePubSubState(MonitorNode monitorNode)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(monitorNode.ParantNodeId,
                      monitorNode.EnableNodeId, new object[] { } ); 
            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("ClientAdaptor:EnablePubSubState API" + ex.Message);
            }
            return errorMessage;
        }
        public string DisablePubSubState(MonitorNode monitorNode)
        {
            string errorMessage = string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(monitorNode.ParantNodeId,
                    monitorNode.DisableNodeId, new object[] { });
            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("ClientAdaptor:DisablePubSubState API" + ex.Message);
            }
            return errorMessage;

        }

        public string AddWriterGroup(DataSetWriterGroup dataSetWriterGroup, out NodeId groupId)
        {
            string errorMessage = string.Empty;
            groupId = null;

            Connection connection = dataSetWriterGroup.ParentNode as Connection;
            if ( connection != null )
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
            else if(connection != null && connection.ConnectionType == 1)
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

        public string AddReaderGroup(ReaderGroupDefinition readerGroupDefinition, out NodeId groupId)
        {
            string errorMessage = string.Empty;
            groupId = null;

            Connection connection = readerGroupDefinition.ParentNode as Connection;
            if ( connection != null )
            {
                string name = connection.Name;
            }

            if ( connection != null )
            {
                NodeId connectionNodeId = connection.ConnectionNodeId;
                NodeId readerGroupNodeId = null;
                ReferenceDescriptionCollection referenceDescriptionReaderCollection = _browserNodeControl.Browser.Browse(connectionNodeId);
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

      public  string AddDataSetReader(NodeId dataSetReaderGroupNodeId, DataSetReaderDefinition dataSetReaderDefinition)
        {
            string retMsg = string.Empty;
            try
            {
                NodeId addDataSetReaderMethodNodeId = new NodeId(dataSetReaderGroupNodeId.Identifier.ToString() + ".AddDataSetReader",1);
                IList<object> lstResponse = Session.Call(dataSetReaderGroupNodeId,
                       addDataSetReaderMethodNodeId,
                       new object[]
                       {
                        dataSetReaderDefinition.DataSetReaderName,dataSetReaderDefinition.PublisherId,dataSetReaderDefinition.DataSetWriterId,dataSetReaderDefinition.DataSetMetaDataType,dataSetReaderDefinition.MessageReceiveTimeOut,dataSetReaderDefinition.NetworkMessageContentMask,dataSetReaderDefinition.DataSetContentMask,dataSetReaderDefinition.PublishingInterval

                       });

                dataSetReaderDefinition.DataSetReaderNodeId = lstResponse[0] as NodeId;
                dataSetReaderDefinition.MessageReceiveTimeOut = Convert.ToInt32(lstResponse[1]);
            }
            catch(Exception ex)
            {
                retMsg = ex.Message;

            }
            return retMsg;
        }
        public string AddUADPDataSetWriter(NodeId dataSetWriterGroupNodeId, DataSetWriterDefinition dataSetWriterDefinition, out NodeId writerNodeId,
            out int revisedKeyFrameCount)
        {
            string errorMessage = string.Empty;
            writerNodeId = null;
            revisedKeyFrameCount = 0;
         
            try
            {
               
                NodeId addDataSetWriterNodeId = new NodeId(dataSetWriterGroupNodeId.Identifier.ToString() + ".AddDataSetWriter",1);

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

        public string AddAMQPDataSetWriter(NodeId dataSetWriterGroupNodeId, DataSetWriterDefinition dataSetWriterDefinition, out NodeId writerNodeId,
            out int revisedMaxMessageSize)
        {
            string errorMessage = string.Empty;
            writerNodeId = null;
            revisedMaxMessageSize = 0;

             
                try
                {
                NodeId addDataSetWriterNodeId = new NodeId(dataSetWriterGroupNodeId.Identifier.ToString()+ ".AddDataSetwriter", 1);
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
        public Session Connect(string endpointUrl, out string errorMessage,  out TreeViewNode node)
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
                // SelectedEndpoint = endpoint.ToString();
                if (endpoint.UpdateBeforeConnect)
                {
                    ConfiguredServerDlg configuredServerDlg = new ConfiguredServerDlg();
                    endpoint = configuredServerDlg.ShowDialog(endpoint, _configuration);

                    if (endpoint == null)
                    {
                        errorMessage = configuredServerDlg.StatusText;
                        return null;
                    }
                    SelectedEndpoint = endpoint.ToString();


                }

                X509Certificate2  clientCertificate = null;
                if (endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
                {
                    if (_configuration.SecurityConfiguration.ApplicationCertificate == null)
                    {
                        errorMessage = "Application certificate is empty";
                        return null;
                    }

                    Task<X509Certificate2> taskclientCertificate =   _configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
                    if (taskclientCertificate == null)
                    {
                        errorMessage = "Couldn't able to find the client certificate";
                        return null;
                    }
                    clientCertificate = taskclientCertificate.Result as X509Certificate2;
                }
                
                var channel = SessionChannel.Create(_configuration, endpoint.Description, endpoint.Configuration,
                                                   clientCertificate, _messageContext);
                Session = new Session(channel, _configuration, endpoint, clientCertificate);
                Session.ReturnDiagnostics = DiagnosticsMasks.All;
                if (!new SessionOpenDlg().ShowDialog(Session, null))
                {
                    return null;
                }

                Session.KeepAliveInterval = 10000;
                Session.KeepAlive += Session_KeepAlive;
                errorMessage = string.Empty;

                _browserNodeControl = new BrowseNodeControl(Session);
                _browserNodeControl.InitializeBrowserView(BrowseViewType.Objects, null);

                node.IsRoot = true;
                _browserNodeControl.Browse(ref node);
            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.Connect API" + ex.Message);
            }
            //  channel = null; ToDO: Do we need to close the channel
            return Session;
        }

        public void Rebrowse(ref TreeViewNode node)
        {
            _browserNodeControl.Browse(ref node);
        }

        private ConfiguredEndpoint CreateConfiguredEndpoint(string serviceUrl)
        {
           
            //Check for security parameters appended to the URL
            string parameters = null;
            var index = serviceUrl.IndexOf("- [", StringComparison.Ordinal);
            if (index != -1)
            {
                parameters = serviceUrl.Substring(index + 3);
                serviceUrl = serviceUrl.Substring(0, index).Trim();
            }

            var useBinaryEncoding = true;
            if (!string.IsNullOrEmpty(parameters))
            {
                var fields = parameters.Split(new[] { '-', '[', ':', ']' }, StringSplitOptions.RemoveEmptyEntries);

                try
                {
                    if (fields.Length > 2) useBinaryEncoding = fields[2] == "Binary";
                    else useBinaryEncoding = false;
                }
                catch
                {
                    useBinaryEncoding = false;
                }
            }
            Uri uri = null;
            try
            {
                uri = new Uri(serviceUrl);
            }
            catch (Exception ex)
            {
                
                return null;
                ;
            }
            var description = new EndpointDescription();
            description.EndpointUrl = uri.ToString();
            description.SecurityMode = MessageSecurityMode.None;
            description.SecurityPolicyUri = SecurityPolicies.None;

            description.Server.ApplicationUri = Utils.UpdateInstanceUri(uri.ToString());
            description.Server.ApplicationName = uri.AbsolutePath;

            if (description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.UaTcpTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }
            else if (description.EndpointUrl.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
            {
                description.TransportProfileUri = Profiles.HttpsBinaryTransport;
                description.Server.DiscoveryUrls.Add(description.EndpointUrl);
            }
            //else
            //{
            //    description.TransportProfileUri = Profiles.WsHttpXmlOrBinaryTransport;
            //    description.Server.DiscoveryUrls.Add(description.EndpointUrl + "/discovery");
            //}

            var configuredEndpointCollection = new ConfiguredEndpointCollection();
            var endpoint = new ConfiguredEndpoint(configuredEndpointCollection, description, null);
            endpoint.Configuration.UseBinaryEncoding = useBinaryEncoding;
            endpoint.UpdateBeforeConnect = true;
            return endpoint;
        }

        //private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        //{
        //    Utils.Trace("OPC Session Alive.");
        //    if (e.CurrentState != ServerState.Running && session.KeepAliveStopped)
        //    {
        //        ServerStatus = e.Status.ToString();
        //        Reconnect();
        //    }
        //    else if (e.CurrentState == ServerState.Running)
        //    {
        //        ServerStatus = e.CurrentState.ToString();
        //    }
        //}

        //private void Reconnect()
        //{ 
        //    Session.Reconnect();
        //    if (!Session.KeepAliveStopped)
        //    {
        //        ServerStatus = ServerState.Running.ToString();
        //    }
        //}

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
                  
                    if (_mReconnectHandler == null)
                    {
                        ActivateTabsonReConnection = false;
                        RefreshOnReconnection = false;
                        _mReconnectHandler = new SessionReconnectHandler();
                        _mReconnectHandler.BeginReconnect(Session, m_reconnectPeriod * 1000, Server_ReconnectComplete);
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
                if (!Object.ReferenceEquals(sender, _mReconnectHandler))
                {
                    return;
                }

                Session = _mReconnectHandler.Session;
                Session.KeepAlive += Session_KeepAlive;
                _mReconnectHandler.Dispose();
                _mReconnectHandler = null;
                _browserNodeControl = new BrowseNodeControl(Session);
                _browserNodeControl.InitializeBrowserView(BrowseViewType.Objects, null);
                try
                {
                    RefreshOnReconnection = true;
                }
                catch(Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.Server_ReconnectComplete API" + ex.Message);
                }
                try
                {
                    ActivateTabsonReConnection = true;
                }
                catch(Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.Server_ReconnectComplete API" + ex.Message);
                }
                    
               

            }
            catch (Exception exception)
            {
                ClientUtils.HandleException("OPC UA PubSub Client",exception);
            }
        }

        private void UpdateStatus(bool error, DateTime time, string status, params object[] args)
        {
            ServerStatus = String.Format(status, args);
        }

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

            EndpointDescription selectedEndpoint = null;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (var client = DiscoveryClient.Create(uri, configuration))
            {
                var endpoints = client.GetEndpoints(null);

                // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
                for (var ii = 0; ii < endpoints.Count; ii++)
                {
                    var endpoint = endpoints[ii];

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
                        if (selectedEndpoint == null) selectedEndpoint = endpoint;

                        // The security level is a relative measure assigned by the server to the 
                        // endpoints that it returns. Clients should always pick the highest level
                        // unless they have a reason not too.
                        if (endpoint.SecurityLevel > selectedEndpoint.SecurityLevel) selectedEndpoint = endpoint;
                    }
                }

                // pick the first available endpoint by default.
                if (selectedEndpoint == null && endpoints.Count > 0) selectedEndpoint = endpoints[2];
            }

            // if a server is behind a firewall it may return URLs that are not accessible to the client.
            // This problem can be avoided by assuming that the domain in the URL used to call 
            // GetEndpoints can be used to access any of the endpoints. This code makes that conversion.
            // Note that the conversion only makes sense if discovery uses the same protocol as the endpoint.

            if ( selectedEndpoint != null )
            {
                var endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);

                if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
                {
                    var builder = new UriBuilder(endpointUrl);
                    builder.Host = uri.DnsSafeHost;
                    builder.Port = uri.Port;
                    selectedEndpoint.EndpointUrl = builder.ToString();
                }
            }

            // return the selected endpoint.
            return selectedEndpoint;
        }

        public bool Disconnect()
        {
            if (Session != null)
            {
                try
                {
                    if (Session.Subscriptions != null)
                    {
                        List<Subscription> subscriptionCollection= Session.Subscriptions.ToList();
                        foreach (var item in subscriptionCollection)
                        {
                            DeleteSubscription(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.Disconnect API" + ex.Message);
                }
                try
                {
                    Session.Close();
                    return true;
                }
                catch (Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.Disconnect API" + ex.Message);
                }
            } 
            return false;
        }


        private bool CreateSession(Session session, IList<string> preferredLocales)
        {
            _isOpened = false;
            try
            {
                var identity = new UserIdentity();
                var sessionName = Utils.Format("MySession {0}", Utils.IncrementIdentifier(ref sessionCounter));

                if (session.ConfiguredEndpoint.SelectedUserTokenPolicy.TokenType == UserTokenType.Anonymous)
                    identity = new UserIdentity();

                else if (session.ConfiguredEndpoint.SelectedUserTokenPolicy.TokenType == UserTokenType.UserName)
                    if (session.ConfiguredEndpoint.UserIdentity != null)
                    {
                        if ((session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).Password != null &&
                             (session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).UserName != null)
                        {
                            var password =
                            Encoding.UTF8.GetString((session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken)
                                                     .Password);
                            var username = (session.ConfiguredEndpoint.UserIdentity as UserNameIdentityToken).UserName;
                            identity = new UserIdentity(username, password);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("UserName or Password is incorrect.", "OPC PubSub Configuration Tool",
                                             MessageBoxButton.OK, MessageBoxImage.Error);
                            _isOpened = false;
                            return _isOpened;
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("UserIdentity not Provided.", "OPC PubSub Configuration Tool", MessageBoxButton.OK,
                                         MessageBoxImage.Error);
                        _isOpened = false;
                        return _isOpened;
                    }

                object[] dataList = { session, sessionName, identity, preferredLocales };



                Open(dataList);
            }
            catch (NullReferenceException exe)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.CreateSession API" + exe.Message);
            }
            finally
            {

            }
            return _isOpened;
        }

        private void Open(object state)
        {
            var session = ((object[])state)[0] as Session;
            var sessionName = ((object[])state)[1] as string;
            var identity = ((object[])state)[2] as UserIdentity;
            var preferredLocales = ((object[])state)[3] as IList<string>;

            try
            {
                session.Open(sessionName, (uint)session.SessionTimeout, identity, preferredLocales);
                _isOpened = true;
            }
            catch (Exception exp)
            {
                System.Windows.MessageBox.Show(exp.Message, "OPC PubSub Configuration Tool", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public bool DeleteSubscription(Subscription subscription)
        {
            if (subscription != null)
            {
                Session.RemoveSubscription(subscription);
                return true;
            }
            return false;
        }
        public Subscription GetPubSubStateSubscription(string subscriptionName)
        {
            List<Subscription> SubscriptionCollection = Session.Subscriptions.ToList();
            Subscription subscription= SubscriptionCollection.Where(i => i.DisplayName == subscriptionName).FirstOrDefault();
            if(subscription!=null)
            {
                return subscription; 
                
            }
            return null;
        }
        public Subscription CreateSubscription(string subscriptionName, int subscriptionInterval)
        {
            Subscription subscription = new Subscription();
            subscription.DisplayName = subscriptionName;
            subscription.PublishingInterval = subscriptionInterval;
            subscription.PublishingEnabled = true;
            subscription.Priority = 0;
            subscription.KeepAliveCount = 1000;
            subscription.LifetimeCount = 1000;
            subscription.MaxNotificationsPerPublish = 1000;

            if (subscription.Created == false)
            {
                Session.AddSubscription(subscription);
                try
                {
                    subscription.Create();
                }
                catch (Exception exe)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.CreateSubscription API" + exe.Message);
                }
                //AddLog("New subscription " + "'" + subscriptionName + "'" + " created successfully.");
            }

            return subscription;
        }

         
        private void LoadApplicationInstance()
        {
            ApplicationInstance applicationInst = new ApplicationInstance();
            applicationInst.ApplicationName = "OPCUA_PubSub_Client";
            applicationInst.ApplicationType = ApplicationType.Client;
            applicationInst.ConfigSectionName = "OPCUAClient_PubSubConfig";

            try
            {
                applicationInst.LoadApplicationConfiguration(false).Wait(); 
                bool certOK = applicationInst.CheckApplicationInstanceCertificate(false, 0).Result;
                if (!certOK)
                {
                    //log
                    throw new Exception("Application instance certificate invalid!");
                }
            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.LoadApplicationInstance API" + ex.Message);
            }

            //ApplicationInstance.SetUaValidationForHttps(applicationInst
            //                                             .ApplicationConfiguration.CertificateValidator);
            // ApplicationTitle = ApplicationInst.ApplicationName;

            _configuration = applicationInst.ApplicationConfiguration;
            _configuredEndpointCollection = _configuration.LoadCachedEndpoints(true);
            _configuredEndpointCollection.DiscoveryUrls = _configuration.ClientConfiguration.WellKnownDiscoveryUrls;
            _messageContext = _configuration.CreateMessageContext();

            if (!_configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                _configuration.CertificateValidator.CertificateValidation += OnCertificateValidation;
        }

        private void OnCertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            e.Accept = true;
        }

        public ReferenceDescriptionCollection Browse(NodeId nodeId)
        {
            ReferenceDescriptionCollection _ReferenceDescriptionCollection = null;
            if (_browserNodeControl != null)
            {
                _ReferenceDescriptionCollection = _browserNodeControl.Browser.Browse(nodeId);
            }
            return _ReferenceDescriptionCollection;
        }
        public ReferenceDescriptionCollection Browse(NodeId nodeId, BrowseDirection Direction )
        {
            ReferenceDescriptionCollection _ReferenceDescriptionCollection = null;
            if (_browserNodeControl != null)
            {
                _browserNodeControl.Browser.BrowseDirection = Direction;
                _ReferenceDescriptionCollection = _browserNodeControl.Browser.Browse(nodeId);
                _browserNodeControl.Browser.BrowseDirection = BrowseDirection.Forward;
            }
            return _ReferenceDescriptionCollection;
        }


        public  ObservableCollection<PublishedDataSetBase> GetPublishedDataSets()
        {
            ObservableCollection<PublishedDataSetBase> PublishedDataSetBaseCollection = new ObservableCollection<PublishedDataSetBase>();
            ReferenceDescriptionCollection _ReferenceDescriptionCollection=  Browse(Constants.PublishedDataSetsNodeId);
            foreach(ReferenceDescription _ReferenceDescription in _ReferenceDescriptionCollection)
            {
                if (_ReferenceDescription.TypeDefinition == ObjectTypeIds.PublishedDataItemsType)
                { 
                    PublishedDataSetBaseCollection.Add(LoadPublishedData(_ReferenceDescription.DisplayName.Text,(NodeId)_ReferenceDescription.NodeId));
                }
            }
            return PublishedDataSetBaseCollection;
        }
 
        private PublishedDataSetDefinition LoadPublishedData(string PublisherName,NodeId PublisherNodeId)
        {
            PublishedDataSetDefinition _PublishedDataSetDefinition = new PublishedDataSetDefinition();
            _PublishedDataSetDefinition.Name = PublisherName;
            _PublishedDataSetDefinition.PublishedDataSetNodeId = (NodeId)PublisherNodeId;
            DataSetMetaDataDefinition _DataSetMetaDataDefinition = new DataSetMetaDataDefinition(_PublishedDataSetDefinition) { Name = "DataSetMetaData" };
            PublishedDataDefinition _PublishedDataDefinition = new PublishedDataDefinition(_PublishedDataSetDefinition) { Name = "PublishedData" };
            _PublishedDataSetDefinition.Children.Add(_DataSetMetaDataDefinition);
            _PublishedDataSetDefinition.Children.Add(_PublishedDataDefinition);
            ReferenceDescriptionCollection _RefDescriptionCollection = Browse(PublisherNodeId);
            foreach (ReferenceDescription _RefDescription in _RefDescriptionCollection)
            {
                try
                {
                    if (_RefDescription.BrowseName.Name == "ConfigurationVersion")
                    {
                        ConfigurationVersionDataType _ConfigurationVersionDataType = (ConfigurationVersionDataType)Session.ReadValue((NodeId)_RefDescription.NodeId, typeof(ConfigurationVersionDataType));
                        if (_ConfigurationVersionDataType != null)
                        {
                            _PublishedDataSetDefinition.ConfigurationVersionDataType = _ConfigurationVersionDataType;
                        }
                    }
                    else if (_RefDescription.BrowseName.Name == "DataSetMetaData")
                    {
                        try
                        {
                            DataSetMetaDataType _DataSetMetaDataType = (DataSetMetaDataType)Session.ReadValue((NodeId)_RefDescription.NodeId, typeof(DataSetMetaDataType));
                            if (_DataSetMetaDataType != null)
                            {
                                _DataSetMetaDataDefinition.DataSetMetaDataType = _DataSetMetaDataType;
                            }
                        }
                        catch(Exception ex)
                        {

                        }
                    }
                    else if (_RefDescription.BrowseName.Name == "PublishedData")
                    {
                        DataValue datavalue = Session.ReadValue((NodeId)_RefDescription.NodeId);
                        if (datavalue == null || datavalue.Value == null)
                        {
                            continue;
                        }
                        ExtensionObject[] ExtensionObjectArray = datavalue.Value as ExtensionObject[];
                        PublishedVariableDataTypeCollection PublishedVariableDataTypeArray = new PublishedVariableDataTypeCollection();
                        foreach (ExtensionObject ex_object in ExtensionObjectArray)
                        {
                            PublishedVariableDataTypeArray.Add(ex_object.Body as PublishedVariableDataType);
                        }
                        if (PublishedVariableDataTypeArray != null)
                        {
                            int count = 0;
                            List<FieldMetaData> LstfieldmetaData = _DataSetMetaDataDefinition.DataSetMetaDataType.Fields.ToList();
                            foreach (PublishedVariableDataType _PublishedVariableDataType in PublishedVariableDataTypeArray)
                            {
                                PublishedDataSetItemDefinition _PublishedDataSetItemDefinition = new PublishedDataSetItemDefinition(_PublishedDataDefinition) { PublishedVariableDataType = _PublishedVariableDataType };

                                try
                                {
                                     _PublishedDataSetItemDefinition.PublishVariableNodeId = _PublishedVariableDataType.PublishedVariable;
                                    _PublishedDataSetItemDefinition.Name = _PublishedVariableDataType.PublishedVariable.Identifier.ToString();
                                    _PublishedDataSetItemDefinition.Attribute = _PublishedVariableDataType.AttributeId;
                                    _PublishedDataSetItemDefinition.SamplingInterval = _PublishedVariableDataType.SamplingIntervalHint;
                                    _PublishedDataSetItemDefinition.DeadbandType = _PublishedVariableDataType.DeadbandType;
                                    _PublishedDataSetItemDefinition.DeadbandValue = _PublishedVariableDataType.DeadbandValue;
                                    _PublishedDataSetItemDefinition.Indexrange = _PublishedVariableDataType.IndexRange;
                                    _PublishedDataSetItemDefinition.SubstituteValue = _PublishedVariableDataType.SubstituteValue;
                                    _PublishedDataSetItemDefinition.FieldMetaDataProperties = _PublishedVariableDataType.MetaDataProperties;
                                   
                                }
                                catch(Exception ex)
                                {
                                    NLogManager.Log.Error("OPCUAClientAdaptor.LoadPublishedData API" + ex.Message);
                                }
                                _PublishedDataDefinition.Children.Add(_PublishedDataSetItemDefinition);
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    NLogManager.Log.Error("OPCUAClientAdaptor.LoadPublishedData API" + ex.Message);
                }
            }
            return _PublishedDataSetDefinition;
        }
     public PublishedDataSetBase AddPublishedDataSet(string PublisherName, ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection)
        {
            PublishedDataSetBase _PublishedDataSetBase = null;
            try
            {
                List<string> FieldNameAliases = new List<string>();
                List<bool> PromotedFields = new List<bool>();
                List<PublishedVariableDataType> VariablesToAdd = new List<PublishedVariableDataType>();
                foreach(PublishedDataSetItemDefinition _PublishedDataSetItemDefinition in VariableListDefinitionCollection)
                { 
                    FieldNameAliases.Add(_PublishedDataSetItemDefinition.Name);
                    PromotedFields.Add(false);
                    PublishedVariableDataType _PublishedVariableDataType = new PublishedVariableDataType();
                    _PublishedVariableDataType.AttributeId = _PublishedDataSetItemDefinition.Attribute;
                    _PublishedVariableDataType.DeadbandType = _PublishedDataSetItemDefinition.DeadbandType;
                    _PublishedVariableDataType.DeadbandValue = _PublishedDataSetItemDefinition.DeadbandValue;
                    _PublishedVariableDataType.IndexRange = _PublishedDataSetItemDefinition.Indexrange;
                    _PublishedVariableDataType.MetaDataProperties = _PublishedDataSetItemDefinition.FieldMetaDataProperties;
                    _PublishedVariableDataType.PublishedVariable = _PublishedDataSetItemDefinition.PublishVariableNodeId;
                    _PublishedVariableDataType.SamplingIntervalHint = _PublishedDataSetItemDefinition.SamplingInterval;
                    _PublishedVariableDataType.SubstituteValue = _PublishedDataSetItemDefinition.SubstituteValue;
                     
                    VariablesToAdd.Add(_PublishedVariableDataType);
                }
                IList<object> lstResponse = Session.Call(Constants.PublishedDataSetsNodeId,
                    Constants.AddPublishedDataSetsNodeId, new object[] { PublisherName, FieldNameAliases, PromotedFields, VariablesToAdd });

              NodeId PublisherId=  lstResponse[0] as NodeId;
               return LoadPublishedData(PublisherName, PublisherId);

            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.AddPublishedDataSet API" + ex.Message);
            }
            return _PublishedDataSetBase;
        }

      public  string AddVariableToPublisher(string publisherName, NodeId PublisherNodeId, ConfigurationVersionDataType configurationVersionDataType, ObservableCollection<PublishedDataSetItemDefinition> VariableListDefinitionCollection, out ConfigurationVersionDataType NewConfigurationVersion)
        {
            string errorMessage = string.Empty;
            NewConfigurationVersion = null;
            try
            {
                List<string> FieldNameAliases = new List<string>();
                List<bool> promotedFields = new List<bool>();
                List<PublishedVariableDataType> variablesToAdd = new List<PublishedVariableDataType>();
                foreach (PublishedDataSetItemDefinition publishedDataSetItemDefinition in VariableListDefinitionCollection)
                {
                    try
                    {
                        FieldNameAliases.Add(publishedDataSetItemDefinition.Name);
                        promotedFields.Add(false);
                        PublishedVariableDataType publishedVariableDataType = new PublishedVariableDataType();
                        publishedVariableDataType.AttributeId = publishedDataSetItemDefinition.Attribute;
                        publishedVariableDataType.DeadbandType = publishedDataSetItemDefinition.DeadbandType;
                        publishedVariableDataType.DeadbandValue = publishedDataSetItemDefinition.DeadbandValue;
                        publishedVariableDataType.IndexRange = publishedDataSetItemDefinition.Indexrange;
                        publishedVariableDataType.MetaDataProperties = publishedDataSetItemDefinition.FieldMetaDataProperties;
                        publishedVariableDataType.PublishedVariable = new NodeId(publishedDataSetItemDefinition.Name, 1);
                        publishedVariableDataType.SamplingIntervalHint = publishedDataSetItemDefinition.SamplingInterval;
                        publishedVariableDataType.SubstituteValue = publishedDataSetItemDefinition.SubstituteValue;

                        variablesToAdd.Add(publishedVariableDataType);
                    }
                    catch (Exception ex)
                    {
                        NLogManager.Log.Error("OPCUAClientAdaptor.AddVariableToPublisher API" + ex.Message);
                    }

                }
                NodeId AddVariableToPublisherNodeId = new NodeId(string.Format("PubSub.DataSets.{0}.AddVariables",publisherName), 1);
                IList<object> lstResponse = Session.Call(PublisherNodeId,
                       AddVariableToPublisherNodeId, new object[] { configurationVersionDataType,FieldNameAliases, promotedFields, variablesToAdd });
                NewConfigurationVersion = lstResponse[0] as ConfigurationVersionDataType;
            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.AddVariableToPublisher API" + ex.Message);
                errorMessage = ex.Message;

            }

            return errorMessage;
        }
        public string RemovePublishedDataSet(NodeId PublishedDataSetNodeId)
        {
            string errorMessage=string.Empty;
            try
            {
                IList<object> lstResponse = Session.Call(Constants.PublishedDataSetsNodeId,
                  Constants.RemovePublishedDataSetsNodeId, new object[] { PublishedDataSetNodeId });

            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.RemovePublishedDataSet API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }
        public string RemovePublishedDataSetVariables(string PublisherName,NodeId PublisherNodeId, ConfigurationVersionDataType ConfigurationVersionDataType, List<UInt32> variableIndexs,out  ConfigurationVersionDataType NewConfigurationVersion)
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
                NLogManager.Log.Error("OPCUAClientAdaptor.RemovePublishedDataSetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }
       public string AddTargetVariables(NodeId dataSetReaderNodeId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection)
        {
            string errorMessage = string.Empty;
           
            try
            {
                ConfigurationVersionDataType _ConfigurationVersionDataType = new ConfigurationVersionDataType() { MinorVersion = minorVersion, MajorVersion = majorVersion };
                FieldTargetDataTypeCollection _FieldTargetDataTypeCollection = new FieldTargetDataTypeCollection();
                 foreach(FieldTargetVariableDefinition _FieldTargetVariableDefinition in variableListDefinitionCollection)
                {
                    FieldTargetDataType _FieldTargetDataType = new FieldTargetDataType();
                    _FieldTargetDataType.AttributeId = _FieldTargetVariableDefinition.AttributeId;
                    _FieldTargetDataType.DataSetFieldId =new Uuid(new Guid( _FieldTargetVariableDefinition.DataSetFieldId));
                    _FieldTargetDataType.OverrideValue = new Variant(_FieldTargetVariableDefinition.OverrideValue);
                    _FieldTargetDataType.OverrideValueHandling = _FieldTargetVariableDefinition.OverrideValueHandling==0? OverrideValueHandling.Disabled: _FieldTargetVariableDefinition.OverrideValueHandling ==1? OverrideValueHandling.LastUseableValue: OverrideValueHandling.OverrideValue;
                    _FieldTargetDataType.ReceiverIndexRange = _FieldTargetVariableDefinition.ReceiverIndexRange;
                    _FieldTargetDataType.TargetNodeId = _FieldTargetVariableDefinition.TargetNodeId;
                    _FieldTargetDataType.WriteIndexRange = _FieldTargetVariableDefinition.WriteIndexRange;
                    _FieldTargetDataTypeCollection.Add(_FieldTargetDataType);
                }
                  NodeId AddTargetVariablesMethodNodeId = new NodeId(dataSetReaderNodeId.Identifier.ToString() + ".CreateTargetVariables", 1);
                IList<object> lstResponse = Session.Call(dataSetReaderNodeId,
                  AddTargetVariablesMethodNodeId, new object[] { _ConfigurationVersionDataType, _FieldTargetDataTypeCollection });
               
            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.AddTargetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }
       public string AddDataSetMirror(DataSetReaderDefinition _DataSetReaderDefinition, string parentName)
        {
            string errMsg = string.Empty;
            NodeId MethodId = new NodeId(_DataSetReaderDefinition.DataSetReaderNodeId.Identifier + ".CreateDataSetMirror", 1);
            try
            {
                List<RolePermissionType> LstRolepermission = new List<RolePermissionType>();
                IList<object> lstResponse = Session.Call(_DataSetReaderDefinition.DataSetReaderNodeId,
                     MethodId, new object[] { parentName, LstRolepermission });
            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.AddDataSetMirror API" + ex.Message);
                errMsg = ex.Message;
            }
            return errMsg;
        }
        public string AddAdditionalTargetVariables(NodeId ObjectId, UInt16 minorVersion, UInt16 majorVersion, ObservableCollection<FieldTargetVariableDefinition> variableListDefinitionCollection)
        {
            string errorMessage = string.Empty;

            try
            {
                ConfigurationVersionDataType _ConfigurationVersionDataType = new ConfigurationVersionDataType() { MinorVersion = minorVersion, MajorVersion = majorVersion };
                FieldTargetDataTypeCollection _FieldTargetDataTypeCollection = new FieldTargetDataTypeCollection();
                foreach (FieldTargetVariableDefinition _FieldTargetVariableDefinition in variableListDefinitionCollection)
                {
                    FieldTargetDataType _FieldTargetDataType = new FieldTargetDataType();
                    _FieldTargetDataType.AttributeId = _FieldTargetVariableDefinition.AttributeId;
                    _FieldTargetDataType.DataSetFieldId = new Uuid(new Guid(_FieldTargetVariableDefinition.DataSetFieldId));
                    _FieldTargetDataType.OverrideValue = new Variant(_FieldTargetVariableDefinition.OverrideValue);
                    _FieldTargetDataType.OverrideValueHandling = _FieldTargetVariableDefinition.OverrideValueHandling == 0 ? OverrideValueHandling.Disabled : _FieldTargetVariableDefinition.OverrideValueHandling == 1 ? OverrideValueHandling.LastUseableValue : OverrideValueHandling.OverrideValue;
                    _FieldTargetDataType.ReceiverIndexRange = _FieldTargetVariableDefinition.ReceiverIndexRange;
                    _FieldTargetDataType.TargetNodeId = _FieldTargetVariableDefinition.TargetNodeId;
                    _FieldTargetDataType.WriteIndexRange = _FieldTargetVariableDefinition.WriteIndexRange;
                    _FieldTargetDataTypeCollection.Add(_FieldTargetDataType);
                }
                NodeId AddTargetVariablesMethodNodeId = new NodeId(ObjectId.Identifier.ToString() + ".AddTargetVariables", 1);
                IList<object> lstResponse = Session.Call(ObjectId,
                  AddTargetVariablesMethodNodeId, new object[] { _ConfigurationVersionDataType, _FieldTargetDataTypeCollection });

            }
            catch (Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.AddAdditionalTargetVariables API" + ex.Message);
                errorMessage = ex.Message;
            }
            return errorMessage;
        }
      public string  RemoveTargetVariable(NodeId ObjectId, ConfigurationVersionDataType VersionType, List<UInt32> TargetsToremove)
        {
            string errmsg = string.Empty;
            try
            {
                NodeId MethodId = new NodeId(ObjectId.Identifier.ToString() + "." + "RemoveTargetVariables",1);
                IList<object> lstResponse = Session.Call(ObjectId,
                 MethodId, new object[] { VersionType, TargetsToremove });

            }
            catch (Exception e)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.RemoveTargetVariable API" + e.Message);
                errmsg = e.Message;
            }
            return errmsg;
        }

      public  StatusCodeCollection WriteValue(WriteValueCollection writeValueCollection)
        {
            StatusCodeCollection results = new StatusCodeCollection();
            try
            {
                 
                DiagnosticInfoCollection diagnosticsInfo;
                Session.Write(new RequestHeader(), writeValueCollection, out results, out diagnosticsInfo);
               
             
            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.WriteValue API" + ex.Message);
            }
            return results;
             
        }
       public object ReadValue(NodeId nodeId)
        {
            try
            {
              DataValue datavalue=  Session.ReadValue(nodeId);
                if(datavalue!=null)
                {
                   return datavalue.Value;
                }
            }
            catch(Exception ex)
            {
                NLogManager.Log.Error("OPCUAClientAdaptor.ReadValue API" + ex.Message);
            }
            return null;
        }
        #region Public Members
        string _serverStatus =String.Empty;
        public string ServerStatus
        {
            get { return _serverStatus; }
            set
            {
                _serverStatus = value;
                OnPropertyChanged("ServerStatus");
            }
        }

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

    public static class Constants
    {
        public static Dictionary<string, NodeId> _dicNodeId = new Dictionary<string, NodeId>();

        public static void Initialize()
        {

        }
        static Constants()
        {
            //"SecurityGroupAddMethodId", "SecurityGroupRemoveMethodId", "SecurityGroups", "SecurityGroupNameTypeDefinitionId", "SecurityKeysObjectId", "SetSecurityKeysMethodId", "GetSecurityKeysMethodId", "PubSubStateNodeId", "PubSubStateObjectId", "PubSubStateEnableMethodId", "PubSubStateDisableMethodId",
            //"PublishSubscribeObjectId","BrokerConnectionMethodId","UADPConnectionMethodId","RemoveConnectionMethodId","UADPTypeDefinitionNodeId","AMQPTypeDefinitionNodeId",
            foreach (string nodeId in ConfigurationManager.AppSettings.Keys )
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
                        _dicNodeId[nodeId] = new NodeId(Convert.ToUInt16(identifierarray[1]), namespaceIndex);
                        
                    }
                    else
                    {
                        _dicNodeId[nodeId] = new NodeId(identifierarray[1], namespaceIndex);
                    }


                }
            }
        }
        public static NodeId SecurityGroupObjectId
        {
            get
            {
                return _dicNodeId["SecurityGroupObjectId"];
                //return new NodeId(15443, 0);
            }
        }
        public static NodeId SecurityGroupAddMethodId
        {
            get
            {
                return _dicNodeId["SecurityGroupAddMethodId"];
                
            }
        }
        public static NodeId SecurityGroupRemoveMethodId
        {
            get
            {
                return _dicNodeId["SecurityGroupRemoveMethodId"];
                
            }
        }
        public static NodeId SecurityGroups
        {
            get
            {
                return _dicNodeId["SecurityGroups"];
                
            }
        }
        public static NodeId SecurityGroupNameTypeDefinitionId
        {
            get
            {
                return _dicNodeId["SecurityGroupNameTypeDefinitionId"];

            }
        }
        public static NodeId SecurityKeysObjectId
        {
            get
            {
                return _dicNodeId["SecurityKeysObjectId"];

            }
        }
        public static NodeId SetSecurityKeysMethodId
        {
            get
            {
                return _dicNodeId["SetSecurityKeysMethodId"];

            }
        }
        public static NodeId GetSecurityKeysMethodId
        {
            get
            {
                return _dicNodeId["GetSecurityKeysMethodId"];

            }
        }
        public static NodeId PubSubStateNodeId
        {
            get
            {
                return _dicNodeId["PubSubStateNodeId"];

            }
        }
        public static NodeId PubSubStateObjectId
        {
            get
            {
                return _dicNodeId["PubSubStateObjectId"];

            }
        }
        public static NodeId PubSubStateEnableMethodId
        {
            get
            {
                return _dicNodeId["PubSubStateEnableMethodId"];

            }
        }
        public static NodeId PubSubStateDisableMethodId
        {
            get
            {
                return _dicNodeId["PubSubStateDisableMethodId"];

            }
        }
        public static NodeId PublishSubscribeObjectId
        {
            get
            {
                return _dicNodeId["PublishSubscribeObjectId"];

            }
        }
        public static NodeId BrokerConnectionMethodId
        {
            get
            {
                return _dicNodeId["BrokerConnectionMethodId"];

            }
        }
        public static NodeId UADPConnectionMethodId
        {
            get
            {
                return _dicNodeId["UADPConnectionMethodId"];

            }
        }
        public static NodeId RemoveConnectionMethodId
        {
            get
            {
                return _dicNodeId["RemoveConnectionMethodId"];

            }
        }
        public static NodeId UADPTypeDefinitionNodeId
        {
            get
            {
                return _dicNodeId["UADPTypeDefinitionNodeId"];

            }
        }
        public static NodeId AMQPTypeDefinitionNodeId
        {
            get
            {
                return _dicNodeId["AMQPTypeDefinitionNodeId"];

            }
        }
        public static NodeId ConnectionTypeDefinitionNodeId
        {
            get
            {
                return _dicNodeId["ConnectionTypeDefinitionNodeId"];

            }
        }
        public static NodeId PropertyNodeId
        {
            get
            {
                return _dicNodeId["PropertyNodeId"];

            }
        }
        public static NodeId WriterGroupTypeId
        {
            get
            {
                return _dicNodeId["WriterGroupTypeId"];

            }
        }
        public static NodeId ReaderGroupTypeId
        {
            get
            {
                return _dicNodeId["ReaderGroupTypeId"];

            }
        }
        public static NodeId DataSetWriterTypeId
        {
            get
            {
                return _dicNodeId["DataSetWriterTypeId"];

            }
        }
        public static NodeId PubSubTypeDefinitionId
        {
            get
            {
                return _dicNodeId["PubSubTypeDefinitionId"];

            }
        }
        public static NodeId PublishedDataSetsNodeId
        {
            get
            {
                return _dicNodeId["PublishedDataSetsNodeId"];

            }
        }
        public static NodeId AddPublishedDataSetsNodeId
        {
            get
            {
                return _dicNodeId["AddPublishedDataSetsNodeId"];

            }
        }
        public static NodeId RemovePublishedDataSetsNodeId
        {
            get
            {
                return _dicNodeId["RemovePublishedDataSetsNodeId"];

            }
        }
        public static NodeId RemoveGroupID
        {
            get
            {
                return _dicNodeId["RemoveGroupID"];

            }
        }
        public static NodeId WriterToDataSetTypeDefinition
        {
            get
            {
                return _dicNodeId["WriterToDataSetTypeDefinition"];

            }
        }
        public static NodeId DataSetReaderTypeId
        {
            get
            {
                return _dicNodeId["DataSetReaderTypeId"];

            }
        }
        public static NodeId TransPortSettingsTypeDefinition
        {
            get
            {
                return _dicNodeId["TransPortSettingsTypeDefinition"];

            }
        }
        public static NodeId PubSubStateTypeId
        {
            get
            {
                return _dicNodeId["PubSubStateTypeId"];

            }
        }
        public static NodeId BaseDataVariableType
        {
            get
            {
                return _dicNodeId["BaseDataVariableType"];

            }
        }


    }



}
