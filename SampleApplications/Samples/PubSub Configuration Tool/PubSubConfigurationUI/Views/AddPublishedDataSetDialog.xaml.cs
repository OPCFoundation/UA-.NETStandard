using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for AddPublishedDataSetDialog.xaml
    /// </summary>
    public partial class AddPublishedDataSetDialog : Window
    {
        #region Private Methods

        private void Rebrowse_Click( object sender, RoutedEventArgs e )
        {
            var node = BrowsedItems.SelectedItem as TreeViewNode;
            AddPublishedDataSetViewModel.Rebrowse( ref node );
        }

        private void PublishedDataItemUserControl_Loaded( object sender, RoutedEventArgs e )
        {
            PubDataSetUserControl.PublishedDataItemEditViewModel.Initialize( );
            PubDataSetUserControl.PublishedDataItemEditViewModel.IsEnabled = true;
        }

        private void AddVariable( object sender, RoutedEventArgs e )
        {
            var publishedDataSetItemDefinition = new PublishedDataSetItemDefinition( null );
            publishedDataSetItemDefinition.Name = PubDataSetUserControl.PublishedDataItemEditViewModel.PublishVariable;
            publishedDataSetItemDefinition.PublishVariableNodeId =
            PubDataSetUserControl.PublishedDataItemEditViewModel.PublishVariableNodeId;
            //   _PublishedDataSetItemDefinition.Attribute= PubDataSetUserControl.PublishedDataItemEditViewModel.Attribute
            publishedDataSetItemDefinition.SamplingInterval =
            PubDataSetUserControl.PublishedDataItemEditViewModel.SamplingInterval;
            //deadbandType
            publishedDataSetItemDefinition.DeadbandValue =
            PubDataSetUserControl.PublishedDataItemEditViewModel.DeadbandValue;
            publishedDataSetItemDefinition.Indexrange =
            PubDataSetUserControl.PublishedDataItemEditViewModel.Indexrange;
            if ( !string.IsNullOrWhiteSpace( PubDataSetUserControl.PublishedDataItemEditViewModel.SubstituteValue ) )
                publishedDataSetItemDefinition.SubstituteValue =
                new Variant( PubDataSetUserControl.PublishedDataItemEditViewModel.SubstituteValue );
            publishedDataSetItemDefinition.FieldMetaDataProperties = PubDataSetUserControl
            .PublishedDataItemEditViewModel.FieldMetaDataProperties;
            AddPublishedDataSetViewModel.AddVariable( publishedDataSetItemDefinition );
        }

        private void OnVariableDeleteClick( object sender, MouseButtonEventArgs e )
        {
            var node = (sender as Image).DataContext as PublishedDataSetItemDefinition;
            AddPublishedDataSetViewModel.RemoveVariable( node );
        }

        private void BrowsedItems_SelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            var node = BrowsedItems.SelectedItem as TreeViewNode;
            if ( node != null )
            {
                PubDataSetUserControl.PublishedDataItemEditViewModel.Definition.PublishVariable =
                PubDataSetUserControl.PublishedDataItemEditViewModel.Definition.Name =
                PubDataSetUserControl.PublishedDataItemEditViewModel.PublishVariable = node.Reference.DisplayName;
                PubDataSetUserControl.PublishedDataItemEditViewModel.PublishVariableNodeId = node.Reference.NodeId;
                PubDataSetUserControl.PublishedDataItemEditViewModel.Definition.PublishVariableNodeId =
                node.Reference.NodeId;
            }
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        private void OnAdd_Click( object sender, RoutedEventArgs e )
        {
            //Do validation.
            if ( AddPublishedDataSetViewModel.PublisherNameVisibility == Visibility.Visible )
                if ( string.IsNullOrWhiteSpace( PublisherName.Text ) )
                {
                    MessageBox.Show( "Publisher Name cannot be empty.", "Add PublishedDataSet" );
                    return;
                }
            _isApplied = true;
            Close( );
        }

        #endregion

        #region Constructors

        public AddPublishedDataSetDialog(
            IOPCUAClientAdaptor opcuaClientAdaptor, TreeViewNode rootNode, Visibility publisherNameVisibility )
        {
            InitializeComponent( );

            DataContext = AddPublishedDataSetViewModel =
            new AddPublishedDataSetViewModel( opcuaClientAdaptor, rootNode );
            AddPublishedDataSetViewModel.PublisherNameVisibility = publisherNameVisibility;
            AddPublishedDataSetViewModel.Initialize( );
        }

        #endregion

        #region Public Property

        public AddPublishedDataSetViewModel AddPublishedDataSetViewModel { get; set; }

        #endregion

        public bool _isApplied;
    }
}