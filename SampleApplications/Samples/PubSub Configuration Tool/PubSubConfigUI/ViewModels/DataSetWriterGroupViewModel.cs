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
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for Data set writer group view
    /// </summary>
    public class DataSetWriterGroupViewModel : BaseViewModel
    {
        #region Private Fields 

        private string m_encodingMimeType = string.Empty;
        private string m_groupName;
        private int m_keepAliveTime;
        private uint m_maxNetworkMessageSize = 1500;
        private int m_messageSecurityMode = 1;
        private byte m_priority;
        private int m_publishingInterval;
        private int m_publishingOffset;
        private int m_samplingOffset;
        private string m_queueName = string.Empty;
        private string m_securityGroupId = "0";
        private int m_writerGroupId;
        private byte m_messageRepeatCount;
        private double m_messsageRepeatDelay;
        private string m_resourceUri;
        private string m_authenticationProfileUri;
        private int m_requestedDeliveryGuarantee;
        private int m_transportSetting;
        private int m_messgaeSetting;
        private int m_dataSetOrdering = 0;
        private uint m_groupVersion;
        private int m_networkMessageContentMask;
        private int m_jsonNetworkMessageContentMask;

        private Visibility m_isDatagramTransport = Visibility.Visible;
        private Visibility m_isBrokerTransport = Visibility.Collapsed;
        private Visibility m_isDatagramMessage = Visibility.Visible;
        private Visibility m_isBrokerMessage = Visibility.Collapsed;

        #endregion

        #region Public Properties

        /// <summary>
        /// defines network message content mask
        /// </summary>
        public int UadpNetworkMessageContentMask
        {
            get { return m_networkMessageContentMask; }
            set
            {
                m_networkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

        /// <summary>
        /// defines network message content mask
        /// </summary>
        public int JsonNetworkMessageContentMask
        {
            get { return m_jsonNetworkMessageContentMask; }
            set
            {
                m_jsonNetworkMessageContentMask = value;
                OnPropertyChanged("JsonNetworkMessageContentMask");
            }
        }

        public int DataSetOrdering
        {
            get { return m_dataSetOrdering; }
            set { m_dataSetOrdering = value; OnPropertyChanged("DataSetOrdering"); }
        }

        public int TransportSetting
        {
            get { return m_transportSetting; }
            set { m_transportSetting = value; OnPropertyChanged("TransportSetting"); }
        }

        public int MessageSetting
        {
            get { return m_messgaeSetting; }
            set { m_messgaeSetting = value; OnPropertyChanged("MessageSetting"); }
        }

        public uint GroupVersion
        {
            get
            {
                return m_groupVersion;
            }
            set
            {
                m_groupVersion = value;
                OnPropertyChanged("GroupVersion");
            }
        }
        /// <summary>
        /// defines group name
        /// </summary>
        public string GroupName
        {
            get { return m_groupName; }
            set
            {
                m_groupName = value;
                OnPropertyChanged("GroupName");
            }
        }

        /// <summary>
        /// defines publishing interval
        /// </summary>
        public int PublishingInterval
        {
            get { return m_publishingInterval; }
            set
            {
                m_publishingInterval = value;
                OnPropertyChanged("PublishingInterval");
            }
        }

        /// <summary>
        /// defines publishing offset
        /// </summary>
        public int PublishingOffset
        {
            get { return m_publishingOffset; }
            set
            {
                m_publishingOffset = value;
                OnPropertyChanged("PublishingOffset");
            }
        }

        public int SamplingOffset
        {
            get { return m_samplingOffset; }
            set
            {
                m_samplingOffset = value;
                OnPropertyChanged("SamplingOffset");
            }
        }

        /// <summary>
        /// defines keep alive time
        /// </summary>
        public int KeepAliveTime
        {
            get { return m_keepAliveTime; }
            set
            {
                m_keepAliveTime = value;
                OnPropertyChanged("KeepAliveTime");
            }
        }

        /// <summary>
        /// defines priority
        /// </summary>
        public byte Priority
        {
            get { return m_priority; }
            set
            {
                m_priority = value;
                OnPropertyChanged("Priority");
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

        /// <summary>
        /// defines quee name 
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
        /// defines encoding mime type
        /// </summary>
        public string EncodingMimeType
        {
            get { return m_encodingMimeType; }
            set
            {
                m_encodingMimeType = value;
                OnPropertyChanged("EncodingMimeType");
            }
        }

        /// <summary>
        /// defines maximum network message size
        /// </summary>
        public uint MaxNetworkMessageSize
        {
            get { return m_maxNetworkMessageSize; }
            set
            {
                m_maxNetworkMessageSize = value;
                OnPropertyChanged("MaxNetworkMessageSize");
            }
        }

        /// <summary>
        /// defines writer group ID
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
        /// defines message security mode
        /// </summary>
        public int MessageSecurityMode
        {
            get { return m_messageSecurityMode; }
            set
            {
                m_messageSecurityMode = value;
                OnPropertyChanged("MessageSecurityMode");
            }
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

        public byte MessageRepeatCount
        {
            get { return m_messageRepeatCount; }
            set { m_messageRepeatCount = value; OnPropertyChanged("MessageRepeatCount"); }
        }

        public double MessageRepeatDelay
        {
            get { return m_messsageRepeatDelay; }
            set { m_messsageRepeatDelay = value; OnPropertyChanged("MessageRepeatDelay"); }
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

        #endregion
    }
}