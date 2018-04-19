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


using System.ComponentModel;
using System.Windows;
using PubSubConfigurationUI.ViewModels;
using PubSubConfigurationUI.Views;
using Opc.Ua;

namespace PubSubConfigurationUI
{
    /// <summary>
    ///   Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Private Member 

        private readonly MainViewModel m_mainViewModel;

        #endregion

        #region Private Methods

        private void MainWindow_Loaded( object sender, RoutedEventArgs e )
        {
            PubSubTabItems.SelectedIndex = 0;
        }

        private void MainWindow_Closing( object sender, CancelEventArgs e )
        {
            ShowDown( );
        }
        /// <summary>
        /// Event used to connect the OPC UA Server with the configured URL
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnConnectClick( object sender, RoutedEventArgs e )
        {
            var isConnected = m_mainViewModel.Connect( cmbBox.Text );
            if ( isConnected )
            {
                cmbBox.Text = m_mainViewModel.SelectedEndPoint;
                //  PubSubTabItems.IsEnabled = true;
                m_mainViewModel.IsConnectButtonVisible = false;
                m_mainViewModel.IsDisConnectButtonVisible = true;
                Utils.Trace( "Server connected Successfully " + cmbBox.Text );
            }
        }
        /// <summary>
        /// Event used to disconnect from the OPC UA Server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnDisConnectClick( object sender, RoutedEventArgs e )
        {
            var isDisConnected = m_mainViewModel.DisConnect( );
            if ( isDisConnected )
            {
                //   PubSubTabItems.IsEnabled = false;
                m_mainViewModel.IsConnectButtonVisible = true;
                m_mainViewModel.IsDisConnectButtonVisible = false;

                m_mainViewModel.ServerStatus = "Disconnected";
                Utils.Trace( "Server disconnected Successfully " );
            }
        }
        /// <summary>
        /// Event used to find the available servers in the network
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnFindServerClick( object sender, RoutedEventArgs e )
        {
            var _FindServerDlg = new FindServerDlg(m_mainViewModel.OPCUAClientAdaptor );
            _FindServerDlg.Closing += _FindServerDlg_Closing;
            _FindServerDlg.ShowDialog( );
        }
        /// <summary>
        /// Event to close the find server dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _FindServerDlg_Closing( object sender, CancelEventArgs e )
        {
            var _FindServerDlg = sender as FindServerDlg;
            if ( _FindServerDlg._selectedServer != null )
            {
                m_mainViewModel.SelectedServers.Insert( 0, _FindServerDlg._selectedServer );
                cmbBox.SelectedIndex = 0;
            }
        }
        /// <summary>
        /// Event to close the window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Exit_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }
        /// <summary>
        /// Disconnect/close the seeion when window is closed
        /// </summary>
        private void ShowDown( )
        {
            m_mainViewModel.OPCUAClientAdaptor.Disconnect( );
        }
        /// <summary>
        /// Event to view the build number, version number of Configuration tool
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAboutClick( object sender, RoutedEventArgs e )
        {
            var helpInformation = new AboutInformationDlg( );
            helpInformation.ShowDialog( );
        }

        #endregion

        #region Constructors

        public MainWindow( )
        {
            InitializeComponent( );
            Closing += MainWindow_Closing;
            DataContext = m_mainViewModel = new MainViewModel(this );
            Loaded += MainWindow_Loaded;
        }

        #endregion
    }
}