/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.AlarmConditionClient
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SetAreaFilterDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SetAreaFilterDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public NodeId ShowDialog(Session session)
        {
            m_session = session;

            TreeNode root = new TreeNode(BrowseNames.Server);
            root.Nodes.Add(new TreeNode());
            BrowseTV.Nodes.Add(root);
            root.Expand();

            // display the dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            // ensure selection is valid.
            if (BrowseTV.SelectedNode == null)
            {
                return null;
            }

            // get the selection.
            ReferenceDescription reference = (ReferenceDescription)BrowseTV.SelectedNode.Tag;

            if (reference == null)
            {
                return ObjectIds.Server;
            }

            // return the result.
            return (NodeId)reference.NodeId;
        }
        #endregion
        
        #region Private Methods
        #endregion

        #region Event Handlers
        /// <summary>
        /// Handles the DoubleClick event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void BrowseTV_DoubleClick(object sender, EventArgs e)
        {
            try
            {
                if (BrowseTV.SelectedNode == null)
                {
                    return;
                }

                if (OkBTN.Enabled)
                {
                    DialogResult = DialogResult.OK;
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the AfterSelect event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewEventArgs"/> instance containing the event data.</param>
        private void BrowseTV_AfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;

                if (reference == null)
                {
                    OkBTN.Enabled = true;
                    return;
                }

                if (reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier)
                {
                    OkBTN.Enabled = true;
                    return;
                }

                OkBTN.Enabled = false;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        /// <summary>
        /// Handles the BeforeExpand event of the BrowseTV control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.TreeViewCancelEventArgs"/> instance containing the event data.</param>
        private void BrowseTV_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            try
            {
                ReferenceDescription reference = (ReferenceDescription)e.Node.Tag;
                e.Node.Nodes.Clear();

                // browse HasEventSource to display the sources but it won't be possible to select them.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = ObjectIds.Server;
                nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasEventSource;
                nodeToBrowse.IncludeSubtypes = true;
                nodeToBrowse.NodeClassMask = 0;
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                if (reference != null)
                {
                    nodeToBrowse.NodeId = (NodeId)reference.NodeId;
                }
                
                // add the childen to the control.
                ReferenceDescriptionCollection references = FormUtils.Browse(m_session, nodeToBrowse, false);
                
                for (int ii = 0; ii < references.Count; ii++)
                {
                    reference = references[ii];

                    // ignore out of server references.
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    TreeNode child = new TreeNode(reference.ToString());
                    child.Nodes.Add(new TreeNode());
                    child.Tag = reference;

                    e.Node.Nodes.Add(child);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
