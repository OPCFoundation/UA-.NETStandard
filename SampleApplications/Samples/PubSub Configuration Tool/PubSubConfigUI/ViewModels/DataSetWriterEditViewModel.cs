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

using System.Windows;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for Data set writer view
    /// </summary>
    public class DataSetWriterEditViewModel : BaseViewModel
    {
        #region Private Fields 

        private int m_dataSetContentMask;
        private int m_dataSetWriterId;
        private string m_dataSetWriterName;
        private uint m_keyFrameCount;
        private int m_maxMessageSize;
        private string m_metadataQueueName;
        private int m_metadataUpdateTime;
        private string m_publisherDataSetId;
        private NodeId m_publisherDataSetNodeId;
        private string m_queueName;
        private int m_transportSetting = 0;
        private int m_messageSetting = 0;
        private string m_resourceUri;
        private string m_authenticationProfileUri;
        private int m_requestedDeliveryGuarantee;
        private int m_configuredSize;
        private int m_dataSetOffset;
        private ushort m_networkMessageNumber;
        private int m_uadpdataSetMessageContentMask;
        private int m_jsondataSetMessageContentMask;
        private string m_datasetName;

        private Visibility m_isDatagramTransport = Visibility.Visible;
        private Visibility m_isBrokerTransport = Visibility.Collapsed;
        private Visibility m_isDatagramMessage = Visibility.Visible;
        private Visibility m_isBrokerMessage = Visibility.Collapsed;
        
        #endregion

        #region Public Properties


        public int ConfiguredSize
        {
            get { return m_configuredSize; }
            set { m_configuredSize = value; OnPropertyChanged("ConfiguredSize"); }
        }
        public int DataSetOffset
        {
            get { return m_dataSetOffset; }
            set { m_dataSetOffset = value; OnPropertyChanged("DataSetOffset"); }
        }
        public ushort NetworkMessageNumber
        {
            get { return m_networkMessageNumber; }
            set { m_networkMessageNumber = value; OnPropertyChanged("NetworkMessageNumber"); }
        }
        public string ResourceUri
        {
            get { return m_resourceUri; }
            set { m_resourceUri = value; OnPropertyChanged("ResourceUri"); }
        }
        public string AuthenticationProfileUri
        {
            get { return m_authenticationProfileUri; }
            set { m_authenticationProfileUri = value; OnPropertyChanged("AuthenticationProfileUri"); }
        }
        public int RequestedDeliveryGuarantee
        {
            get { return m_requestedDeliveryGuarantee; }
            set { m_requestedDeliveryGuarantee = value; OnPropertyChanged("RequestedDeliveryGuarantee"); }
        }
        public Visibility IsDatagramTransport
        {
            get
            {
                return m_isDatagramTransport;
            }
            set
            {
                m_isDatagramTransport = value;
                OnPropertyChanged("IsDatagramTransport");
            }
        }
        public Visibility IsBrokerTransport
        {
            get
            {
                return m_isBrokerTransport;
            }
            set
            {
                m_isBrokerTransport = value;
                OnPropertyChanged("IsBrokerTransport");
            }
        }
        public Visibility IsDatagramMessage
        {
            get
            {
                return m_isDatagramMessage;
            }
            set
            {
                m_isDatagramMessage = value;
                OnPropertyChanged("IsDatagramMessage");
            }
        }
        public Visibility IsBrokerMessage
        {
            get
            {
                return m_isBrokerMessage;
            }
            set
            {
                m_isBrokerMessage = value;
                OnPropertyChanged("IsBrokerMessage");
            }
        }
        
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public int TransportSetting
        {
            get { return m_transportSetting; }
            set
            {
                m_transportSetting = value;
                OnPropertyChanged("TransportSetting");
            }
        }
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public int MessageSetting
        {
            get { return m_messageSetting; }
            set
            {
                m_messageSetting = value;
                OnPropertyChanged("MessageSetting");
            }
        }
        /// <summary>
        /// defines data set writer name
        /// </summary>
        public string DataSetWriterName
        {
            get { return m_dataSetWriterName; }
            set
            {
                m_dataSetWriterName = value;
                OnPropertyChanged("DataSetWriterName");
            }
        }
        /// <summary>
        /// defines publisher data set ID
        /// </summary>
        public string PublisherDataSetId
        {
            get
            {
                if (m_publisherDataSetNodeId != null) return m_publisherDataSetNodeId.ToString();
                return m_publisherDataSetId;
            }
            set
            {
                m_publisherDataSetId = value;
                OnPropertyChanged("PublisherDataSetId");
            }
        }
        /// <summary>
        /// defines publisher data set node ID
        /// </summary>
        public NodeId PublisherDataSetNodeId
        {
            get { return m_publisherDataSetNodeId; }
            set
            {
                m_publisherDataSetNodeId = value;
                OnPropertyChanged("PublisherDataSetNodeId");
            }
        }
        /// <summary>
        /// defines key frame count for target definition
        /// </summary>
        public uint KeyFrameCount
        {
            get { return m_keyFrameCount; }
            set
            {
                m_keyFrameCount = value;
                OnPropertyChanged("KeyFrameCount");
            }
        }
        /// <summary>
        /// defines data set writer ID
        /// </summary>
        public int DataSetWriterId
        {
            get { return m_dataSetWriterId; }
            set
            {
                m_dataSetWriterId = value;
                OnPropertyChanged("DataSetWriterId");
            }
        }
        /// <summary>
        /// defines queue name foe data set writer
        /// </summary>
        public string QueueName
        {
            get { return m_queueName; }
            set
            {
                m_queueName = value;
                OnPropertyChanged("QueueName");
            }
        }
        /// <summary>
        /// defines metadata queue name of data set writer
        /// </summary>
        public string MetadataQueueName
        {
            get { return m_metadataQueueName; }
            set
            {
                m_metadataQueueName = value;
                OnPropertyChanged("MetadataQueueName");
            }
        }
        /// <summary>
        /// defines metadata updata time of target definition
        /// </summary>
        public int MetadataUpdataTime
        {
            get { return m_metadataUpdateTime; }
            set
            {
                m_metadataUpdateTime = value;
                OnPropertyChanged("MetadataUpdataTime");
            }
        }
        /// <summary>
        /// defines maximum message size of target definition
        /// </summary>
        public int MaxMessageSize
        {
            get { return m_maxMessageSize; }
            set
            {
                m_maxMessageSize = value;
                OnPropertyChanged("MaxMessageSize");
            }
        }
        /// <summary>
        /// defines Dataset cotent mask of target defnition
        /// </summary>
        public int DataSetContentMask
        {
            get { return m_dataSetContentMask; }
            set
            {
                m_dataSetContentMask = value;
                OnPropertyChanged("DataSetContentMask");
            }
        }
        /// <summary>
        /// defines the DataSet Message Content Mask
        /// </summary>
        public int UadpDataSetMessageContentMask
        {
            get { return m_uadpdataSetMessageContentMask; }
            set
            {
                m_uadpdataSetMessageContentMask = value;
                OnPropertyChanged("UadpDataSetMessageContentMask");
            }
        }
        /// <summary>
        /// defines the DataSet Message Content Mask
        /// </summary>
        public int JsonDataSetMessageContentMask
        {
            get { return m_jsondataSetMessageContentMask; }
            set
            {
                m_jsondataSetMessageContentMask = value;
                OnPropertyChanged("BrokerDataSetMessageContentMask");
            }
        }
        public string DataSetName
        {
            get
            {
                return m_datasetName;
            }
            set
            {
                m_datasetName = value;
                OnPropertyChanged("DataSetName");
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Initialize method
        /// 
        /// </summary>
        public void Initialize()
        {
            DataSetWriterName = DataSetWriterDefinition.DataSetWriterName;
            PublisherDataSetNodeId = DataSetWriterDefinition.PublisherDataSetNodeId;
            PublisherDataSetId = PublisherDataSetNodeId.Identifier.ToString();
            DataSetName = DataSetWriterDefinition.DataSetName;

            KeyFrameCount = DataSetWriterDefinition.KeyFrameCount;
            DataSetWriterId = DataSetWriterDefinition.DataSetWriterId;
            DataSetContentMask = DataSetWriterDefinition.DataSetContentMask;
            QueueName = DataSetWriterDefinition.QueueName;

            //  MaxMessageSize = DataSetWriterDefinition.MaxMessageSize;

            TransportSetting = DataSetWriterDefinition.TransportSetting;
            MessageSetting = DataSetWriterDefinition.MessageSetting;

            if (TransportSetting == 1)
            {
                QueueName = DataSetWriterDefinition.QueueName;
                MetadataQueueName = DataSetWriterDefinition.MetadataQueueName;
                MetadataUpdataTime = DataSetWriterDefinition.MetadataUpdataTime;
                ResourceUri = DataSetWriterDefinition.ResourceUri;
                RequestedDeliveryGuarantee = DataSetWriterDefinition.RequestedDeliveryGuarantee;
                AuthenticationProfileUri = DataSetWriterDefinition.AuthenticationProfileUri;
            }
            if (MessageSetting == 0)
            {
                ConfiguredSize = DataSetWriterDefinition.ConfiguredSize;
                NetworkMessageNumber = DataSetWriterDefinition.NetworkMessageNumber;
                DataSetOffset = DataSetWriterDefinition.DataSetOffset;
                UadpDataSetMessageContentMask = DataSetWriterDefinition.UadpDataSetMessageContentMask;
            }
            else
            {
                JsonDataSetMessageContentMask = DataSetWriterDefinition.JsonDataSetMessageContentMask;
            }


        }

        #endregion

        #region Public Member
        public DataSetWriterDefinition DataSetWriterDefinition;
        #endregion
    }
}