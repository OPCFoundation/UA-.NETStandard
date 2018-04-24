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

namespace PubSubConfigurationUI.UserControls
{
    /// <summary>
    ///   Interaction logic for DataSetWriterDefinition.xaml
    /// </summary>
    public partial class DataSetWriterUserControl : UserControl
    {
        #region Private Member 

        private readonly Dictionary<string, int> DicControlBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _uadpdicControlBitPositionmappping = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _jsondicControlBitPositionmappping = new Dictionary<string, int>();
        #endregion

        #region Constructors

        public DataSetWriterUserControl()
        {
            InitializeComponent();
            DataSetWriterEditViewModel = new DataSetWriterEditViewModel();
            DataContext = DataSetWriterEditViewModel;

            DicControlBitPositionmappping["Chk_box1"] = 1;
            DicControlBitPositionmappping["Chk_box2"] = 2;
            DicControlBitPositionmappping["Chk_box3"] = 4;
            DicControlBitPositionmappping["Chk_box4"] = 8;
            DicControlBitPositionmappping["Chk_box5"] = 16;
            DicControlBitPositionmappping["Chk_box6"] = 32;

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

        #region Public Methods
        /// <summary>
        /// Get the DataSet content mask
        /// </summary>
        public int GetDataSetContentMask()
        {
            var DataSetContentMask = 0;
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {
                if (checkbox.IsChecked == true)
                {
                    var enumValue = DicControlBitPositionmappping[checkbox.Name];
                    DataSetContentMask = DataSetContentMask | enumValue;
                }
            }
            return DataSetContentMask;
        }
        /// <summary>
        /// Initialize the DataSet content Mask controls
        /// </summary>
        public void InitializeContentmask()
        {
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {
                var enumValue = DicControlBitPositionmappping[checkbox.Name];
                checkbox.IsChecked = (DataSetWriterEditViewModel.DataSetContentMask & enumValue) == enumValue ? true
                    : false;

            }

            if (DataSetWriterEditViewModel.MessageSetting == 0)
            {
                foreach (var checkbox in new[]
                                                     {
                                          UadpChk_box1, UadpChk_box2, UadpChk_box3, UadpChk_box4, UadpChk_box5, UadpChk_box6
                                      })
                {
                    var enumValue = _uadpdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetWriterEditViewModel.UadpDataSetMessageContentMask & enumValue) == enumValue ? true
                        : false;

                }
            }
            else
            {
                foreach (var checkbox in new[]
                                     {
                                          JsonChk_box1, JsonChk_box2, JsonChk_box3, JsonChk_box4, JsonChk_box5
                                      })
                {
                    var enumValue = _jsondicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetWriterEditViewModel.JsonDataSetMessageContentMask & enumValue) == enumValue ? true
                        : false;

                }
            }
        }

        #endregion

        public DataSetWriterEditViewModel DataSetWriterEditViewModel;

        private void TransportSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void MessageSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}