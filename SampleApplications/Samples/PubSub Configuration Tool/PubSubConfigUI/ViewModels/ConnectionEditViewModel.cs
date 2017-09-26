/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

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

using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// definition of Connection view model
    /// </summary>
    public class ConnectionEditViewModel : BaseViewModel
    {
        #region Private Fields 

        private string m_address = string.Empty;
        private string m_connectionName = string.Empty;
        private int m_connectionType;
        private object m_publisherId;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines connection name 
        /// </summary>
        public string ConnectionName
        {
            get { return m_connectionName; }
            set
            {
                m_connectionName = value;
                OnPropertyChanged( "ConnectionName" );
            }
        }
        /// <summary>
        /// defines connection address
        /// </summary>
        public string Address
        {
            get { return m_address; }
            set
            {
                m_address = value;
                OnPropertyChanged( "Address" );
            }
        }
        
        /// <summary>
        /// defines publisher ID for connection
        /// </summary>
        public object PublisherId
        {
            get { return m_publisherId; }
            set
            {
                m_publisherId = value;
                OnPropertyChanged( "PublisherId" );
            }
        }

        /// <summary>
        /// defines connection type
        /// </summary>
        public int ConnectionType
        {
            get { return m_connectionType; }
            set
            {
                m_connectionType = value;
                OnPropertyChanged( "ConnectionType" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// initialise view model with new connection
        /// </summary>
        public void Initialize( )
        {
            ConnectionName = Connection.Name;
            Address = Connection.Address;
            PublisherId = Connection.PublisherId;
            ConnectionType = Connection.ConnectionType;
        }

        #endregion

        public Connection Connection;
    }
}