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

        private readonly Dictionary<string, int> UadpDicControlBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> JsonDicControlBitPositionmappping = new Dictionary<string, int>();

        #endregion

        #region Constructors

        public DataSetWriterGroupUserControl()
        {
            InitializeComponent();
            DataSetWriterGroupEditViewModel = new DataSetWriterGroupEditViewModel();
            DataContext = DataSetWriterGroupEditViewModel;

            UadpDicControlBitPositionmappping["Chk_box1"] = 1;
            UadpDicControlBitPositionmappping["Chk_box2"] = 2;
            UadpDicControlBitPositionmappping["Chk_box3"] = 4;
            UadpDicControlBitPositionmappping["Chk_box4"] = 8;
            UadpDicControlBitPositionmappping["Chk_box5"] = 16;
            UadpDicControlBitPositionmappping["Chk_box6"] = 32;
            UadpDicControlBitPositionmappping["Chk_box7"] = 64;
            UadpDicControlBitPositionmappping["Chk_box8"] = 128;
            UadpDicControlBitPositionmappping["Chk_box9"] = 256;
            UadpDicControlBitPositionmappping["Chk_box10"] = 512;
            UadpDicControlBitPositionmappping["Chk_box11"] = 1024;

            JsonDicControlBitPositionmappping["Chk_box21"] = 1;
            JsonDicControlBitPositionmappping["Chk_box22"] = 2;
            JsonDicControlBitPositionmappping["Chk_box23"] = 4;
            JsonDicControlBitPositionmappping["Chk_box24"] = 8;
            JsonDicControlBitPositionmappping["Chk_box25"] = 16;
            JsonDicControlBitPositionmappping["Chk_box26"] = 32;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Get the Network content mask
        /// </summary>
        public int GetNetworkContentMask()
        {
            var NetworkMessageContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumvalue = UadpDicControlBitPositionmappping[checkbox.Name];
                    NetworkMessageContentMask = NetworkMessageContentMask | enumvalue;
                }
            }
            return NetworkMessageContentMask;
        }
        /// <summary>
        /// Initialize the Network content Mask controls
        /// </summary>
        public void InitializeContentmask()
        {
            if (DataSetWriterGroupEditViewModel.MessageSetting == 0)
            {


                foreach (var checkbox in new[]
                                          {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6, Chk_box7,
                                          Chk_box8, Chk_box9, Chk_box10, Chk_box11
                                      })
                {
                    var enumvalue = UadpDicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetWriterGroupEditViewModel.UadpNetworkMessageContentMask & enumvalue) ==
                                         enumvalue ? true : false;

                }
            }
            else
            {
                foreach (var checkbox in new[]
                                          {
                                          Chk_box21, Chk_box22, Chk_box23, Chk_box24, Chk_box25, Chk_box26
                                      })
                {
                    var enumvalue = JsonDicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetWriterGroupEditViewModel.JsonNetworkMessageContentMask & enumvalue) ==
                                         enumvalue ? true : false;

                }
            }
        }

        #endregion

        public DataSetWriterGroupEditViewModel DataSetWriterGroupEditViewModel;

        private void TransportSettings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TransportSettings.SelectedIndex == 0)
            {
                DataSetWriterGroupEditViewModel.TransportSetting = 0;
            }
            else
            {
                DataSetWriterGroupEditViewModel.TransportSetting = 1;
            }
        }

        private void MessageSettings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MessageSettings.SelectedIndex == 0)
            {
                DataSetWriterGroupEditViewModel.MessageSetting = 0;
            }
            else
            {
                DataSetWriterGroupEditViewModel.MessageSetting = 1;
            }
        }
    }
}