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
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseDlg : Page
    {
        #region Constructors
        public BrowseDlg()
        {
            InitializeComponent();
            m_SessionClosing = new EventHandler(Session_Closing);
        }
        #endregion
        
        #region Private Fields
        private Session m_session;
        private EventHandler m_SessionClosing;
        #endregion

        #region Public Interface
        /// <summary>
        /// Displays the address space with the specified view
        /// </summary>
        public void Show(Session session, NodeId startId)
        {   
            if (session == null) throw new ArgumentNullException("session");
            
            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
            }

            m_session = session;            
            m_session.SessionClosing += m_SessionClosing;
            
            Browser browser  = new Browser(session);

            browser.BrowseDirection = BrowseDirection.Both;
            browser.ContinueUntilDone = true;
            browser.ReferenceTypeId = ReferenceTypeIds.References;

            BrowseCTRL.Initialize(browser, startId);
            
            UpdateNavigationBar();
        }
        #endregion

        /// <summary>
        /// Updates the navigation bar with the current positions in the browse control.
        /// </summary>
        private void UpdateNavigationBar()
        {
            int index = 0;

            foreach (NodeId nodeId in BrowseCTRL.Positions)
            {
                Node node = m_session.NodeCache.Find(nodeId) as Node;

                string displayText = m_session.NodeCache.GetDisplayText(node);

                if (index < NodeCTRL.Items.Count)
                {
                    if (displayText != NodeCTRL.Items[index] as string)
                    {
                        NodeCTRL.Items[index] = displayText;
                    }
                }
                else
                {
                    NodeCTRL.Items.Add(displayText);
                }

                index++;
            }        
         
            while (index < NodeCTRL.Items.Count)
            {
                NodeCTRL.Items.RemoveAt(NodeCTRL.Items.Count-1);
            }
                                
            NodeCTRL.SelectedIndex = BrowseCTRL.Position;
        }
        
        private void Session_Closing(object sender, EventArgs e)
        {
            if (Object.ReferenceEquals(sender, m_session))
            {
                m_session.SessionClosing -= m_SessionClosing;
                m_session = null;
            }
        }

        private void AddressSpaceDlg_FormClosing(object sender, EventArgs e)
        {
            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
                m_session = null;
            }
        }

        private void BackBTN_Click(object sender, EventArgs e)
        {
            try
            {   
                BrowseCTRL.Back();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ForwardBTN_Click(object sender, EventArgs e)
        {
            try
            {   
                BrowseCTRL.Forward();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void NodeCTRL_SelectedIndexChanged(object sender, EventArgs e)
        {            
            try
            {   
                BrowseCTRL.SetPosition(NodeCTRL.SelectedIndex);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseCTRL_PositionChanged(object sender, EventArgs e)
        { 
            try
            {
                if (BrowseCTRL.Position < NodeCTRL.Items.Count)
                {
                    NodeCTRL.SelectedIndex = BrowseCTRL.Position;
                }
                else
                {
                    NodeCTRL.SelectedIndex = -1;
                }                   
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void BrowseCTRL_PositionAdded(object sender, EventArgs e)
        {
            try
            {
                UpdateNavigationBar();
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
    }
}
