using System.Collections.Generic;
using System.Windows;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetWriterGroup.xaml
    /// </summary>
    public partial class AddDataSetWriterGroup : Window
    {
        #region Private Member 

        private readonly Dictionary< string, int > _dicControlBitPositionmappping = new Dictionary< string, int >( );

        #endregion

        #region Private Methods

        private void AddApplyClick( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( GroupNameTxt.Text ) )
            {
                MessageBox.Show( "Writer Group Name cannot be empty.", "Add Writer Group" );
                return;
            }

            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = _dicControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    _networkMessageContentMask = _networkMessageContentMask | shiftNumber;
                }
            }

            _isApplied = true;

            Close( );
        }

        private void OnCanecelClick( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public AddDataSetWriterGroup( )
        {
            InitializeComponent( );
            DataContext = _dataSetGroupViewModel = new DataSetWriterGroupViewModel( );
            _dicControlBitPositionmappping[ "Chk_box1" ] = 0;
            _dicControlBitPositionmappping[ "Chk_box2" ] = 1;
            _dicControlBitPositionmappping[ "Chk_box3" ] = 2;
            _dicControlBitPositionmappping[ "Chk_box4" ] = 3;
            _dicControlBitPositionmappping[ "Chk_box5" ] = 4;
            _dicControlBitPositionmappping[ "Chk_box6" ] = 5;
            _dicControlBitPositionmappping[ "Chk_box7" ] = 6;
            _dicControlBitPositionmappping[ "Chk_box8" ] = 7;
            _dicControlBitPositionmappping[ "Chk_box9" ] = 8;
            _dicControlBitPositionmappping[ "Chk_box10" ] = 9;
            _dicControlBitPositionmappping[ "Chk_box11" ] = 10;
            _dicControlBitPositionmappping[ "Chk_box12" ] = 11;
        }

        #endregion

        public DataSetWriterGroupViewModel _dataSetGroupViewModel;
        public bool _isApplied;
        public int _networkMessageContentMask;
    }
}