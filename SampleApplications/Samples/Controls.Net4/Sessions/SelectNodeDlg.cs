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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class SelectNodeDlg : Form
    {
        #region Constructors
        public SelectNodeDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            foreach (IdType idType in Enum.GetValues(typeof(IdType)))
            {
                IdentifierTypeCB.Items.Add(idType);
            }

            foreach (NodeClass nodeClass in Enum.GetValues(typeof(NodeClass)))
            {
                NodeClassCB.Items.Add(nodeClass);
            }
        }
        #endregion

        #region Private Fields
        private ReferenceDescription m_reference;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ReferenceDescription ShowDialog(Browser browser, NodeId rootId)
        {
            if (browser == null) throw new ArgumentNullException("browser");

            BrowseCTRL.SetRoot(browser, rootId);

            NamespaceUriCB.Items.Clear();
            NamespaceUriCB.Items.AddRange(browser.Session.NamespaceUris.ToArray());
            
            OkBTN.Enabled = false;

            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }
            
            return m_reference;
        }
        #endregion

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void NamespaceUriCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.Enabled = false;
        }

        private void NamespaceUriCB_TextChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.Enabled = false;
        }

        private void NodeIdentifierTB_TextChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.Enabled = false;
        }

        private void IdentifierTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.Enabled = false;
        }

        private void BrowseCTRL_NodeSelected(object sender, TreeNodeActionEventArgs e)
        {
            try
            {
                // disable ok button if selection is not valid.
                OkBTN.Enabled = false;

                ReferenceDescription reference = e.Node as ReferenceDescription;
                
                if (reference == null)
                {
                    return;
                }

                if (NodeId.IsNull(reference.NodeId))
                {
                    return;
                }

                // set the display name.
                DisplayNameTB.Text       = reference.ToString();
                NodeClassCB.SelectedItem = (NodeClass)reference.NodeClass;
                
                // set identifier type.
                IdentifierTypeCB.SelectedItem = reference.NodeId.IdType;

                // set namespace uri.
                if (!String.IsNullOrEmpty(reference.NodeId.NamespaceUri))
                {
                    NamespaceUriCB.SelectedIndex = -1;
                    NamespaceUriCB.Text = reference.NodeId.NamespaceUri;
                }
                else
                {
                    if (reference.NodeId.NamespaceIndex < NamespaceUriCB.Items.Count)
                    {
                        NamespaceUriCB.SelectedIndex = (int)reference.NodeId.NamespaceIndex;
                    }
                    else
                    {
                        NamespaceUriCB.SelectedIndex = -1;
                        NamespaceUriCB.Text = null;
                    }
                }
                
                // set identifier.
                switch (reference.NodeId.IdType)
                {
                    case IdType.Opaque:
                    {
                        NodeIdentifierTB.Text = Convert.ToBase64String((byte[])reference.NodeId.Identifier);
                        break;
                    }

                    default:
                    {
                        NodeIdentifierTB.Text = Utils.Format("{0}", reference.NodeId.Identifier);
                        break;
                    }
                }

                // selection valid - enable ok.
                OkBTN.Enabled = true;
                m_reference = reference;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }
}
