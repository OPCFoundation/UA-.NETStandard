/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Opc.Ua.Sample.Controls
{
    public partial class SelectNodeDlg : Page
    {
        #region Constructors
        public SelectNodeDlg()
        {
            InitializeComponent();

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
            foreach (string uri in browser.Session.NamespaceUris.ToArray())
            {
                NamespaceUriCB.Items.Add(uri);
            }
            
            OkBTN.IsEnabled = false;

            return m_reference;
        }
        #endregion

        private void NamespaceUriCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.IsEnabled = false;
        }

        private void NamespaceUriCB_TextChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.IsEnabled = false;
        }

        private void NodeIdentifierTB_TextChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.IsEnabled = false;
        }

        private void IdentifierTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_reference = null;
            OkBTN.IsEnabled = false;
        }

        private void BrowseCTRL_NodeSelected(object sender, EventArgs e)
        {
            try
            {
                // disable ok button if selection is not valid.
                OkBTN.IsEnabled = false;

                ReferenceDescription reference = sender as ReferenceDescription;
                
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
                    NamespaceUriCB.SelectedValue = reference.NodeId.NamespaceUri;
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
                        NamespaceUriCB.SelectedValue = String.Empty;
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
                OkBTN.IsEnabled = true;
                m_reference = reference;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
    }
}
