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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Controls;
using ClientAdaptor;
using Opc.Ua.Client;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for main window
    /// </summary>
    public class MainViewModel : BaseViewModel
    {
        #region Private Fields 

        private Session m_session;
        private ObservableCollection<ServerNode> _selectedServers = new ObservableCollection<ServerNode>();
        public string _ServerStatusColor = Brushes.Orange.ToString();
        private string _ServerStatus = "Not Connected";
        public bool _IsConnectButtonVisible = true;
        public bool _IsDisConnectButtonVisible;
        private string _SelectedEndPoint;
        private ObservableCollection<TabItem> _TabItems = new ObservableCollection<TabItem>();
        private bool _TabItemEnabled;


        #endregion

        #region Private Methods

        private void OPCUAClientAdaptor_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ServerStatus")
            {
                ServerStatus = OPCUAClientAdaptor.ServerStatus;
            }
            else if (e.PropertyName == "RefreshOnReconnection")
            {
                if (OPCUAClientAdaptor.RefreshOnReconnection) InitializeViews();
            }
            else if (e.PropertyName == "ActivateTabsonReConnection")
            {
                TabItemEnabled = OPCUAClientAdaptor.ActivateTabsonReConnection;
            }
        }

        /// <summary>
        /// Method to initialise tabs
        /// </summary>
        private void InitializeViews()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var item in TabItems)
                {
                    if (item.Content is SecurityGroupConfigurationView)
                        (item.Content as SecurityGroupConfigurationView).ViewModel.Initialize();
                    if (item.Content is PubSubStatusView) (item.Content as PubSubStatusView).ViewModel.Initialize();

                    if (item.Content is PublishedDataSetView)
                        (item.Content as PublishedDataSetView).ViewModel.Initialize(Rootnode);

                    if (item.Content is PubSubConfigurationView)
                        (item.Content as PubSubConfigurationView).ViewModel.Initialize();


                }
            });
        }

        #endregion

        #region Constructors

        public MainViewModel()
        {
            OPCUAClientAdaptor = new OPCUAClientAdaptor();
            OPCUAClientAdaptor.PropertyChanged += OPCUAClientAdaptor_PropertyChanged;
            _SecurityGroupConfigurationView = new SecurityGroupConfigurationView(OPCUAClientAdaptor);
            _PubSubConfigurationView = new PubSubConfigurationView(OPCUAClientAdaptor);
            _PublisherDataSetView = new PublishedDataSetView(OPCUAClientAdaptor);
            _PubSubStatusView = new PubSubStatusView(OPCUAClientAdaptor);
            TabItems.Add(new TabItem
            {
                Header = "Security Group Configuration",
                Content = _SecurityGroupConfigurationView
            });
            TabItems.Add(new TabItem { Header = "PubSub Configuration", Content = _PubSubConfigurationView });
            TabItems.Add(new TabItem { Header = "Publisher DataSet Configuration", Content = _PublisherDataSetView });
            TabItems.Add(new TabItem { Header = "PubSub Status", Content = _PubSubStatusView });
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Method to connect the selected Url 
        /// </summary>
        /// <param name="endPointURL">url to establish the connection with the server</param> 
        public bool Connect(string endPointURL)
        {
            var errorMessage = string.Empty;

            m_session = OPCUAClientAdaptor.Connect(endPointURL, out errorMessage, out Rootnode);

            if (m_session == null)
            {
                ServerStatus = errorMessage;
                return false;
            }
            ServerStatus = "Running";
            InitializeViews();

            SelectedEndPoint = OPCUAClientAdaptor.SelectedEndpoint;
            TabItemEnabled = true;
            return true;
        }

        /// <summary>
        /// Method to disconnect the server
        /// </summary>
        /// <returns></returns>
        public bool DisConnect()
        {
            TabItemEnabled = false;
            return OPCUAClientAdaptor.Disconnect();
        }

        #endregion

        #region Public Fields
        public OPCUAClientAdaptor OPCUAClientAdaptor;
        public static TreeViewNode Rootnode;
        public static SecurityGroupConfigurationView _SecurityGroupConfigurationView;
        public static PubSubConfigurationView _PubSubConfigurationView;
        public static PublishedDataSetView _PublisherDataSetView;
        public static PubSubStatusView _PubSubStatusView;
        #endregion

        #region Public Properties

        /// <summary>
        /// defines definition of selected server 
        /// </summary>
        public ObservableCollection<ServerNode> SelectedServers
        {
            get { return _selectedServers; }
            set
            {
                _selectedServers = value;
                OnPropertyChanged("SelectedServers");
            }
        }

        /// <summary>
        /// defines current server status
        /// </summary>
        public string ServerStatus
        {
            get { return _ServerStatus; }
            set
            {
                _ServerStatus = value;
                OnPropertyChanged("ServerStatus");
            }
        }

        /// <summary>
        /// defines visibility for button based on current status
        /// </summary>
        public bool IsConnectButtonVisible
        {
            get { return _IsConnectButtonVisible; }
            set
            {
                _IsConnectButtonVisible = value;
                OnPropertyChanged("IsConnectButtonVisible");
            }
        }

        /// <summary>
        /// defines visibility for for button based on current status
        /// </summary>
        public bool IsDisConnectButtonVisible
        {
            get { return _IsDisConnectButtonVisible; }
            set
            {
                _IsDisConnectButtonVisible = value;
                OnPropertyChanged("IsDisConnectButtonVisible");
            }
        }

        /// <summary>
        /// defines current selected end point
        /// </summary>
        public string SelectedEndPoint
        {
            get { return _SelectedEndPoint; }
            set { _SelectedEndPoint = value; }
        }


        /// <summary>
        /// defines collection of tab items 
        /// </summary>
        public ObservableCollection<TabItem> TabItems
        {
            get { return _TabItems; }
            set
            {
                _TabItems = value;
                OnPropertyChanged("TabItems");
            }
        }

        /// <summary>
        /// defines current status of selected tab item
        /// </summary>
        public bool TabItemEnabled
        {
            get { return _TabItemEnabled; }
            set
            {
                _TabItemEnabled = value;
                OnPropertyChanged("TabItemEnabled");
            }
        }

        #endregion
    }
}