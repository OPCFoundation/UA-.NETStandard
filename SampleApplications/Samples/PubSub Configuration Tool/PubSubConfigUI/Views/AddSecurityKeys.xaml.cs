using System.Collections.Generic;
using System.Windows;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddSecurityKeys.xaml
    /// </summary>
    public partial class AddSecurityKeys : Window
    {
        #region Private Methods

        private void AddFeaturekeys_click( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( FeatureKey.Text ) )
            {
                MessageBox.Show( "Future Key Name can not be Empty", "Security Keys Dialog", MessageBoxButton.OK );
                return;
            }
            if ( FeatureKey.Text == "Enter a new future key here" )
            {
                MessageBox.Show( "Enter the valid Future Key", "Security Keys Dialog", MessageBoxButton.OK );
                return;
            }
            _securityKeyViewModel.AddFeatureKeysList( FeatureKey.Text );
            FeatureKey.Text = string.Empty;
        }

      

        private void Remove_click( object sender, RoutedEventArgs e )
        {
            if ( FutuereKeysList.SelectedItems != null && FutuereKeysList.SelectedItems.Count > 0 )
            {
                var itemstoRemove = new List< string >( );
                foreach ( string key in FutuereKeysList.SelectedItems ) itemstoRemove.Add( key );
                foreach ( var key in itemstoRemove ) _securityKeyViewModel.RemoveFeatureKey( key );
            }
        }

        private void OnOK_click( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( securityPolicyUri.Text ) ||
                 string.IsNullOrWhiteSpace( currentTokenId.Text ) || string.IsNullOrWhiteSpace( currentKey.Text ) ||
                 string.IsNullOrWhiteSpace( timeToNextKey.Text ) || string.IsNullOrWhiteSpace( keyLifeTime.Text ) )
            {
                MessageBox.Show( "Enter all the Mandatory Fields", "Security Keys Dialog", MessageBoxButton.OK );
                return;
            }
            _isApplied = true;
            Close( );
        }

        private void OnCancel_click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public AddSecurityKeys( )
        {
            InitializeComponent( );
            DataContext = _securityKeyViewModel = new SecurityKeyViewModel( );
        }

        #endregion

        public SecurityKeyViewModel _securityKeyViewModel;
        public bool _isApplied;
    }
}