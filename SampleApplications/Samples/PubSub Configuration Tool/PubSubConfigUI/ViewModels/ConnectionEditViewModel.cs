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

using System;
using PubSubBase.Definitions;
using System.Windows;

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
        private int m_connectionType = 0;
        private object m_publisherId;
        private int m_transportType;
        public string m_networkInterface;
        public string m_resourceUri;
        public string m_authenticationProfileUri;
        public Visibility m_TransportBrokerVisibility = Visibility.Collapsed;
        public Visibility m_TransportDatagramVisibility = Visibility.Visible;

        private string m_discoveryNetworkInterface;
        private string m_discoveryAddress;
        #endregion

        #region Public Properties

        /// <summary>
        /// Defines the coonnection address 
        /// </summary>
        public string DiscoveryAddress
        {
            get
            {
                return m_discoveryAddress;
            }
            set
            {
                m_discoveryAddress = value;
                OnPropertyChanged("DiscoveryAddress");
            }
        }

        /// <summary>
        /// Defines the coonnection address 
        /// </summary>
        public string DiscoveryNetworkInterface
        {
            get
            {
                return m_discoveryNetworkInterface;
            }
            set
            {
                m_discoveryNetworkInterface = value;
                OnPropertyChanged("DiscoveryNetworkInterface");
            }
        }

        public Visibility TransportBrokerVisibility
        {
            get
            {
                return m_TransportBrokerVisibility;
            }
            set
            {
                m_TransportBrokerVisibility = value;
                OnPropertyChanged("TransportBrokerVisibility");
            }
        }

        public Visibility TransportDatagramVisibility
        {
            get
            {
                return m_TransportDatagramVisibility;
            }
            set
            {
                m_TransportDatagramVisibility = value;
                OnPropertyChanged("TransportDatagramVisibility");
            }
        }

        /// <summary>
        /// defines connection name 
        /// </summary>
        public string ConnectionName
        {
            get { return m_connectionName; }
            set
            {
                m_connectionName = value;
                OnPropertyChanged("ConnectionName");
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
                OnPropertyChanged("Address");
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
                OnPropertyChanged("PublisherId");
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
                OnPropertyChanged("ConnectionType");
            }
        }


        public int TransportType
        {
            get { return m_transportType; }
            set
            {
                m_transportType = value;
                OnPropertyChanged("TransportType");
            }
        }

        public string NetworkInterface
        {
            get { return m_networkInterface; }
            set
            {
                m_networkInterface = value;
                OnPropertyChanged("NetworkInterface");
            }
        }

        public string ResourceUri
        {
            get { return m_resourceUri; }
            set
            {
                m_resourceUri = value;
                OnPropertyChanged("ResourceUri");
            }
        }

        public string AuthenticationProfileUri
        {
            get { return m_authenticationProfileUri; }
            set
            {
                m_authenticationProfileUri = value;
                OnPropertyChanged("AuthenticationProfileUri");
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// initialise view model with new connection
        /// </summary>
        public void Initialize()
        {
            ConnectionName = Connection.Name;
            Address = Connection.Address;
            PublisherId = Connection.PublisherId;
            ConnectionType = Convert.ToInt16(Connection.ConnectionType);

            if (ConnectionType == 0)
            {
                TransportDatagramVisibility = Visibility.Visible;
                TransportBrokerVisibility = Visibility.Collapsed;

                DiscoveryAddress = Connection.DiscoveryAddress;
                DiscoveryNetworkInterface = Connection.DiscoveryNetworkInterface;
            }
            else
            {
                ResourceUri = Connection.ResourceUri;
                AuthenticationProfileUri = Connection.AuthenticationProfileUri;

                TransportDatagramVisibility = Visibility.Collapsed;
                TransportBrokerVisibility = Visibility.Visible;
            }
            SetTransportType(Connection.TransportProfile);
            NetworkInterface = Connection.NetworkInterface;

        }

        private void SetTransportType(string transportProfile)
        {
            switch (transportProfile)
            {
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp":
                    TransportType = 0;
                    break;
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-eth-uadp":
                    TransportType = 1;
                    break;
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp":
                    TransportType = 2;
                    break;
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json":
                    TransportType = 3;
                    break;
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-amqp-uadp":
                    TransportType = 4;
                    break;
                case "http://opcfoundation.org/UA-Profile/Transport/pubsub-amqp-json":
                    TransportType = 5;
                    break;
            }
        }

        #endregion

        public Connection Connection;
    }
}