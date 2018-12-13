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
using PubSubBase.Definitions;
using System;
using System.Windows;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// View model definition for Data set reader
    /// </summary>
    public class DataSetReaderEditViewModel : BaseViewModel
    {
        #region Private Fields

        private string m_connectionPublisherId;
        private int m_dataSetContentMask;
        private string m_dataSetReaderName;
        private uint m_dataSetWriterId;
        private double m_messageReceiveTimeOut;
        private int m_networkMessageContentMask;
        private object m_publisherId;
        private double m_publishingInterval;
        private double m_processingOffset = 0;
        private double m_receiveOffset = 0;
        private double m_groupVersion;
        private string m_resourceUri;
        private string m_authenticationProfileUri;
        private int m_requestedDeliveryGuarantee;
        private int m_publisherType;
        private int m_securityMode;
        private uint m_NetworkMessageNumber = 0;
        private int m_UadpNetworkMessageContentMask;
        private int m_UadpDataSetMessageContentMask;
        private int m_JsonNetworkMessageContentMask;
        private int m_JsonDataSetMessageContentMask;
        private int m_DataSetFieldContentMask;
        private uint m_dataSetOffset;
        private Guid m_dataSetClassId;
        private int m_writerGroupId;
        private int m_transportSetting;
        private int m_messageSetting;
        private string m_queueName;
        private string m_metadataQueueName;
        private string m_securityGroupId = "0";
        private uint m_KeyFrameCount;
        private string m_HeaderLayoutUri;
        private DataSetMetaDataType m_dataSetMetaDataType = new DataSetMetaDataType();
        private Visibility m_isDatagramTransport = Visibility.Visible;
        private Visibility m_isBrokerTransport = Visibility.Collapsed;
        private Visibility m_isDatagramMessage = Visibility.Visible;
        private Visibility m_isBrokerMessage = Visibility.Collapsed;
        #endregion

        #region Public Properties
        public uint KeyFrameCount
        {
            get { return m_KeyFrameCount; }
            set { m_KeyFrameCount = value; OnPropertyChanged("KeyFrameCount"); }
        }
        public string HeaderLayoutUri
        {
            get { return m_HeaderLayoutUri; }
            set { m_HeaderLayoutUri = value; OnPropertyChanged("HeaderLayoutUri"); }
        }
        public int PublisherType
        {
            get { return m_publisherType; }
            set { m_publisherType = value; OnPropertyChanged("PublisherType"); }
        }
        
        public int SecurityMode
        {
            get { return m_securityMode; }
            set { m_securityMode = value; OnPropertyChanged("SecurityMode"); }
        }
        
        public uint NetworkMessageNumber
        {
            get { return m_NetworkMessageNumber; }
            set { m_NetworkMessageNumber = value; OnPropertyChanged("NetworkMessageNumber"); }
        }

        public double Receiveoffset
        {
            get { return m_receiveOffset; }
            set { m_receiveOffset = value; OnPropertyChanged("Receiveoffset"); }
        }

        public double ProcessingOffset
        {
            get { return m_processingOffset; }
            set { m_processingOffset = value; OnPropertyChanged("ProcessingOffset"); }
        }

        public int UadpNetworkMessageContentMask
        {
            get { return m_UadpNetworkMessageContentMask; }
            set { m_UadpNetworkMessageContentMask = value; OnPropertyChanged("UadpNetworkMessageContentMask"); }
        }

        public int UadpDataSetMessageContentMask
        {
            get { return m_UadpDataSetMessageContentMask; }
            set { m_UadpDataSetMessageContentMask = value; OnPropertyChanged("UadpDataSetMessageContentMask"); }
        }

        public int JsonNetworkMessageContentMask
        {
            get { return m_JsonNetworkMessageContentMask; }
            set { m_JsonNetworkMessageContentMask = value; OnPropertyChanged("JsonNetworkMessageContentMask"); }
        }

        public int JsonDataSetMessageContentMask
        {
            get { return m_JsonDataSetMessageContentMask; }
            set { m_JsonDataSetMessageContentMask = value; OnPropertyChanged("JsonDataSetMessageContentMask"); }
        }

        public int DataSetFieldContentMask
        {
            get { return m_DataSetFieldContentMask; }
            set { m_DataSetFieldContentMask = value; OnPropertyChanged("DataSetFieldContentMask"); }
        }

        public uint DataSetOffset
        {
            get { return m_dataSetOffset; }
            set { m_dataSetOffset = value; OnPropertyChanged("DataSetOffset"); }
        }

        public Guid DataSetClassId
        {
            get { return m_dataSetClassId; }
            set { m_dataSetClassId = value; OnPropertyChanged("DataSetClassId"); }
        }

        public double GroupVersion
        {
            get { return m_groupVersion; }
            set { m_groupVersion = value; OnPropertyChanged("GroupVersion"); }
        }

        /// <summary>
        /// defines data set reader name
        /// </summary>
        public string DataSetReaderName
        {
            get { return m_dataSetReaderName; }
            set
            {
                m_dataSetReaderName = value;
                OnPropertyChanged("DataSetReaderName");
            }
        }

        /// <summary>
        /// defines publisher id for Data ser reader
        /// </summary>
        public object PublisherId
        {
            get { return m_publisherId; }
            set
            {
                m_publisherId = value;
                OnPropertyChanged("PublisherId");
            }
        }

        /// <summary>
        /// defines connection publisher id for data set reader
        /// </summary>
        public string ConnectionPublisherId
        {
            get
            {
                if (PublisherId != null) PublisherId.ToString();
                return m_connectionPublisherId;
            }
            set
            {
                m_connectionPublisherId = value;
                OnPropertyChanged("ConnectionPublisherId");
            }
        }

        /// <summary>
        /// defines data set writer ID
        /// </summary>
        public uint DataSetWriterId
        {
            get { return m_dataSetWriterId; }
            set
            {
                m_dataSetWriterId = value;
                OnPropertyChanged("DataSetWriterId");
            }
        }

        /// <summary>
        /// defines data set writer ID
        /// </summary>
        public int WriterGroupId
        {
            get { return m_writerGroupId; }
            set
            {
                m_writerGroupId = value;
                OnPropertyChanged("WriterGroupId");
            }
        }

        /// <summary>
        /// defines dataSet meta data type
        /// </summary>
        public DataSetMetaDataType DataSetMetaDataType
        {
            get { return m_dataSetMetaDataType; }
            set
            {
                m_dataSetMetaDataType = value;
                OnPropertyChanged("DataSetMetaDataType");
            }
        }

        /// <summary>
        /// defines message receive timeout
        /// </summary>
        public double MessageReceiveTimeOut
        {
            get { return m_messageReceiveTimeOut; }
            set
            {
                m_messageReceiveTimeOut = value;
                OnPropertyChanged("MessageReceiveTimeOut");
            }
        }

        /// <summary>
        /// defines data set content mask
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
        /// defines network message content mask
        /// </summary>
        public int NetworkMessageContentMask
        {
            get { return m_networkMessageContentMask; }
            set
            {
                m_networkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

        /// <summary>
        /// defines publishing interval for data set reader
        /// </summary>
        public double PublishingInterval
        {
            get { return m_publishingInterval; }
            set
            {
                m_publishingInterval = value;
                OnPropertyChanged("PublishingInterval");
            }
        }

        public int TransportSetting
        {
            get { return m_transportSetting; }
            set { m_transportSetting = value; OnPropertyChanged("TransportSetting"); }
        }

        public int MessageSetting
        {
            get { return m_messageSetting; }
            set { m_messageSetting = value; OnPropertyChanged("MessageSetting"); }
        }

        public string QueueName
        {
            get { return m_queueName; }
            set { m_queueName = value; OnPropertyChanged("QueueName"); }
        }

        public string MetadataQueueName
        {
            get { return m_metadataQueueName; }
            set { m_metadataQueueName = value; OnPropertyChanged("MetadataQueueName"); }
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
        /// defines security group ID
        /// </summary>
        public string SecurityGroupId
        {
            get { return m_securityGroupId; }
            set
            {
                m_securityGroupId = value;
                OnPropertyChanged("SecurityGroupId");
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Initialise method
        /// </summary>
        public void Initialize()
        {
            DataSetReaderName = ReaderDefinition.DataSetReaderName;
            PublisherId = ReaderDefinition.PublisherId;
            if (PublisherId != null)
            {
                ConnectionPublisherId = PublisherId.ToString();
            }
            DataSetWriterId = ReaderDefinition.DataSetWriterId;
            WriterGroupId = ReaderDefinition.WriterGroupId;
            SecurityGroupId = ReaderDefinition.SecurityGroupId;
            DataSetMetaDataType = ReaderDefinition.DataSetMetaDataType;
            MessageReceiveTimeOut = ReaderDefinition.MessageReceiveTimeOut;
            DataSetContentMask = ReaderDefinition.DataSetContentMask;
            PublishingInterval = ReaderDefinition.PublishingInterval;
            NetworkMessageContentMask = ReaderDefinition.NetworkMessageContentMask;
            SecurityMode = ReaderDefinition.MessageSecurityMode;
            TransportSetting = ReaderDefinition.TransportSetting;
            MessageSetting = ReaderDefinition.MessageSetting;
            KeyFrameCount = ReaderDefinition.KeyFrameCount;
            HeaderLayoutUri = ReaderDefinition.HeaderLayoutUri;
            if (TransportSetting == 1)
            {
                QueueName = ReaderDefinition.QueueName;
                MetadataQueueName = ReaderDefinition.MetadataQueueName;
                ResourceUri = ReaderDefinition.ResourceUri;
                AuthenticationProfileUri = ReaderDefinition.AuthenticationProfileUri;
                RequestedDeliveryGuarantee = ReaderDefinition.RequestedDeliveryGuarantee;
                IsBrokerTransport = Visibility.Visible;
                IsDatagramTransport = Visibility.Collapsed;
            }
            else
            {
                IsBrokerTransport = Visibility.Collapsed;
                IsDatagramTransport = Visibility.Visible;
            }
            if (MessageSetting == 0)
            {
                NetworkMessageNumber = ReaderDefinition.NetworkMessageNumber;
                GroupVersion = ReaderDefinition.GroupVersion;
                DataSetOffset = ReaderDefinition.DataSetOffset;
                //   DataSetClassId = ReaderDefinition.DataSetClassId;
                UadpNetworkMessageContentMask = ReaderDefinition.UadpNetworkMessageContentMask;
                UadpDataSetMessageContentMask = ReaderDefinition.UadpDataSetMessageContentMask;
                Receiveoffset = ReaderDefinition.Receiveoffset;
                ProcessingOffset = ReaderDefinition.ProcessingOffset;

                IsDatagramMessage = Visibility.Visible;
                IsBrokerMessage = Visibility.Collapsed;

            }
            else
            {
                JsonDataSetMessageContentMask = ReaderDefinition.JsonDataSetMessageContentMask;
                JsonNetworkMessageContentMask = ReaderDefinition.JsonNetworkMessageContentMask;
                IsDatagramMessage = Visibility.Collapsed;
                IsBrokerMessage = Visibility.Visible;
            }

        }

        #endregion

        #region Public Fields
        public DataSetReaderDefinition ReaderDefinition;
        #endregion
    }
}