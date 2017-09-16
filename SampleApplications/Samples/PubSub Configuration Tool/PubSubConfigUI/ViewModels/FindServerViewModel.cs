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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for find server view
    /// </summary>
    internal class FindServerViewModel : BaseViewModel
    {
        #region Private Fields 

        private ObservableCollection< SystemNode > m_localMachineServerNode;
        private ObservableCollection< SystemNode > m_localNetworkServerNodes;

        #endregion

        #region Private Methods

        /// <summary>
        ///   Gets the IP address of the HostName
        /// </summary>
        /// <param name="hostName">HostName</param>
        /// <returns></returns>
        private string GetIpAddress( string hostName )
        {
            IPAddress[ ] ips;
            try
            {
                ips = Dns.GetHostAddresses( hostName );
            }
            catch ( Exception ex )
            {
                Utils.Trace(ex, "FindServerViewModel:GetIpAddress", ex );
                return string.Empty;
            }

            var ipAddress = string.Empty;
            foreach ( var ip in ips )
                if ( ip.AddressFamily == AddressFamily.InterNetwork )
                {
                    ipAddress = ip.ToString( );
                    break;
                }

            return ipAddress;
        }

        /// <summary>
        ///   Gets the endpoints for the host.
        /// </summary>
        /// <param name="hostName">Name of the host.</param>
        private List< ServerNode > GetEndpoints( string hostName )
        {
            var serverNodeList = new List< ServerNode >( );
            var applicationDescriptionCollection = OPCUAClientAdaptor.FindServers( hostName );
            if ( applicationDescriptionCollection != null )
                for ( var ii = 0; ii < applicationDescriptionCollection.Count; ii++ )
                {
                    // don't show discovery servers.
                    if ( applicationDescriptionCollection[ ii ].ApplicationType ==
                         ApplicationType.DiscoveryServer ) continue;

                    for ( var jj = 0; jj < applicationDescriptionCollection[ ii ].DiscoveryUrls.Count; jj++ )
                    {
                        var discoveryUrl = applicationDescriptionCollection[ ii ].DiscoveryUrls[ jj ];

                        // Many servers will use the '/discovery' suffix for the discovery endpoint.
                        // The URL without this prefix should be the base URL for the server. 
                        if ( discoveryUrl.EndsWith( "/discovery" ) )
                            discoveryUrl = discoveryUrl.Substring( 0, discoveryUrl.Length - "/discovery".Length );

                        var serverNode = new ServerNode( );
                        serverNode.Name = discoveryUrl;
                        serverNode.UAApplicationDescription = applicationDescriptionCollection[ ii ];
                        serverNodeList.Add( serverNode );
                    }
                }
            return serverNodeList;
        }

        #endregion

        #region Constructors

        public FindServerViewModel( OPCUAClientAdaptor _OPCUAClientAdaptor )
        {
            LocalMachineServerNode = new ObservableCollection< SystemNode >( );
            LocalNetworkServerNodes = new ObservableCollection< SystemNode >( );
            OPCUAClientAdaptor = _OPCUAClientAdaptor;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// collection of server available in local host
        /// </summary>
        public ObservableCollection< SystemNode > LocalMachineServerNode
        {
            get { return m_localMachineServerNode; }
            set
            {
                m_localMachineServerNode = value;
                OnPropertyChanged( "LocalMachineServerNode" );
            }
        }

        /// <summary>
        /// collection of server available in local network machines 
        /// </summary>
        public ObservableCollection< SystemNode > LocalNetworkServerNodes
        {
            get { return m_localNetworkServerNodes; }
            set
            {
                m_localNetworkServerNodes = value;
                OnPropertyChanged( "LocalNetworkServerNodes" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Method to find available servers in selected host.
        /// </summary>
        /// <param name="systemNode"></param>
        public void FindAvailableServers( SystemNode systemNode )
        {
            systemNode.Children.Clear( );
            foreach ( var servernode in GetEndpoints( systemNode.Name ) ) systemNode.Children.Add( servernode );
        }

        /// <summary>
        ///   Initially loads local machine
        /// </summary>
        public void FindSystemLocalMachine( )
        {
            LocalMachineServerNode.Clear( );
            var hostName = Dns.GetHostName( );
            var ipAddress = GetIpAddress( hostName );
            var hostItem = new SystemNode { Name = hostName, IpAddress = ipAddress };
            LocalMachineServerNode.Add( hostItem );
        }

        /// <summary>
        ///   Gets the current network systems.
        /// </summary>
        public void FindSystemLocalNetwork( )
        {
            var netUtility = new Process
                             {
                                 StartInfo =
                                 {
                                     FileName = "net.exe",
                                     CreateNoWindow = true,
                                     Arguments = "view",
                                     RedirectStandardOutput = true,
                                     UseShellExecute = false,
                                     RedirectStandardError = true
                                 }
                             };
            netUtility.Start( );

            try
            {
                var streamReader = new StreamReader( netUtility.StandardOutput.BaseStream,
                                                     netUtility.StandardOutput.CurrentEncoding );
                string line;
                var remoteSystem = new ObservableCollection< SystemNode >( );
                while ( (line = streamReader.ReadLine( )) != null )
                    if ( line.StartsWith( "\\" ) )
                    {
                        var hostName = line.Substring( 2 )
                                           .Substring(
                                               0, line.Substring( 2 ).IndexOf( " ", StringComparison.Ordinal ) )
                                           .ToUpper( );
                        if ( !string.IsNullOrEmpty( hostName ) && Environment.MachineName != hostName )
                        {
                            var ipAddress = GetIpAddress( hostName );
                            if ( string.IsNullOrEmpty( ipAddress ) ) continue;

                            var hostItem = new SystemNode { Name = hostName, IpAddress = ipAddress };
                            remoteSystem.Add( hostItem );
                        }
                    }
                streamReader.Close( );

                netUtility.WaitForExit( );
                Application.Current.Dispatcher.Invoke( ( ) => { LocalNetworkServerNodes = remoteSystem; } );
            }
            catch ( Exception exe )
            {
                Utils.Trace( exe,"FindServerViewModel:FindSystemLocalNetwork", exe );
            }
        }

        #endregion

        #region Public Fields
        public OPCUAClientAdaptor OPCUAClientAdaptor;

        #endregion
    }
}