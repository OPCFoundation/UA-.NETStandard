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
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Allows the user to browse a list of servers.
    /// </summary>
    public partial class ConfiguredServerListDlg : Page
    {
        #region Constructors
        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        public ConfiguredServerListDlg()
        {
            InitializeComponent();
        }
        #endregion
        
        #region Private Fields
        private ConfiguredEndpoint m_endpoint;
        private ApplicationConfiguration m_configuration;
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public ConfiguredEndpoint ShowDialog(ApplicationConfiguration configuration, bool createNew)
        {
            m_configuration = configuration;
            m_endpoint = null;

            // create a default collection if none provided.
            if (createNew)
            {
                ApplicationDescription server = new DiscoveredServerListDlg().ShowDialog(null, m_configuration);

                if (server != null)
                {
                    return new ConfiguredEndpoint(server, EndpointConfiguration.Create(configuration));
                }

                return null;
            }
            
            ServersCTRL.Initialize(null, configuration);
            
            OkBTN.IsEnabled = false;

            Popup myPopup = new Popup();
            myPopup.Child = this;
            myPopup.IsOpen = true;

            return m_endpoint;
        }
        #endregion
        
        #region Event Handlers
        private void ServersCTRL_ItemsSelected(object sender, ListItemActionEventArgs e)
        {
            try
            {
                m_endpoint = null;

                foreach (ConfiguredEndpoint server in e.Items)
                {
                    m_endpoint = server;
                    break;
                }

                OkBTN.IsEnabled = m_endpoint != null;
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }
        #endregion
    }
}
