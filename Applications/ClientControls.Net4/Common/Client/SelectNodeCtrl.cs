/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control with button that displays an open file dialog.
    /// </summary>
    public partial class SelectNodeCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public SelectNodeCtrl()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private event EventHandler m_NodeSelected;
        private ReferenceDescription m_selectedNode;
        #endregion

        #region Public Interface
        /// <summary>
        /// Gets or sets the current session.
        /// </summary>
        public Session Session { get; set; }

        /// <summary>
        /// Gets or sets starting node.
        /// </summary>
        public NodeId RootId { get; set; }

        /// <summary>
        /// Gets or sets the view to use.
        /// </summary>
        public ViewDescription View { get; set; }

        /// <summary>
        /// Gets or sets the reference types to follow.
        /// </summary>
        public NodeId[] ReferenceTypeIds { get; set; }
        
        /// <summary>
        /// Gets or sets the currently selected node.
        /// </summary>
        public NodeId SelectedNode
        {
            get
            {
                if (m_selectedNode != null)
                {
                    return (NodeId)m_selectedNode.NodeId;
                }

                return null;
            }

            set
            {
                if (NodeControl != null)
                {
                    NodeControl.Text = null;

                    if (value != null && Session != null)
                    {
                        NodeControl.Text = Session.NodeCache.GetDisplayText(value);
                    }
                }

                ReferenceDescription reference = new ReferenceDescription();
                reference.NodeId = value;
                
                if (Session != null)
                {
                    INode node = Session.NodeCache.Find(value);

                    if (node != null)
                    {
                        reference.BrowseName = node.BrowseName;
                        reference.DisplayName = node.DisplayName;
                    }
                }

                m_selectedNode = reference;
            }
        }

        /// <summary>
        /// Gets or sets the currently selected reference.
        /// </summary>
        public ReferenceDescription SelectedReference
        {
            get
            {
                return m_selectedNode;
            }

            set
            {
                if (NodeControl != null)
                {
                    NodeControl.Text = null;

                    if (value != null && !NodeId.IsNull(value.NodeId))
                    {
                        NodeControl.Text = value.ToString();
                    }
                }

                if (value == null || NodeId.IsNull(value.NodeId))
                {
                    m_selectedNode = null;
                    return;
                }

                m_selectedNode = value;
            }
        }

        /// <summary>
        /// Gets or sets the control that is stores with the current node.
        /// </summary>
        public Control NodeControl { get; set; }

        /// <summary>
        /// Raised when a new node is selected.
        /// </summary>
        public event EventHandler NodeSelected
        {
            add { m_NodeSelected += value; }
            remove { m_NodeSelected -= value; }
        }
        #endregion

        #region Event Handlers
        private void BrowseBTN_Click(object sender, EventArgs e)
        {
            ReferenceDescription reference = new SelectNodeDlg().ShowDialog(
                Session,
                RootId,
                View,
                null,
                ReferenceTypeIds);

            if (reference != null)
            {
                SelectedReference = reference;

                if (m_NodeSelected != null)
                {
                    m_NodeSelected(this, new EventArgs());
                }
            }
        }
        #endregion
    }
}
