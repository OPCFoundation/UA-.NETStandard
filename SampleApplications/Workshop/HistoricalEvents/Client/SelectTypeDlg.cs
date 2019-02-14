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
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Quickstarts.HistoricalEvents.Client
{
    /// <summary>
    /// Prompts the user to select an area to use as an event filter.
    /// </summary>
    public partial class SelectTypeDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public SelectTypeDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private NodeId m_rootId;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the available areas in a tree view.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public TypeDeclaration ShowDialog(Session session, NodeId rootId, string caption)
        {
            m_session = session;

            // set the caption.
            if (!String.IsNullOrEmpty(caption))
            {
                this.Text = caption;
            }

            // set default root.
            if (NodeId.IsNull(rootId))
            {
                rootId = Opc.Ua.ObjectTypeIds.BaseEventType;
            }

            m_rootId = rootId;

            // display root.
            TreeNode root = new TreeNode(session.NodeCache.GetDisplayText(rootId));
            root.Nodes.Add(new TreeNode());
            BrowseTV.Nodes.Add(root);
            root.Expand();
            BrowseTV.SelectedNode = root;

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

            // get the currently selected event.
            NodeId typeId = m_rootId;
            ReferenceDescription reference = BrowseTV.SelectedNode.Tag as ReferenceDescription;

            if (reference != null)
            {
                typeId = (NodeId)reference.NodeId;
            }

            TypeDeclaration declaration = new TypeDeclaration();
            declaration.NodeId = typeId;
            declaration.Declarations = new List<InstanceDeclaration>();

            // update selected fields.
            for (int ii = 0; ii < DeclarationsLV.Items.Count; ii++)
            {
                InstanceDeclaration instance = DeclarationsLV.Items[ii].Tag as InstanceDeclaration;

                if (instance != null)
                {
                    declaration.Declarations.Add(instance);
                }
            }

            // return the result.
            return declaration;
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
                DeclarationsLV.Items.Clear();

                if (e.Node == null)
                {
                    OkBTN.Enabled = false;
                    return;
                }

                OkBTN.Enabled = true;

                // get the currently selected event.
                NodeId typeId = m_rootId;
                ReferenceDescription reference = e.Node.Tag as ReferenceDescription;

                if (reference != null)
                {
                    typeId = (NodeId)reference.NodeId;
                }
                
                // get the instance declarations.
                List<InstanceDeclaration> instances = ModelUtils.CollectInstanceDeclarationsForType(m_session, typeId);

                // populate the list box.
                for (int ii = 0; ii < instances.Count; ii++)
                {
                    InstanceDeclaration instance = instances[ii];

                    ListViewItem item = new ListViewItem(instance.DisplayPath);
                    item.SubItems.Add(instance.DataTypeDisplayText);
                    item.SubItems.Add(instance.Description);
                    item.Tag = instance;

                    DeclarationsLV.Items.Add(item);
                }

                // resize columns to fit text.
                for (int ii = 0; ii < DeclarationsLV.Columns.Count; ii++)
                {
                    DeclarationsLV.Columns[ii].Width = -2;
                } 
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

                nodeToBrowse.NodeId = Opc.Ua.ObjectTypeIds.BaseEventType;
                nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false;
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
