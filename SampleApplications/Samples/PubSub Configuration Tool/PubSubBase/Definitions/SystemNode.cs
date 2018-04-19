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
using System.Collections.ObjectModel;

namespace PubSubConfigurationUI.Definitions
{
    /// <summary>
    /// definition of channel information
    /// </summary>
    [Serializable]
    public class SystemNode
    {
        #region Private Fields
        private ObservableCollection<ServerNode> m_children;
        #endregion

        #region Public Properties
        /// <summary>
        /// defines name of the channel
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// defines channel IP address
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// defines list of available servers in particular host/machine
        /// </summary>
        public ObservableCollection<ServerNode> Children
        {
            get { return m_children; }
            set { m_children = value; }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// initialising channel definition
        /// </summary>
        public SystemNode()
        {
            Children = new ObservableCollection<ServerNode>();
        }
        #endregion
    }
}
