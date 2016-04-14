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
