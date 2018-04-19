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

using System.Collections.ObjectModel;
using System.Windows;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// definition of node
    /// </summary>
    public class TreeViewNode : BaseViewModel
    {
        #region Constructors
        /// <summary>
        /// initialising node with children and reference
        /// </summary>
        public TreeViewNode()
        {
            Children = new ObservableCollection<TreeViewNode>();
            Reference = new ClientReferenceDescription();
        }
        #endregion

        #region Private Fields
        private string m_header;
        private string m_value = string.Empty;
        private Visibility m_isMethodEnable = Visibility.Collapsed;
        private Visibility m_isMethodDisable = Visibility.Collapsed;

        #endregion

        #region Properties
        /// <summary>
        /// defines header of the treenode
        /// </summary>
        public string Header
        {
            get
            {

                return m_header;
            }
            set
            {
                m_header = value;
            }
        }

        /// <summary>
        /// defines parent ID of the node
        /// </summary>
        public string ParentId { get; set; }

        /// <summary>
        /// defines reference of the node
        /// </summary>
        public ClientReferenceDescription Reference { get; set; }

        /// <summary>
        /// defines child nodes of the node
        /// </summary>
        public ObservableCollection<TreeViewNode> Children { get; set; }

        /// <summary>
        /// defines unique ID for the node
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// defines the node as Root node or not
        /// </summary>
        public bool IsRoot { get; set; }

        /// <summary>
        /// defines visibility for node menu
        /// </summary>
        public Visibility IsMethodEnable
        {
            get { return m_isMethodEnable; }
            set { m_isMethodEnable = value; OnPropertyChanged("IsMethodEnable"); }
        }

        /// <summary>
        /// defines visibility for node menu
        /// </summary>
        public Visibility IsMethodDisable
        {
            get { return m_isMethodDisable; }
            set { m_isMethodDisable = value; OnPropertyChanged("IsMethodDisable"); }
        }

        #endregion
    }
}
