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
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.UserControls
{
    /// <summary>
    ///   Interaction logic for DataSetReaderUserControl.xaml
    /// </summary>
    public partial class DataSetReaderUserControl : UserControl
    {
        #region Private Member 

        private readonly Dictionary< string, int > DicControlBitPositionmappping = new Dictionary< string, int >( );

        private readonly Dictionary< string, int > DicNetworkMessageControlBitPositionmappping =
        new Dictionary< string, int >( );

        #endregion

        #region Private Methods

        private void ViewDataSetMeta_Click( object sender, RoutedEventArgs e )
        {
            if ( DataSetReaderEditViewModel.DataSetMetaDataType == null )
            {
                MessageBox.Show( "DataSet Metadata is not configured yet.", "View DataSet MetaData" );
                return;
            }

            var _DataSetMetaDataUserControl = new DataSetMetaDataUserControl( );
            _DataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Definition.DataSetMetaDataType =
            DataSetReaderEditViewModel.DataSetMetaDataType;
            _DataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Initialize( );

            var _ViewDataSetMetaData = new ViewDataSetMetaData( );

            _ViewDataSetMetaData.ContentControl.Content = _DataSetMetaDataUserControl;
            _ViewDataSetMetaData.ShowInTaskbar = false;
            _ViewDataSetMetaData.ShowDialog( );
        }

        #endregion

        #region Constructors

        public DataSetReaderUserControl( )
        {
            InitializeComponent( );
            DataSetReaderEditViewModel = new DataSetReaderEditViewModel( );
            DataContext = DataSetReaderEditViewModel;
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

            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box1" ] = 0;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box2" ] = 1;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box3" ] = 2;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box4" ] = 3;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box5" ] = 4;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box6" ] = 5;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box7" ] = 6;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box8" ] = 7;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box9" ] = 8;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box10" ] = 9;
            DicNetworkMessageControlBitPositionmappping[ "NM_Chk_box11" ] = 10;
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Get the dataSet content Mask
        /// </summary>
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
        /// <summary>
        /// Get the Network content mask
        /// </summary>
         
        public int GetNetworkContentMask( )
        {
            var NetworkMessageContentMask = 0;
            foreach ( var checkbox in new[ ]
                                      {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
                                          NM_Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                if ( checkbox.IsChecked == true )
                {
                    var bitposition = DicNetworkMessageControlBitPositionmappping[ checkbox.Name ];
                    shiftNumber = 1 << bitposition;
                    NetworkMessageContentMask = NetworkMessageContentMask | shiftNumber;
                }
            }
            return NetworkMessageContentMask;
        }
        /// <summary>
        /// Initialize the DataSet Content Mask and Network content Mask controls
        /// </summary>
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
                checkbox.IsChecked = (DataSetReaderEditViewModel.DataSetContentMask & shiftNumber) == shiftNumber ? true
                    : false;
                            
            }
            foreach ( var checkbox in new[ ]
                                      {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
                                          NM_Chk_box11
                                      } )
            {
                var shiftNumber = 1;
                var bitposition = DicNetworkMessageControlBitPositionmappping[ checkbox.Name ];
                shiftNumber = 1 << bitposition;
                checkbox.IsChecked = (DataSetReaderEditViewModel.NetworkMessageContentMask & shiftNumber) == shiftNumber
                    ? true : false;

                // checkbox.IsEnabled = false;
            }
        }

        #endregion

        public DataSetReaderEditViewModel DataSetReaderEditViewModel;
    }
}