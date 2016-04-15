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
