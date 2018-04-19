using System.Windows;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for GetPublisherIdDialog.xaml
    /// </summary>
    public partial class GetPublisherIdDialog : Window
    {
        #region Private Methods

        private void GetPublisherIdDialog_Loaded( object sender, RoutedEventArgs e )
        {
            foreach ( var publishedDataSetBase in MainViewModel
            ._PublisherDataSetView.ViewModel.PublishedDataSetCollection )
                PublisherDataGrid.Items.Add( publishedDataSetBase as PublishedDataSetDefinition );
        }

        private void OnApply_Click( object sender, RoutedEventArgs e )
        {
            if ( PublisherDataGrid.SelectedItem == null )
            {
                MessageBox.Show( "No Publisher DataSet selected", "Get Publisher DataSet Id" );
                return;
            }
            _isApplied = true;
            Close( );
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }

        #endregion

        #region Constructors

        public GetPublisherIdDialog( )
        {
            InitializeComponent( );
            Loaded += GetPublisherIdDialog_Loaded;
        }

        #endregion

        public bool _isApplied;
    }
}