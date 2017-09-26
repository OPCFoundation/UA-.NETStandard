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


using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for security group edit view
    /// </summary>
    public class SecurityGroupEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string m_groupNodeId;
        private string m_name = string.Empty;
        private string m_securityGroupId;

        #endregion

        #region Public Property
        /// <summary>
        /// defines name of the security group
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set
            {
                m_name = value;
                OnPropertyChanged("Name");
            }
        }

        /// <summary>
        /// defines group node ID
        /// </summary>
        public string GroupNodeId
        {
            get { return m_groupNodeId; }
            set
            {
                m_groupNodeId = value;
                OnPropertyChanged("GroupNodeId");
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
        /// initialiser method for Security Group
        /// </summary>
        public void Initialize()
        {
            Name = SecurityGroup.Name;
            SecurityGroupId = SecurityGroup.SecurityGroupId;
            GroupNodeId = SecurityGroup.GroupNodeId.ToString();
        }

        #endregion

        #region Public Fields
        public SecurityGroup SecurityGroup;
        #endregion
    }
}