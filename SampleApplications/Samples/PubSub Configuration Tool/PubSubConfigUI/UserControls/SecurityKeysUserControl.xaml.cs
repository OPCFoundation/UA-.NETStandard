using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.UserControls
{
    /// <summary>
    ///   Interaction logic for SecurityKeysUserControl.xaml
    /// </summary>
    public partial class SecurityKeysUserControl : UserControl
    {
        #region Private Methods

        private void AddFeaturekeys_click( object sender, RoutedEventArgs e )
        {
            //  SecurityKeyEditViewModel.AddFeatureKeysList(Featurekey.Text);
        }

        private void Remove_click( object sender, RoutedEventArgs e )
        {
            if ( FutuereKeysList.SelectedItems != null && FutuereKeysList.SelectedItems.Count > 0 )
            {
                var ItemstoRemove = new List< string >( );
                foreach ( string key in FutuereKeysList.SelectedItems ) ItemstoRemove.Add( key );
                foreach ( var key in ItemstoRemove ) SecurityKeyEditViewModel.RemoveFeatureKey( key );
            }
        }

        #endregion

        #region Constructors

        public SecurityKeysUserControl( )
        {
            InitializeComponent( );
            SecurityKeyEditViewModel = new SecurityKeyEditViewModel( );
            DataContext = SecurityKeyEditViewModel;
        }

        #endregion

        public SecurityKeyEditViewModel SecurityKeyEditViewModel;
    }
}