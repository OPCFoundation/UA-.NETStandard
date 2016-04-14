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
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class BrowseTypesDlg : Page
    {
        #region Constructors
        public BrowseTypesDlg()
        {
            InitializeComponent();
        }
        #endregion

        #region Private Fields
        private Session m_session;
        //private ILocalNode m_selectedType;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public void Show(
            Session session,
            NodeId  typeId)
        {
            if (session == null) throw new ArgumentNullException("session");

            m_session = session;                    
            
            //TypeNavigatorCTRL.Initialize(m_session, typeId);
            //TypeHierarchyCTRL.Initialize(m_session, typeId);
        }
        #endregion
        
        #region Private Methods
        #endregion
        
        #region Event Handler
        private void TypeNavigatorCTRL_TypeSelected(object sender, EventArgs e)
        {
            try
            {
                //m_selectedType = e.Node;

                //if (m_selectedType != null)
                //{
                //    TypeHierarchyCTRL.Initialize(m_session, m_selectedType.NodeId);
                //}
                //else
                //{
                //    TypeHierarchyCTRL.Initialize(m_session, null);
                //}
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
