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
        private Visibility m_isAMQP = Visibility.Collapsed;
        private Visibility m_isUADP = Visibility.Visible;
        private int m_keepAliveTime;
        private int m_maxNetworkMessageSize = 1500;
        private int m_messageSecurityMode = 1;
        private int m_priority;
        private int m_publishingInterval;
        private int m_publishingOffset;
        private string m_queueName = string.Empty;
        private string m_securityGroupId;
        private int m_writerGroupId;

        #endregion

        #region Public Properties

        /// <summary>
        /// defines group name
        /// </summary>
        public string GroupName
        {
            get { return m_groupName; }
            set
            {
                m_groupName = value;
                OnPropertyChanged( "GroupName" );
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
                OnPropertyChanged( "PublishingInterval" );
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
                OnPropertyChanged( "PublishingOffset" );
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
                OnPropertyChanged( "KeepAliveTime" );
            }
        }

        /// <summary>
        /// defines priority
        /// </summary>
        public int Priority
        {
            get { return m_priority; }
            set
            {
                m_priority = value;
                OnPropertyChanged( "Priority" );
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
                OnPropertyChanged( "SecurityGroupId" );
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
                OnPropertyChanged( "QueueName" );
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
                OnPropertyChanged( "EncodingMimeType" );
            }
        }

        /// <summary>
        /// defines maximum network message size
        /// </summary>
        public int MaxNetworkMessageSize
        {
            get { return m_maxNetworkMessageSize; }
            set
            {
                m_maxNetworkMessageSize = value;
                OnPropertyChanged( "MaxNetworkMessageSize" );
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
                OnPropertyChanged( "WriterGroupId" );
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
                OnPropertyChanged( "MessageSecurityMode" );
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
                OnPropertyChanged( "IsUADP" );
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
                OnPropertyChanged( "IsAMQP" );
            }
        }

        #endregion
    }
}