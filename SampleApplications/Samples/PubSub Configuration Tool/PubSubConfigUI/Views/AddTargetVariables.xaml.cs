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
    ///   Interaction logic for AddDataSetReader.xaml
    /// </summary>
    public partial class AddTargetVariables : Window
    {
        #region Private Methods

        private void BrowsedItems_SelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            var node = BrowsedItems.SelectedItem as TreeViewNode;
            if ( node != null )
            {
                TargetVariableUserControl.FieldTargetVariableEditViewModel.TargetFieldNodeId = node.Reference.NodeId;
                TargetVariableUserControl.FieldTargetVariableEditViewModel.TargetNodeId = node.Reference.NodeId;
            }
            TargetVariableUserControl.FieldTargetVariableEditViewModel.Name = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.TargetFieldNodeId.Identifier.ToString( );
        }

        private void Apply_Click( object sender, RoutedEventArgs e )
        {
            //Validate here

            _isApplied = true;
            Close( );
        }

        private void Cancel_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        private void AddVariable( object sender, RoutedEventArgs e )
        {
            if ( TargetVariableUserControl.FieldTargetVariableEditViewModel.TargetFieldNodeId == null )
            {
                MessageBox.Show( "Please browse and select the target variable", "Add Target Variable" );
                return;
            }
            var fieldTargetVariableDefinition = new FieldTargetVariableDefinition( );
            fieldTargetVariableDefinition.Name = TargetVariableUserControl.FieldTargetVariableEditViewModel.Name;
            fieldTargetVariableDefinition.TargetFieldNodeId = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.TargetFieldNodeId;
            fieldTargetVariableDefinition.TargetNodeId = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.TargetFieldNodeId.ToString( );
            fieldTargetVariableDefinition.AttributeId = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.AttributeId;
            fieldTargetVariableDefinition.DataSetFieldId =
            TargetVariableUserControl.FieldTargetVariableEditViewModel.DataSetFieldId;
            fieldTargetVariableDefinition.OverrideValueHandling = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.OverrideValueHandling;
            fieldTargetVariableDefinition.OverrideValue = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.OverrideValue;
            fieldTargetVariableDefinition.ReceiverIndexRange = TargetVariableUserControl
            .FieldTargetVariableEditViewModel.ReceiverIndexRange;
            fieldTargetVariableDefinition.WriteIndexRange =
            TargetVariableUserControl.FieldTargetVariableEditViewModel.WriteIndexRange;
            _targetVariablesViewModel.AddVariable( fieldTargetVariableDefinition );
        }

        private void OnApply_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = true;
            Close( );
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
            _isApplied = false;
            Close( );
        }

        private void Rebrowse_Click( object sender, RoutedEventArgs e )
        {
            var node = BrowsedItems.SelectedItem as TreeViewNode;
            _targetVariablesViewModel.Rebrowse( ref node );
        }

        private void OnVariableDeleteClick( object sender, MouseButtonEventArgs e )
        {
            var node = (sender as Button).DataContext as FieldTargetVariableDefinition;
            _targetVariablesViewModel.RemoveVariable( node );
        }

        private void TargetDataItemUserControl_Loaded( object sender, RoutedEventArgs e )
        {
            TargetVariableUserControl.FieldTargetVariableEditViewModel.Initialize( );
        }

        #endregion

        #region Constructors

        public AddTargetVariables(
            IOPCUAClientAdaptor opcuaClientAdaptor, TreeViewNode rootNode, DataSetMetaDataType dataSetMetaDataType )
        {
            InitializeComponent( );
            DataContext = _targetVariablesViewModel = new TargetVariablesViewModel( opcuaClientAdaptor, rootNode );
            _targetVariablesViewModel.Initialize( );
            if ( dataSetMetaDataType != null && dataSetMetaDataType.ConfigurationVersion != null )
            {
                _targetVariablesViewModel.MinorVersion = ( int ) dataSetMetaDataType.ConfigurationVersion.MinorVersion;
                _targetVariablesViewModel.MajorVersion = ( int ) dataSetMetaDataType.ConfigurationVersion.MajorVersion;
            }
            TargetVariableUserControl.FieldTargetVariableEditViewModel.DataSetMetaDataType = dataSetMetaDataType;
        }

        #endregion

        public TargetVariablesViewModel _targetVariablesViewModel;
        public bool _isApplied;
    }
}