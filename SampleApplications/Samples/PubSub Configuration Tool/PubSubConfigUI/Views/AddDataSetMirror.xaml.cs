using System.Windows;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddDataSetMirror.xaml
    /// </summary>
    public partial class AddDataSetMirror : Window
    {
        #region Event Handlers

        private void Apply_Click( object sender, RoutedEventArgs e )
        {
            if ( string.IsNullOrWhiteSpace( ParentNodeName.Text ) )
            {
                MessageBox.Show( "Parent Node name cannot be empty", "Add DataSet Mirror" );

                return;
            }
            IsApplied = true;
            Close( );
        }

        private void Cancel_Click( object sender, RoutedEventArgs e )
        {
            IsApplied = false;
            Close( );
        }

        #endregion

        #region Constructors

        public AddDataSetMirror( )
        {
            InitializeComponent( );
        }

        #endregion

        public bool IsApplied;
    }
}