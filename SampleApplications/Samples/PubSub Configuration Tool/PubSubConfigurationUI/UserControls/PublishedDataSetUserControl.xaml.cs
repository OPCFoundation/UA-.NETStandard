using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PublishedDataSetUserControl.xaml
    /// </summary>
    public partial class PublishedDataSetUserControl : UserControl
    {
        #region Constructors

        public PublishedDataSetUserControl( )
        {
            InitializeComponent( );
            DataContext = PublishedDataSetEditViewModel = new PublishedDataSetEditViewModel( );
        }

        #endregion

        #region Public Property

        public PublishedDataSetEditViewModel PublishedDataSetEditViewModel { get; set; }

        #endregion
    }
}