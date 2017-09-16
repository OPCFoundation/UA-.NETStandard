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
    /// view model for data set writer group edit view
    /// </summary>
    public class DataSetWriterGroupEditViewModel : BaseViewModel
    {
        #region Private Fields 

        private string m_encodingMimeType = string.Empty;
        private string m_groupName;
        private Visibility m_isAMQP = Visibility.Collapsed;
        private Visibility m_isUADP = Visibility.Visible;
        private double m_keepAliveTime;
        private int m_maxNetworkMessageSize = 1500;
        private int m_messageSecurityMode = 1;
        private int m_netWorkMessageContentMask;
        private int m_priority;
        private double m_publishingInterval;
        private double m_publishingOffset;
        private string m_QueueName = string.Empty;
        private string m_securityGroupId;
        private int m_writerGroupId;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines group name for target definition
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
        /// defines queue name for target definition
        /// </summary>
        public string QueueName
        {
            get { return m_QueueName; }
            set
            {
                m_QueueName = value;
                OnPropertyChanged("QueueName");
            }
        }
        
        /// <summary>
        /// defines encoding mime type for target definition
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
        /// defines publishing interval for target definition.
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

        /// <summary>
        /// defines publishing offset for target definition
        /// </summary>
        public double PublishingOffset
        {
            get { return m_publishingOffset; }
            set
            {
                m_publishingOffset = value;
                OnPropertyChanged("PublishingOffset");
            }
        }

        /// <summary>
        /// defines keepAliveTime for target definition
        /// </summary>
        public double KeepAliveTime
        {
            get { return m_keepAliveTime; }
            set
            {
                m_keepAliveTime = value;
                OnPropertyChanged("KeepAliveTime");
            }
        }

        /// <summary>
        /// defines priority for target definition
        /// </summary>
        public int Priority
        {
            get { return m_priority; }
            set
            {
                m_priority = value;
                OnPropertyChanged("Priority");
            }
        }

        /// <summary>
        /// defines security group ID of target definition
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
        /// defines maximum network message size of target definition
        /// </summary>
        public int MaxNetworkMessageSize
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
        /// defines network message content mask
        /// </summary>
        public int NetworkMessageContentMask
        {
            get { return m_netWorkMessageContentMask; }
            set
            {
                m_netWorkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

        /// <summary>
        /// defines message security mode of target definition
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

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsUADP
        {
            get { return m_isUADP; }
            set
            {
                m_isUADP = value;
                OnPropertyChanged("IsUADP");
            }
        }

        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsAMQP
        {
            get { return m_isAMQP; }
            set
            {
                m_isAMQP = value;
                OnPropertyChanged("IsAMQP");
            }
        }

        #endregion

        #region Public Methods

        //CheckBox
        /// <summary>
        /// Initialise method for DataSetWriterGroup
        /// </summary>
        public void Initialize()
        {
            GroupName = DataSetWriterGroup.GroupName;
            PublishingInterval = DataSetWriterGroup.PublishingInterval;
            PublishingOffset = DataSetWriterGroup.PublishingOffset;
            KeepAliveTime = DataSetWriterGroup.KeepAliveTime;
            Priority = DataSetWriterGroup.Priority;
            SecurityGroupId = DataSetWriterGroup.SecurityGroupId;
            MaxNetworkMessageSize = DataSetWriterGroup.MaxNetworkMessageSize;
            WriterGroupId = DataSetWriterGroup.WriterGroupId;
            QueueName = DataSetWriterGroup.QueueName;
            EncodingMimeType = DataSetWriterGroup.EncodingMimeType;
            NetworkMessageContentMask = DataSetWriterGroup.NetworkMessageContentMask;
            MessageSecurityMode = DataSetWriterGroup.MessageSecurityMode;
        }

        #endregion

        #region Public Fields
        public DataSetWriterGroup DataSetWriterGroup;
        #endregion
    }
}