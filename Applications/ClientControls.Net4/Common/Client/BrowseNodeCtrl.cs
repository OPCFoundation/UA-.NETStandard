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
using System.Text;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// A control which displays browse tree.
    /// </summary>
    public partial class BrowseNodeCtrl : UserControl
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance of the control.
        /// </summary>
        public BrowseNodeCtrl()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Initializes the control with a root and a set of hierarchial reference types to follow. 
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="rootId">The root of the hierarchy to browse.</param>
        /// <param name="referenceTypeIds">The reference types to follow.</param>
        public void Initialize(
            Session session,
            NodeId rootId,
            params NodeId[] referenceTypeIds)
        {
            BrowseCTRL.Initialize(session, rootId, referenceTypeIds);
        }

        /// <summary>
        /// Changes the session used by the control.
        /// </summary>
        /// <param name="session">The session.</param>
        public void ChangeSession(Session session)
        {
            BrowseCTRL.ChangeSession(session);
        }

        /// <summary>
        /// The view to use.
        /// </summary>
        public ViewDescription View
        {
            get { return BrowseCTRL.View; }
            set { BrowseCTRL.View = value; }
        }

        /// <summary>
        /// Gets or sets the default position of the splitter
        /// </summary>
        public int SplitterDistance
        {
            get { return MainPN.SplitterDistance; }
            set { MainPN.SplitterDistance = value; }
        }

        /// <summary>
        /// Gets or sets a flag that indicates whether the attributes should be displayed.
        /// </summary>
        public bool AttributesListCollapsed 
        {
            get { return MainPN.Panel2Collapsed;  }
            set { MainPN.Panel2Collapsed = value; }
        }

        /// <summary>
        /// Gets or sets the context menu for the browse tree.
        /// </summary>
        public ContextMenuStrip BrowseMenuStrip
        {
            get { return BrowseCTRL.BrowseMenuStrip; }
            set { BrowseCTRL.BrowseMenuStrip = value; }
        }

        /// <summary>
        /// Gets or sets the context menu for the attributes list.
        /// </summary>
        public ContextMenuStrip AttributesMenuStrip
        {
            get { return AttributesCTRL.AttributesMenuStrip; }
            set { AttributesCTRL.AttributesMenuStrip = value; }
        }

        /// <summary>
        /// The reference for the currently selected node.
        /// </summary>
        public ReferenceDescription SelectedNode
        {
            get
            {
                return BrowseCTRL.SelectedNode;
            }
        }

        /// <summary>
        /// The reference for the parent of the currently selected node.
        /// </summary>
        public ReferenceDescription SelectedParent
        {
            get
            {
                return BrowseCTRL.SelectedParent;
            }
        }

        /// <summary>
        /// Returns the child node at the specified index.
        /// </summary>
        public ReferenceDescription GetChildOfSelectedNode(int index)
        {
            return BrowseCTRL.GetChildOfSelectedNode(index);
        }

        /// <summary>
        /// Returns the attribute at the specified index.
        /// </summary>
        public ReadValueId GetSelectedAttribute(int index)
        {
            return AttributesCTRL.GetSelectedAttribute(index);
        }

        /// <summary>
        /// The reference for the parent of the currently selected node.
        /// </summary>
        public void RefreshSelection()
        {
            BrowseCTRL.RefreshSelection();
        }

        /// <summary>
        /// Raised after a node is selected in the control.
        /// </summary>
        public event EventHandler AfterSelect 
        {
            add { BrowseCTRL.AfterSelect += value; }
            remove { BrowseCTRL.AfterSelect -= value; } 
        }
        #endregion

        #region Private Methods
        #endregion

        #region Event Handlers
        #endregion
    }
}
