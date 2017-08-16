using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PublishedDataItemUserControl.xaml
    /// </summary>
    public partial class PublishedDataItemUserControl : UserControl
    {
        #region Constructors

        public PublishedDataItemUserControl( )
        {
            InitializeComponent( );
            DataContext = PublishedDataItemEditViewModel = new PublishedDataItemSetModel( );
        }

        #endregion

        #region Public Property

        public PublishedDataItemSetModel PublishedDataItemEditViewModel { get; set; }

        #endregion
    }
}