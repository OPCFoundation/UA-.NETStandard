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

namespace PubSubBase.Definitions
{
    /// <summary>
    /// defines reader group definition
    /// </summary>
    public class ReaderGroupDefinition : PubSubConfiguationBase
    {
        #region Private Fields

        private string m_groupName;
        private string m_securityGroupId;
        private int m_maxNetworkMessageSize = 1500;
        private int m_securityMode;
        private string m_queueName;
        private NodeId m_groupId;

        #endregion

        #region Public Properties

        /// <summary>
        /// defines group name 
        /// </summary>
        public string GroupName
        {
            get
            {
                return m_groupName;
            }
            set
            {
                Name = m_groupName = value;
                OnPropertyChanged("GroupName");
            }
        }

        /// <summary>
        /// defines security group ID
        /// </summary>
        public string SecurityGroupId
        {
            get
            {
                return m_securityGroupId;
            }
            set
            {
                m_securityGroupId = value;
                OnPropertyChanged("SecurityGroupId");
            }
        }

        /// <summary>
        /// defines maximum network message size
        /// </summary>
        public int MaxNetworkMessageSize
        {
            get
            {
                return m_maxNetworkMessageSize;
            }
            set
            {
                m_maxNetworkMessageSize = value;
                OnPropertyChanged("MaxNetworkMessageSize");
            }
        }

        /// <summary>
        /// defines  security mode
        /// </summary>
        public int SecurityMode
        {
            get { return m_securityMode; }
            set
            {
                m_securityMode = value;
                OnPropertyChanged("SecurityMode");
            }
        }

        /// <summary>
        /// defines queue name
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
        /// defines group ID 
        /// </summary>
        public NodeId GroupId
        {
            get
            {
                return m_groupId;

            }
            set
            {
                m_groupId = value;
                OnPropertyChanged("GroupId");
            }
        }

        #endregion
    }
}
