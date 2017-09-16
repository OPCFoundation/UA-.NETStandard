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


using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace PubSubConfigurationUI.ViewModels
{
    public class TreeViewLineConverter : IValueConverter
    {
        #region Public Methods

        public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
        {
            var item = ( TreeViewItem ) value;
            var ic = ItemsControl.ItemsControlFromItemContainer( item );
            return ic.ItemContainerGenerator.IndexFromContainer( item ) == ic.Items.Count - 1;
        }

        public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
        {
            return false;
        }

        #endregion
    }
}