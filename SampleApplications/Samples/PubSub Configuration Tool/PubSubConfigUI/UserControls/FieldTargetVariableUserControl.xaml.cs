using System.Windows;
using System.Windows.Controls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PublishedDataSetUserControl.xaml
    /// </summary>
    public partial class FieldTargetVariableUserControl : UserControl
    {
        #region Private Methods

        private void GetDataSetField_Click( object sender, RoutedEventArgs e )
        {
            FieldTargetVariableEditViewModel.GetDataSetFieldId( );
        }

        #endregion

        #region Constructors

        public FieldTargetVariableUserControl( )
        {
            InitializeComponent( );
            DataContext = FieldTargetVariableEditViewModel = new FieldTargetVariableEditViewModel( );
        }

        #endregion

        #region Public Property

        public FieldTargetVariableEditViewModel FieldTargetVariableEditViewModel { get; set; }

        #endregion
    }
}