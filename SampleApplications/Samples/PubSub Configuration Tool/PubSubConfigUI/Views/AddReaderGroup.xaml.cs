using System.Windows;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddReaderGroup.xaml
    /// </summary>
    public partial class AddReaderGroup : Window
    {
        #region Private Methods

        private void OnApplyClick( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( GroupNameTxt.Text ) )
            {
                MessageBox.Show( "Reader Group Name cannot be empty.", "Add Reader Group" );
                return;
            }

            _isApplied = true;

            Close( );
        }

        private void OnCancelClick( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public AddReaderGroup( )
        {
            InitializeComponent( );
            DataContext = _readerGroupViewModel = new ReaderGroupViewModel( );
        }

        #endregion

        public ReaderGroupViewModel _readerGroupViewModel;
        public bool _isApplied;
    }
}