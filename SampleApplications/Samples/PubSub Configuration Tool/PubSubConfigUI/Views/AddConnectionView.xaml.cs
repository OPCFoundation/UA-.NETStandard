using System;
using System.Windows;
using System.Windows.Controls;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddConnection.xaml
    /// </summary>
    public partial class AddConnectionView : Window
    {
        #region Event Handlers

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PublisherIdTxt.Text))
            {
                ShowErrorMessage("Publisher Id cannot be empty", "Add Connection");
                return;
            }
            if (string.IsNullOrWhiteSpace(ConnectionNameTxt.Text))
            {
                ShowErrorMessage("Connection name cannot be empty", "Add Connection");
                return;
            }
            if (string.IsNullOrWhiteSpace(AddressTxt.Text))
            {
                ShowErrorMessage("Address cannot be empty", "Add Connection");
                return;
            }
            ConnectionName = ConnectionNameTxt.Text;
            Address = AddressTxt.Text;
            if (PublisherDataType.SelectedIndex == 0)
            {
                PublisherId = PublisherIdTxt.Text;
            }
            else if (PublisherDataType.SelectedIndex == 1)
            {
                byte PublisherIdbyteType;
                if (!byte.TryParse(PublisherIdTxt.Text, out PublisherIdbyteType))
                {
                    ShowErrorMessage("Publisher Id value doesn't match for the selected DataType.", "Add Connection");
                    return;
                }
                PublisherId = PublisherIdbyteType;
            }
            else if (PublisherDataType.SelectedIndex == 2)
            {
                ushort PublisherIdType;
                if (!ushort.TryParse(PublisherIdTxt.Text, out PublisherIdType))
                {
                    ShowErrorMessage("Publisher Id value doesn't match for the selected DataType.", "Add Connection");
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if (PublisherDataType.SelectedIndex == 3)
            {
                uint PublisherIdType;
                if (!uint.TryParse(PublisherIdTxt.Text, out PublisherIdType))
                {
                    ShowErrorMessage("Publisher Id value doesn't match for the selected DataType.", "Add Connection");
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if (PublisherDataType.SelectedIndex == 4)
            {
                ulong PublisherIdType;
                if (!ulong.TryParse(PublisherIdTxt.Text, out PublisherIdType))
                {
                    ShowErrorMessage("Publisher Id value doesn't match for the selected DataType.", "Add Connection");
                    return;
                }
                PublisherId = PublisherIdType;
            }
            else if (PublisherDataType.SelectedIndex == 5)
            {
                Guid PublisherIdType;

                if (!Guid.TryParse(PublisherIdTxt.Text, out PublisherIdType))
                {
                    ShowErrorMessage("Publisher Id value doesn't match for the selected DataType.", "Add Connection");
                    return;
                }
                PublisherId = PublisherIdType;
            }

            if (ConnectionTypeCmb.SelectedIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(ResourceUritext.Text))
                {
                    ShowErrorMessage("Resource Uri cannot be empty.", "Add Connection");
                    return;
                }

                if (string.IsNullOrWhiteSpace(authenticationProfileUriText.Text))
                {
                    ShowErrorMessage("Authentication Profile Uri cannot be empty.", "Add Connection");
                    return;
                }
                ResourceUri = ResourceUritext.Text;
                AuthenticationProfileUri = authenticationProfileUriText.Text;
            }

            if (ConnectionTypeCmb.SelectedIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(DiscoveryAddressTxt.Text))
                {
                    ShowErrorMessage("Discovery Address cannot be empty.", "Add Connection");
                    return;
                }
                if (string.IsNullOrWhiteSpace(DiscoveryNetworkInterfaceText.Text))
                {
                    ShowErrorMessage("Discovery Network Interface cannot be empty.", "Add Connection");
                    return;
                }
                DiscoveryUrl = DiscoveryAddressTxt.Text;
                DiscoveryNetworkInterface = DiscoveryNetworkInterfaceText.Text;
            }

            if (string.IsNullOrWhiteSpace(ConnectionNameTxt.Text))
            {
                ShowErrorMessage("Connection Name cannot be empty.", "Add Connection");
                return;
            }

            if (string.IsNullOrWhiteSpace(NetworkInterfaceText.Text))
            {
                ShowErrorMessage("Network Interface cannot be empty.", "Add Connection");
                return;
            }
            NetworkInterface = NetworkInterfaceText.Text;

            IsApplied = true;
            Close();
        }

        private void ShowErrorMessage(string errorMessage, string headerName)
        {
            MessageBox.Show(errorMessage, headerName);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsApplied = false;
            Close();
        }

        #endregion

        #region Constructors


        public AddConnectionView()
        {
            InitializeComponent();
            DataContext = ViewModel = new AddConnectionViewViewModel();
        }

        #endregion

        public bool IsApplied;
        public string ConnectionName = string.Empty;
        public string Address = string.Empty;
        public object PublisherId;
        public string ConnectionType;
        public int TransportProfile;
        public string NetworkInterface;
        public string DiscoveryUrl;
        public string DiscoveryNetworkInterface;
        public string ResourceUri;
        public string AuthenticationProfileUri;
        public AddConnectionViewViewModel ViewModel;

        private void TransportTypeCmb_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ComboBoxItem item = TransportTypeCmb.SelectedItem as ComboBoxItem;
            switch (TransportTypeCmb.SelectedIndex)
            {
                case 0:
                case 1:
                    ConnectionTypeCmb.SelectedIndex = 0;
                    break;
                case 2:
                case 3:
                case 4:
                case 5:
                    ConnectionTypeCmb.SelectedIndex = 1;
                    break;

            }
            TransportProfile = TransportTypeCmb.SelectedIndex;
        }

        private void ConnectionTypeCmb_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConnectionType = ConnectionTypeCmb.SelectedIndex.ToString();
            if (ResourceUriLabel != null)
            {
                if (ConnectionType == "0")
                {
                    ViewModel.TransportDatagramVisibility = Visibility.Visible;
                    ViewModel.TransportBrokerVisibility = Visibility.Collapsed;
                }
                else
                {
                    ViewModel.TransportDatagramVisibility = Visibility.Collapsed;
                    ViewModel.TransportBrokerVisibility = Visibility.Visible;
                }
            }
        }
    }
}