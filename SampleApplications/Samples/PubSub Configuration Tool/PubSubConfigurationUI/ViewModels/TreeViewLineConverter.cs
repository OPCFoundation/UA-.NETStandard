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