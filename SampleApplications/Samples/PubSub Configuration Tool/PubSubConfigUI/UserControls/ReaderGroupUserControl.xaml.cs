using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.UserControls
{
    /// <summary>
    ///   Interaction logic for ReaderGroupUserControl.xaml
    /// </summary>
    public partial class ReaderGroupUserControl : UserControl
    {
        #region Constructors

        public ReaderGroupUserControl( )
        {
            InitializeComponent( );
            ReaderGroupEditViewModel = new ReaderGroupEditViewModel( );
            DataContext = ReaderGroupEditViewModel;
        }

        #endregion

        public ReaderGroupEditViewModel ReaderGroupEditViewModel;
    }
}