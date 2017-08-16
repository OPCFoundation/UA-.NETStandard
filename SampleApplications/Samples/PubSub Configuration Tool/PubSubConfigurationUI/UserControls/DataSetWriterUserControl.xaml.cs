using System.Collections.Generic;
using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.UserControls
{
    /// <summary>
    ///   Interaction logic for DataSetWriterDefinition.xaml
    /// </summary>
    public partial class DataSetWriterUserControl : UserControl
    {
        #region Private Member 

        private readonly Dictionary< string, int > DicControlBitPositionmappping = new Dictionary< string, int >( );

        #endregion

        #region Constructors

        public DataSetWriterUserControl( )
        {
            InitializeComponent( );
            DataSetWriterEditViewModel = new DataSetWriterEditViewModel( );
            DataContext = DataSetWriterEditViewModel;

            DicControlBitPositionmappping[ "Chk_box1" ] = 0;
            DicControlBitPositionmappping[ "Chk_box2" ] = 1;
            DicControlBitPositionmappping[ "Chk_box3" ] = 2;
            DicControlBitPositionmappping[ "Chk_box4" ] = 3;
            DicControlBitPositionmappping[ "Chk_box5" ] = 4;
            DicControlBitPositionmappping[ "Chk_box6" ] = 5;
            DicControlBitPositionmappping[ "Chk_box7" ] = 16;
            DicControlBitPositionmappping[ "Chk_box8" ] = 17;
            DicControlBitPositionmappping[ "Chk_box9" ] = 18;
            DicControlBitPositionmappping[ "Chk_box10" ] = 19;
            DicControlBitPositionmappping[ "Chk_box11" ] = 20;
            DicControlBitPositionmappping[ "Chk_box12" ] = 21;
        }

        #endregion

        #region Public Methods

        public int GetDataSetContentMask( )
        {
            var DataSetContentMask = 0;
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = DicControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    DataSetContentMask = DataSetContentMask | shiftNumber;
                }
            }
            return DataSetContentMask;
        }

        public void InitializeContentmask( )
        {
            foreach ( var checkbox in new[ ]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11, Chk_box12
                                      } )
            {
                var shiftNumber = 1;
                var bitposition = DicControlBitPositionmappping[ checkbox.Name ];
                shiftNumber = 1 << bitposition;
                checkbox.IsChecked = (DataSetWriterEditViewModel.DataSetContentMask & shiftNumber) == shiftNumber ? true
                    : false;

                // checkbox.IsEnabled = false;
            }
        }

        #endregion

        public DataSetWriterEditViewModel DataSetWriterEditViewModel;
    }
}