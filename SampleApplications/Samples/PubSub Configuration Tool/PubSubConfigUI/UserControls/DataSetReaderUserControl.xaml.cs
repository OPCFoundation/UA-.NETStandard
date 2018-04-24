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

        private readonly Dictionary<string, int> DicControlBitPositionmappping = new Dictionary<string, int>();

        private readonly Dictionary<string, int> DicNetworkMessageControlBitPositionmappping =
        new Dictionary<string, int>();
        private readonly Dictionary<string, int> m_uadpDataSetdicControlBitPositionmappping = new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_jsonDataSetdicControlBitPositionmappping = new Dictionary<string, int>();

        private readonly Dictionary<string, int> m_jsonNetworkdicControlBitPositionmappping = new Dictionary<string, int>();


        #endregion

        #region Private Methods

        private void ViewDataSetMeta_Click(object sender, RoutedEventArgs e)
        {
            if (DataSetReaderEditViewModel.DataSetMetaDataType == null)
            {
                MessageBox.Show("DataSet Metadata is not configured yet.", "View DataSet MetaData");
                return;
            }

            var _DataSetMetaDataUserControl = new DataSetMetaDataUserControl();
            _DataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Definition.DataSetMetaDataType =
            DataSetReaderEditViewModel.DataSetMetaDataType;
            _DataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Initialize();

            var _ViewDataSetMetaData = new ViewDataSetMetaData();

            _ViewDataSetMetaData.ContentControl.Content = _DataSetMetaDataUserControl;
            _ViewDataSetMetaData.ShowInTaskbar = false;
            _ViewDataSetMetaData.ShowDialog();
        }

        #endregion

        #region Constructors

        public DataSetReaderUserControl()
        {
            DataSetReaderEditViewModel = new DataSetReaderEditViewModel();
            DataContext = DataSetReaderEditViewModel;
            InitializeComponent();


            DicControlBitPositionmappping["Chk_box1"] = 1;
            DicControlBitPositionmappping["Chk_box2"] = 2;
            DicControlBitPositionmappping["Chk_box3"] = 4;
            DicControlBitPositionmappping["Chk_box4"] = 8;
            DicControlBitPositionmappping["Chk_box5"] = 16;
            DicControlBitPositionmappping["Chk_box6"] = 32;


            DicNetworkMessageControlBitPositionmappping["NM_Chk_box1"] = 1;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box2"] = 2;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box3"] = 4;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box4"] = 8;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box5"] = 16;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box6"] = 32;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box7"] = 64;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box8"] = 128;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box9"] = 256;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box10"] = 512;
            DicNetworkMessageControlBitPositionmappping["NM_Chk_box11"] = 1024;

            m_uadpDataSetdicControlBitPositionmappping["UDChk_box1"] = 1;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box2"] = 2;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box3"] = 4;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box4"] = 8;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box5"] = 16;
            m_uadpDataSetdicControlBitPositionmappping["UDChk_box6"] = 32;

            m_jsonDataSetdicControlBitPositionmappping["JNChk_box1"] = 1;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box2"] = 2;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box3"] = 4;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box4"] = 8;
            m_jsonDataSetdicControlBitPositionmappping["JNChk_box5"] = 16;

            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box1"] = 1;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box2"] = 2;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box3"] = 4;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box4"] = 8;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box5"] = 16;
            m_jsonNetworkdicControlBitPositionmappping["JN_Chk_box6"] = 32;
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Get the dataSet content Mask
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
                    var enumvalue = DicControlBitPositionmappping[checkbox.Name];
                    DataSetContentMask = DataSetContentMask | enumvalue;
                }
            }
            return DataSetContentMask;
        }
        /// <summary>
        /// Get the Network content mask
        /// </summary>

        public int GetNetworkContentMask()
        {
            var NetworkMessageContentMask = 0;
            //foreach (var checkbox in new[]
            //                          {
            //                              NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
            //                              NM_Chk_box6, NM_Chk_box7, NM_Chk_box8, NM_Chk_box9, NM_Chk_box10,
            //                              NM_Chk_box11
            //                          })
            //{
            //    var shiftNumber = 1;
            //    if (checkbox.IsChecked == true)
            //    {
            //        var bitposition = DicNetworkMessageControlBitPositionmappping[checkbox.Name];
            //        shiftNumber = 1 << bitposition;
            //        NetworkMessageContentMask = NetworkMessageContentMask | shiftNumber;
            //    }
            //}
            return NetworkMessageContentMask;
        }
        /// <summary>
        /// Initialize the DataSet Content Mask and Network content Mask controls
        /// </summary>
        public void InitializeContentmask()
        {
            foreach (var checkbox in new[]
                                      {
                                          Chk_box1, Chk_box2, Chk_box3, Chk_box4, Chk_box5, Chk_box6
                                      })
            {
                var enumvalue = DicControlBitPositionmappping[checkbox.Name];
                checkbox.IsChecked = (DataSetReaderEditViewModel.DataSetContentMask & enumvalue) == enumvalue ? true
                    : false;

            }


            if (DataSetReaderEditViewModel.MessageSetting == 0)
            {
                foreach (var checkbox in new[]
                                     {
                                          UDChk_box1, UDChk_box2, UDChk_box3, UDChk_box4, UDChk_box5,
                                          UDChk_box6
                                      })
                {
                    var enumvalue = m_uadpDataSetdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetReaderEditViewModel.UadpDataSetMessageContentMask & enumvalue) == enumvalue
                        ? true : false;

                    // checkbox.IsEnabled = false;
                }

                foreach (var checkbox in new[]
                                     {
                                          NM_Chk_box1, NM_Chk_box2, NM_Chk_box3, NM_Chk_box4, NM_Chk_box5,
                                          NM_Chk_box6,NM_Chk_box7,NM_Chk_box8,NM_Chk_box9,NM_Chk_box10,NM_Chk_box11
                                      })
                {
                    var enumvalue = DicNetworkMessageControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetReaderEditViewModel.UadpNetworkMessageContentMask & enumvalue) == enumvalue
                        ? true : false;

                    // checkbox.IsEnabled = false;
                }

            }
            else
            {
                foreach (var checkbox in new[]
                                     {
                                          JNChk_box1, JNChk_box2, JNChk_box3, JNChk_box4, JNChk_box5
                                      })
                {
                    var enumvalue = m_jsonDataSetdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetReaderEditViewModel.JsonDataSetMessageContentMask & enumvalue) == enumvalue
                        ? true : false;

                    // checkbox.IsEnabled = false;
                }

                foreach (var checkbox in new[]
                                     {
                                          JN_Chk_box1, JN_Chk_box2, JN_Chk_box3, JN_Chk_box4, JN_Chk_box5,
                                          JN_Chk_box6
                                      })
                {
                    var enumvalue = m_jsonNetworkdicControlBitPositionmappping[checkbox.Name];
                    checkbox.IsChecked = (DataSetReaderEditViewModel.JsonNetworkMessageContentMask & enumvalue) == enumvalue
                        ? true : false;

                    // checkbox.IsEnabled = false;
                }
            }

        }

        #endregion

        public DataSetReaderEditViewModel DataSetReaderEditViewModel;

        private void TransportSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void MessageSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}