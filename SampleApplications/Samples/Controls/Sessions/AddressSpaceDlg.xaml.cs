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
using Windows.UI.Xaml.Controls;
using Opc.Ua.Sample.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class AddressSpaceDlg : Page
    {
        #region Constructors
        public AddressSpaceDlg()
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
        public void Show(Session session, BrowseViewType viewType, NodeId viewId)
        {   
            if (session == null) throw new ArgumentNullException("session");
            
            if (m_session != null)
            {
                m_session.SessionClosing -= m_SessionClosing;
            }

            m_session = session;            
            m_session.SessionClosing += m_SessionClosing;
            
            BrowseCTRL.SetView(session, viewType, viewId);
        }
        #endregion
        
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
    }
}
