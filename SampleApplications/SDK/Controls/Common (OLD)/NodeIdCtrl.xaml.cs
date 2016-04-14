/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using Opc.Ua.Client;
using System;
using System.ComponentModel;
using System.Reflection;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A list of node ids.
    /// </summary>
    public sealed partial class NodeIdCtrl : UserControl
    {
		#region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeIdCtrl"/> class.
        /// </summary>
        public NodeIdCtrl()
        {
            InitializeComponent();

            m_rootId = Objects.RootFolder;
            BrowseBTN.IsEnabled = false;
        }
        #endregion

        public NodeId ShowDialog(Session session, NodeId value)
        {
            if (session == null) throw new ArgumentNullException("session");

            m_browser = new Browser(session);
            m_rootId = Objects.RootFolder;
            this.Identifier = value;

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            return this.Identifier;
        }

        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ExpandedNodeId ShowDialog(Session session, ExpandedNodeId value)
        {
            if (session == null) throw new ArgumentNullException("session");

            m_browser = new Browser(session);
            m_rootId = Objects.RootFolder;
            Identifier = ExpandedNodeId.ToNodeId(value, session.NamespaceUris);

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            return this.Identifier;
        }

        #region Event Handlers
        private Browser m_browser;
        private NodeId m_rootId;
        private ReferenceDescription m_reference;
        private event EventHandler m_IdentifierChanged;
		#endregion
        
		#region Public Interface
        /// <summary>
        /// Raised if the node id is changed.
        /// </summary>
        public event EventHandler IdentifierChanged
        {
            add    { m_IdentifierChanged += value; }
            remove { m_IdentifierChanged -= value; }
        }
        
        /// <summary>
        /// The browser to used browse for a node id.
        /// </summary>
        [DefaultValue(null)]
        public Browser Browser
        {
            get 
            { 
                return m_browser;  
            }
            
            set 
            { 
                m_browser = value; 
                BrowseBTN.IsEnabled = m_browser != null;
            }
        }
        
        /// <summary>
        /// The root node id to display when browsing.
        /// </summary>
        [DefaultValue(null)]
        public NodeId RootId
        {
            get
            {
                return m_rootId;                
            }

            set
            {
                m_rootId = value;

                if (NodeId.IsNull(m_rootId))
                {
                    m_rootId = Objects.RootFolder;
                }
            }
        }

        /// <summary>
        /// Returns true if the control is empty.
        /// </summary>
        [DefaultValue(false)]
        public bool IsEmpty
        {
            get
            { 
                return String.IsNullOrEmpty(NodeIdTB.Text);
            }
        }

        /// <summary>
        /// The node identifier specified in the control.
        /// </summary>
        [DefaultValue(null)]
        public NodeId Identifier
        {
            get
            { 
                return NodeId.Parse(NodeIdTB.Text);  
            }
            
            set
            { 
                NodeIdTB.Text = Utils.Format("{0}", value); 
            }
        }
        
        /// <summary>
        /// The reference seleected if the browse feature was used.
        /// </summary>
        [DefaultValue(null)]
        public ReferenceDescription Reference
        {
            get
            { 
                return m_reference;  
            }
            
            set
            { 
                m_reference = value;

                if (m_reference != null)
                {
                    NodeIdTB.Text = Utils.Format("{0}", m_reference.NodeId);
                }
            }
        }
		#endregion
        
		#region Event Handlers
        private void NodeIdTB_TextChanged(object sender, EventArgs e)
        {
            if (m_IdentifierChanged != null)
            {
                m_IdentifierChanged(this, null);
            }
        }
		#endregion
    }
}
