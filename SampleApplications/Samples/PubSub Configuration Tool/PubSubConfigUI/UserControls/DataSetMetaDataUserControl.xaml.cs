using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PublishedDataItemUserControl.xaml
    /// </summary>
    public partial class DataSetMetaDataUserControl : UserControl
    {
        #region Constructors

        public DataSetMetaDataUserControl( )
        {
            InitializeComponent( );
            DataContext = DataSetMetaDataEditViewModel = new DataSetMetaDataEditViewModel( );
        }

        #endregion

        #region Public Property

        public DataSetMetaDataEditViewModel DataSetMetaDataEditViewModel { get; set; }

        #endregion
    }
}