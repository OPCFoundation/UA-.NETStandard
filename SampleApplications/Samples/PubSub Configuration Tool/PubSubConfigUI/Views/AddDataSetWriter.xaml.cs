using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetWriter.xaml
    /// </summary>
    public partial class AddDataSetWriter : Window
    {
        #region Private Member 

        private readonly Dictionary<string, int> _dicControlBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _uadpdicControlBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _jsondicControlBitPositionmappping = new Dictionary<string, int>();

        #endregion

        #region Private Methods

        private void OnCanecelClick(object sender, RoutedEventArgs e)
        {
            _isApplied = false;
            Close();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DataSetWriterNameTxt.Text))
            {
                MessageBox.Show("DataSet Writer Name cannot be empty.", "Add DataSet Writer");
                return;
            }
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumvalue = _dicControlBitPositionmappping[checkbox.Name];
                    _dataSetContentMask = _dataSetContentMask | enumvalue;

                }
            }
            if (_dataSetWriterViewModel.MessageSetting == 0)
            {
                foreach (var checkbox in new[]
                                     {
                                          UadpChk_box1, UadpChk_box2, UadpChk_box3, UadpChk_box4, UadpChk_box5, UadpChk_box6
                                      })
                {
                    if (checkbox.IsChecked == true)
                    {
                        var enumvalue = _uadpdicControlBitPositionmappping[checkbox.Name];
                        _uadpdataSetMessageContentMask = _uadpdataSetMessageContentMask | enumvalue;
                    }
                }
            }
            else
            {
                foreach (var checkbox in new[]
                                     {
                                          JsonChk_box1, JsonChk_box2, JsonChk_box3, JsonChk_box4, JsonChk_box5
                                      })
                {
                    if (checkbox.IsChecked == true)
                    {
                        var enumvalue = _jsondicControlBitPositionmappping[checkbox.Name];
                        _jsondataSetMessageContentMask = _jsondataSetMessageContentMask | enumvalue;
                    }
                }
            }
            _dataSetWriterViewModel.DataSetContentMask = _dataSetContentMask;
            _dataSetWriterViewModel.UadpDataSetMessageContentMask = _uadpdataSetMessageContentMask;
            _dataSetWriterViewModel.JsonDataSetMessageContentMask = _jsondataSetMessageContentMask;
            _isApplied = true;

            Close();
        }

        private void OnGetPublisherId(object sender, RoutedEventArgs e)
        {
            var getPublisherIdDialog = new GetPublisherIdDialog();
            getPublisherIdDialog.Closing += _GetPublisherIdDialog_Closing;
            getPublisherIdDialog.ShowInTaskbar = false;
            getPublisherIdDialog.ShowDialog();
        }

        private void _GetPublisherIdDialog_Closing(object sender, CancelEventArgs e)
        {
            var getPublisherIdDialog = sender as GetPublisherIdDialog;
            if (getPublisherIdDialog != null && getPublisherIdDialog._isApplied)
            {
                var publishedDataSetDefinition =
                getPublisherIdDialog.PublisherDataGrid.SelectedItem as PublishedDataSetDefinition;
                if (publishedDataSetDefinition != null)
                {
                    _dataSetWriterViewModel.PublisherDataSetId =
                    publishedDataSetDefinition.PublishedDataSetNodeId.ToString();
                    _dataSetWriterViewModel.PublisherDataSetNodeId = publishedDataSetDefinition.PublishedDataSetNodeId;
                    _dataSetWriterViewModel.DataSetName = publishedDataSetDefinition.Name;
                }
            }
        }

        #endregion

        #region Constructors

        public AddDataSetWriter()
        {
            DataContext = _dataSetWriterViewModel = new DataSetWriterViewModel();
            InitializeComponent();

            _dicControlBitPositionmappping["Chk_box1"] = 1;
            _dicControlBitPositionmappping["Chk_box2"] = 2;
            _dicControlBitPositionmappping["Chk_box3"] = 4;
            _dicControlBitPositionmappping["Chk_box4"] = 8;
            _dicControlBitPositionmappping["Chk_box5"] = 16;
            _dicControlBitPositionmappping["Chk_box6"] = 32;

            _uadpdicControlBitPositionmappping["UadpChk_box1"] = 1;
            _uadpdicControlBitPositionmappping["UadpChk_box2"] = 2;
            _uadpdicControlBitPositionmappping["UadpChk_box3"] = 4;
            _uadpdicControlBitPositionmappping["UadpChk_box4"] = 8;
            _uadpdicControlBitPositionmappping["UadpChk_box5"] = 16;
            _uadpdicControlBitPositionmappping["UadpChk_box6"] = 32;

            _jsondicControlBitPositionmappping["JsonChk_box1"] = 1;
            _jsondicControlBitPositionmappping["JsonChk_box2"] = 2;
            _jsondicControlBitPositionmappping["JsonChk_box3"] = 4;
            _jsondicControlBitPositionmappping["JsonChk_box4"] = 8;
            _jsondicControlBitPositionmappping["JsonChk_box5"] = 16;

        }

        #endregion

        public DataSetWriterViewModel _dataSetWriterViewModel;
        public bool _isApplied;
        public int _dataSetContentMask;
        public int _uadpdataSetMessageContentMask;
        public int _jsondataSetMessageContentMask;

        private void MessageSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MessageSelect.SelectedIndex == 0)
            {
                _dataSetWriterViewModel.MessageSetting = 0;
                _dataSetWriterViewModel.IsDatagramMessage = Visibility.Visible;
                _dataSetWriterViewModel.IsBrokerMessage = Visibility.Collapsed;
            }
            else
            {
                _dataSetWriterViewModel.MessageSetting = 1;
                _dataSetWriterViewModel.IsDatagramMessage = Visibility.Collapsed;
                _dataSetWriterViewModel.IsBrokerMessage = Visibility.Visible;
            }
        }

        private void TransportSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

            if (TransportSelect.SelectedIndex == 0)
            {
                _dataSetWriterViewModel.TransportSetting = 0;
                _dataSetWriterViewModel.IsBrokerTransport = Visibility.Collapsed;
                _dataSetWriterViewModel.IsDatagramTransport = Visibility.Visible;
            }
            else
            {
                _dataSetWriterViewModel.TransportSetting = 1;
                _dataSetWriterViewModel.IsBrokerTransport = Visibility.Visible;
                _dataSetWriterViewModel.IsDatagramTransport = Visibility.Collapsed;
            }

        }
    }
}