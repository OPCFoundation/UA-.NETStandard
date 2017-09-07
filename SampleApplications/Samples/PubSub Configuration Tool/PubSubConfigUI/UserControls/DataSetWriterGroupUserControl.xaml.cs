using System.Collections.Generic;
using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetWriterGroupUserControl.xaml
    /// </summary>
    public partial class DataSetWriterGroupUserControl : UserControl
    {
        #region Private Member 

        private readonly Dictionary< string, int > DicControlBitPositionmappping = new Dictionary< string, int >( );

        #endregion

        #region Constructors

        public DataSetWriterGroupUserControl( )
        {
            InitializeComponent( );
            DataSetWriterGroupEditViewModel = new DataSetWriterGroupEditViewModel( );
            DataContext = DataSetWriterGroupEditViewModel;

            DicControlBitPositionmappping[ "Chk_box1" ] = 0;
            DicControlBitPositionmappping[ "Chk_box2" ] = 1;
            DicControlBitPositionmappping[ "Chk_box3" ] = 2;
            DicControlBitPositionmappping[ "Chk_box4" ] = 3;
            DicControlBitPositionmappping[ "Chk_box5" ] = 4;
            DicControlBitPositionmappping[ "Chk_box6" ] = 5;
            DicControlBitPositionmappping[ "Chk_box7" ] = 6;
            DicControlBitPositionmappping[ "Chk_box8" ] = 7;
            DicControlBitPositionmappping[ "Chk_box9" ] = 8;
            DicControlBitPositionmappping[ "Chk_box10" ] = 9;
            DicControlBitPositionmappping[ "Chk_box11" ] = 10;
            //   DicControlBitPositionmappping["Chk_box12"] = 11;
        }

        #endregion

        #region Public Methods

        public int GetNetworkContentMask( )
        {
            var NetworkMessageContentMask = 0;
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = DicControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    NetworkMessageContentMask = NetworkMessageContentMask | shiftNumber;
                }
            }
            return NetworkMessageContentMask;
        }

        public void InitializeContentmask( )
        {
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                var bitposition = DicControlBitPositionmappping[ checkbox.Name ];
                shiftNumber = 1 << bitposition;
                checkbox.IsChecked = (DataSetWriterGroupEditViewModel.NetworkMessageContentMask & shiftNumber) ==
                                     shiftNumber ? true : false;

                //checkbox.IsEnabled = false;
            }
        }

        #endregion

        public DataSetWriterGroupEditViewModel DataSetWriterGroupEditViewModel;
    }
}