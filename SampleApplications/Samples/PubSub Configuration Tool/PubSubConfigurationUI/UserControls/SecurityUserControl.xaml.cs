using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for SecurityGroupUserControl.xaml
    /// </summary>
    public partial class SecurityGroupUserControl : UserControl
    {
        #region Constructors

        public SecurityGroupUserControl( )
        {
            InitializeComponent( );
            SecurityGroupEditViewModel = new SecurityGroupEditViewModel( );
            DataContext = SecurityGroupEditViewModel;
        }

        #endregion

        public SecurityGroupEditViewModel SecurityGroupEditViewModel;
    }
}