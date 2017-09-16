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
using System.Collections.ObjectModel;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// defines security base definition
    /// </summary>
    public class SecurityBase : BaseViewModel
    {
        #region Private Fields

        private string m_securityGroupId;
        private string m_name;
        private ObservableCollection<SecurityBase> m_children = new ObservableCollection<SecurityBase>();

        #endregion

        #region Public Properties
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
        /// defines security group name
        /// </summary>
        public string Name
        {
            get
            {
                return m_name;
            }
            set
            {
                m_name = value;
                OnPropertyChanged("Name");
            }
        }

        /// <summary>
        /// defines child nodes of security base
        /// </summary>
        public ObservableCollection<SecurityBase> Children
        {
            get { return m_children; }
            set
            {
                m_children = value;
                OnPropertyChanged("Children");
            }
        }

        /// <summary>
        /// defines parent node of Security base
        /// </summary>
        public SecurityBase ParentNode { get; set; }
        #endregion
    }

    /// <summary>
    /// definition of Security Group
    /// </summary>
    public class SecurityGroup : SecurityBase
    {
        #region Private Fields
        private string m_groupName;
        private NodeId m_groupNodeId;
        #endregion

        #region Public Properties
        /// <summary>
        /// Defines Security Group Name
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
        /// Defines security group node ID
        /// </summary>
        public NodeId GroupNodeId
        {
            get
            {
                return m_groupNodeId;
            }
            set
            {
                m_groupNodeId = value;
                OnPropertyChanged("GroupNodeId");
            }
        }

        #endregion
    }

}
