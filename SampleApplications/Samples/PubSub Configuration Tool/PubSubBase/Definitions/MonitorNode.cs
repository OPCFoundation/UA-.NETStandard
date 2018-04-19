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
using System.Windows;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// A Monitered Node
    /// </summary>
    public class MonitorNode : BaseViewModel
    {
        #region Private Fields
        private string m_value = string.Empty;
      
        #endregion

        #region Public Properties
        /// <summary>
        /// Node ID of the Target Node
        /// </summary>
        public string Id { get; set; }
        /// <summary>
        /// Value of the target Node
        /// </summary>
        public string Value
        {
            get
            {
                return m_value;
            }
            set
            {
                m_value = value;
                OnPropertyChanged("Value");
            }
        }
       /// <summary>
       /// Parent Node ID of the Target Node
       /// </summary>
        public NodeId ParentNodeId { get; set; }
        /// <summary>
        /// Enable Node  ID of the Target Node
        /// </summary>
        public NodeId EnableNodeId { get; set; }
        /// <summary>
        /// Disable Node ID of the Target Node
        /// </summary>
        public NodeId DisableNodeId { get; set; }
        /// <summary>
        /// Display Name of the Target Node
        /// </summary>
        public string DisplayName { get; set; }


        #endregion
    }
}

