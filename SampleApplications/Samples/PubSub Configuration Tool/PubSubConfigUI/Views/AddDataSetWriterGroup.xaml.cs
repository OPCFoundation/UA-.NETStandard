using System.Collections.Generic;
using System.Windows;
using PubSubConfigurationUI.ViewModels;
using System.Text.RegularExpressions;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetWriterGroup.xaml
    /// </summary>
    public partial class AddDataSetWriterGroup : Window
    {
        #region Private Member 

        private readonly Dictionary<string, int> _dicControlUadpBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _dicControlJsonBitPositionmappping = new Dictionary<string, int>();


        #endregion

        #region Private Methods

        private void AddApplyClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GroupNameTxt.Text))
            {
                MessageBox.Show("Writer Group Name cannot be empty.", "Add Writer Group");
                return;
            }

            if (_dataSetGroupViewModel.MessageSetting == 0)
            {

                foreach (var checkbox in new[]
                                          {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      })
                {
                    if (checkbox.IsChecked == true)
                    {
                        var enumvalue = _dicControlUadpBitPositionmappping[checkbox.Name];
                        _uadpNetworkMessageContentMask = _uadpNetworkMessageContentMask | enumvalue;
                        _dataSetGroupViewModel.UadpNetworkMessageContentMask = _uadpNetworkMessageContentMask;
                    }
                }
            }
            else
            {
                foreach (var checkbox in new[]
                                         {
                                          Chk_box21, Chk_box22, Chk_box23, Chk_box24, Chk_box25, Chk_box26
                                      })
                {
                    if (checkbox.IsChecked == true)
                    {
                        var enumvalue = _dicControlJsonBitPositionmappping[checkbox.Name];
                        _jsonNetworkMessageContentMask = _jsonNetworkMessageContentMask | enumvalue;
                        _dataSetGroupViewModel.JsonNetworkMessageContentMask = _jsonNetworkMessageContentMask;
                    }
                }
            }
            if (TransportSettings.SelectedIndex == 0)
            {
                _dataSetGroupViewModel.TransportSetting = TransportSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramTransport = Visibility.Visible;
                _dataSetGroupViewModel.IsBrokerTransport = Visibility.Collapsed;
            }
            else
            {
                _dataSetGroupViewModel.TransportSetting = TransportSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramTransport = Visibility.Collapsed;
                _dataSetGroupViewModel.IsBrokerTransport = Visibility.Visible;
            }

            _isApplied = true;

            Close();
        }

        private void OnCanecelClick(object sender, RoutedEventArgs e)
        {
            _isApplied = false;
            Close();
        }


        #endregion

        #region Constructors

        public AddDataSetWriterGroup()
        {
            DataContext = _dataSetGroupViewModel = new DataSetWriterGroupViewModel();
            InitializeComponent();

            _dicControlUadpBitPositionmappping["Chk_box1"] = 1;
            _dicControlUadpBitPositionmappping["Chk_box2"] = 2;
            _dicControlUadpBitPositionmappping["Chk_box3"] = 4;
            _dicControlUadpBitPositionmappping["Chk_box4"] = 8;
            _dicControlUadpBitPositionmappping["Chk_box5"] = 16;
            _dicControlUadpBitPositionmappping["Chk_box6"] = 32;
            _dicControlUadpBitPositionmappping["Chk_box7"] = 64;
            _dicControlUadpBitPositionmappping["Chk_box8"] = 128;
            _dicControlUadpBitPositionmappping["Chk_box9"] = 256;
            _dicControlUadpBitPositionmappping["Chk_box10"] = 512;
            _dicControlUadpBitPositionmappping["Chk_box11"] = 1024;

            _dicControlJsonBitPositionmappping["Chk_box21"] = 1;
            _dicControlJsonBitPositionmappping["Chk_box22"] = 2;
            _dicControlJsonBitPositionmappping["Chk_box23"] = 4;
            _dicControlJsonBitPositionmappping["Chk_box24"] = 8;
            _dicControlJsonBitPositionmappping["Chk_box25"] = 16;
            _dicControlJsonBitPositionmappping["Chk_box26"] = 32;

        }

        #endregion

        public DataSetWriterGroupViewModel _dataSetGroupViewModel;
        public bool _isApplied;

        private void TransportSettings_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TransportSettings.SelectedIndex == 0)
            {
                _dataSetGroupViewModel.TransportSetting = TransportSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramTransport = Visibility.Visible;
                _dataSetGroupViewModel.IsBrokerTransport = Visibility.Collapsed;
            }
            else
            {
                _dataSetGroupViewModel.TransportSetting = TransportSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramTransport = Visibility.Collapsed;
                _dataSetGroupViewModel.IsBrokerTransport = Visibility.Visible;
            }
        }

        private void MessageSettings_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MessageSettings.SelectedIndex == 0)
            {
                _dataSetGroupViewModel.MessageSetting = MessageSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramMessage = Visibility.Visible;
                _dataSetGroupViewModel.IsBrokerMessage = Visibility.Collapsed;
            }
            else
            {
                _dataSetGroupViewModel.MessageSetting = MessageSettings.SelectedIndex;
                _dataSetGroupViewModel.IsDatagramMessage = Visibility.Collapsed;
                _dataSetGroupViewModel.IsBrokerMessage = Visibility.Visible;
            }
        }

        public int _uadpNetworkMessageContentMask;
        public int _jsonNetworkMessageContentMask;

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _dataSetGroupViewModel.MessageSecurityMode = MessageSecurity.SelectedIndex;
        }

        private void TextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}