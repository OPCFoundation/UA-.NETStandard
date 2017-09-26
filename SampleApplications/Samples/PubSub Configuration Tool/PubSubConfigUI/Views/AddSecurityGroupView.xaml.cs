using System.Windows;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddSecurityGroupView.xaml
    /// </summary>
    public partial class AddSecurityGroupView : Window
    {
        #region Private Methods

        private void ButtonBase_OnClick( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( SecurityName.Text ) )
            {
                MessageBox.Show( "Security group name can not be empty", "Security Group Dialog", MessageBoxButton.OK );
                return;
            }
            _name = SecurityName.Text;

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

        public AddSecurityGroupView( )
        {
            InitializeComponent( );
        }

        #endregion

        public bool _isApplied;
        public string _name = string.Empty;
    }
}