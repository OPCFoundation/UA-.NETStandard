using System.Windows;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for ViewDataSetMetaData.xaml
    /// </summary>
    public partial class ViewDataSetMetaData : Window
    {
        #region Private Methods

        private void Button_Click( object sender, RoutedEventArgs e )
        {
            Close( );
        }

        #endregion

        #region Constructors

        public ViewDataSetMetaData( )
        {
            InitializeComponent( );
        }

        #endregion
    }
}