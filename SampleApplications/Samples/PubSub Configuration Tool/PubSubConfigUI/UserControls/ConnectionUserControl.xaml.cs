using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for ConnectionUserControl.xaml
    /// </summary>
    public partial class ConnectionUserControl : UserControl
    {
        #region Constructors

        public ConnectionUserControl( )
        {
            InitializeComponent( );
            ConnectionEditViewModel = new ConnectionEditViewModel( );

            DataContext = ConnectionEditViewModel;
        }

        #endregion

        public ConnectionEditViewModel ConnectionEditViewModel;
    }
}