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
    internal class FindServerViewModel : BaseViewModel
    {
        #region Private Member 

        private ObservableCollection< SystemNode > _LocalMachineServerNode;
        private ObservableCollection< SystemNode > _LocalNetworkServerNodes;

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
            var ServerNodeList = new List< ServerNode >( );
            var _ApplicationDescriptionCollection = OPCUAClientAdaptor.FindServers( hostName );
            if ( _ApplicationDescriptionCollection != null )
                for ( var ii = 0; ii < _ApplicationDescriptionCollection.Count; ii++ )
                {
                    // don't show discovery servers.
                    if ( _ApplicationDescriptionCollection[ ii ].ApplicationType ==
                         ApplicationType.DiscoveryServer ) continue;

                    for ( var jj = 0; jj < _ApplicationDescriptionCollection[ ii ].DiscoveryUrls.Count; jj++ )
                    {
                        var discoveryUrl = _ApplicationDescriptionCollection[ ii ].DiscoveryUrls[ jj ];

                        // Many servers will use the '/discovery' suffix for the discovery endpoint.
                        // The URL without this prefix should be the base URL for the server. 
                        if ( discoveryUrl.EndsWith( "/discovery" ) )
                            discoveryUrl = discoveryUrl.Substring( 0, discoveryUrl.Length - "/discovery".Length );

                        var _ServerNode = new ServerNode( );
                        _ServerNode.Name = discoveryUrl;
                        _ServerNode.UAApplicationDescription = _ApplicationDescriptionCollection[ ii ];
                        ServerNodeList.Add( _ServerNode );
                    }
                }
            return ServerNodeList;
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

        #region Public Property

        public ObservableCollection< SystemNode > LocalMachineServerNode
        {
            get { return _LocalMachineServerNode; }
            set
            {
                _LocalMachineServerNode = value;
                OnPropertyChanged( "LocalMachineServerNode" );
            }
        }

        public ObservableCollection< SystemNode > LocalNetworkServerNodes
        {
            get { return _LocalNetworkServerNodes; }
            set
            {
                _LocalNetworkServerNodes = value;
                OnPropertyChanged( "LocalNetworkServerNodes" );
            }
        }

        #endregion

        #region Public Methods

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

        public OPCUAClientAdaptor OPCUAClientAdaptor;

        #region Public Properties

        #endregion
    }
}