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

        private readonly Dictionary< string, int > _dicControlBitPositionmappping = new Dictionary< string, int >( );

        #endregion

        #region Private Methods

        private void OnCanecelClick( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        private void OnApplyClick( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( DataSetWriterNameTxt.Text ) )
            {
                MessageBox.Show( "DataSet Writer Name cannot be empty.", "Add DataSet Writer" );
                return;
            }
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = _dicControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    _dataSetContentMask = _dataSetContentMask | shiftNumber;
                }
            }

            _isApplied = true;

            Close( );
        }

        private void OnGetPublisherId( object sender, RoutedEventArgs e )
        {
            var getPublisherIdDialog = new GetPublisherIdDialog( );
            getPublisherIdDialog.Closing += _GetPublisherIdDialog_Closing;
            getPublisherIdDialog.ShowInTaskbar = false;
            getPublisherIdDialog.ShowDialog( );
        }

        private void _GetPublisherIdDialog_Closing( object sender, CancelEventArgs e )
        {
            var getPublisherIdDialog = sender as GetPublisherIdDialog;
            if ( getPublisherIdDialog != null && getPublisherIdDialog._isApplied )
            {
                var publishedDataSetDefinition =
                getPublisherIdDialog.PublisherDataGrid.SelectedItem as PublishedDataSetDefinition;
                if ( publishedDataSetDefinition != null )
                {
                    _dataSetWriterViewModel.PublisherDataSetId =
                    publishedDataSetDefinition.PublishedDataSetNodeId.ToString( );
                    _dataSetWriterViewModel.PublisherDataSetNodeId = publishedDataSetDefinition.PublishedDataSetNodeId;
                }
            }
        }

        #endregion

        #region Constructors

        public AddDataSetWriter( )
        {
            InitializeComponent( );
            DataContext = _dataSetWriterViewModel = new DataSetWriterViewModel( );
            _dicControlBitPositionmappping[ "Chk_box1" ] = 0;
            _dicControlBitPositionmappping[ "Chk_box2" ] = 1;
            _dicControlBitPositionmappping[ "Chk_box3" ] = 2;
            _dicControlBitPositionmappping[ "Chk_box4" ] = 3;
            _dicControlBitPositionmappping[ "Chk_box5" ] = 4;
            _dicControlBitPositionmappping[ "Chk_box6" ] = 5;
            _dicControlBitPositionmappping[ "Chk_box7" ] = 16;
            _dicControlBitPositionmappping[ "Chk_box8" ] = 17;
            _dicControlBitPositionmappping[ "Chk_box9" ] = 18;
            _dicControlBitPositionmappping[ "Chk_box10" ] = 19;
            _dicControlBitPositionmappping[ "Chk_box11" ] = 20;
            _dicControlBitPositionmappping[ "Chk_box12" ] = 21;
        }

        #endregion

        public DataSetWriterViewModel _dataSetWriterViewModel;
        public bool _isApplied;
        public int _dataSetContentMask;
    }
}