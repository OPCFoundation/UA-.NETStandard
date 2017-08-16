using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAdaptor;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PublisherDataSetView.xaml
    /// </summary>
    public partial class PublishedDataSetView : UserControl
    {
        #region Private Member 

        private readonly DataSetMetaDataUserControl _dataSetMetaDataUserControl;
        private readonly PublishedDataItemUserControl _publishedDataItemUserControl;
        private readonly PublishedDataSetUserControl _publishedDataSetUserControl;
        private PublishedDataSetMainViewModel _viewModel;

        #endregion

        #region Private Methods

        private void UIElement_OnMouseRightButtonDown( object sender, MouseButtonEventArgs e )
        {
            if ( e.ChangedButton == MouseButton.Right && !(e.OriginalSource is Image) &&
                 !(e.OriginalSource is TextBlock) && !(e.OriginalSource is Border) )
            {
                ViewModel.IsAddEventsVisible = Visibility.Collapsed;
                ViewModel.IsAddVariableVisible = Visibility.Collapsed;
                ViewModel.IsPublishedDataSetVisible = Visibility.Visible;
                ViewModel.IsPublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariableVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariablesVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;

                return;
            }
            UpdateMenuandButtonVisibility( );
        }

        private void UpdateMenuandButtonVisibility( )
        {
            if ( DataSetTreeView.ContextMenu != null ) DataSetTreeView.ContextMenu.Visibility = Visibility.Visible;

            if ( DataSetTreeView.SelectedItem is PublishedDataSetDefinition )
            {
                ViewModel.IsAddEventsVisible = Visibility.Collapsed;
                ViewModel.IsAddVariableVisible = Visibility.Collapsed;
                ViewModel.IsPublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsPublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedDataSetVisible = Visibility.Visible;
                ViewModel.IsRemovePublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariableVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariablesVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( DataSetTreeView.SelectedItem is DataSetMetaDataDefinition )
            {
                ViewModel.IsAddEventsVisible = Visibility.Collapsed;
                ViewModel.IsAddVariableVisible = Visibility.Collapsed;
                ViewModel.IsPublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsPublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariableVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariablesVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( DataSetTreeView.SelectedItem is PublishedDataSetItemDefinition )
            {
                ViewModel.IsAddEventsVisible = Visibility.Collapsed;
                ViewModel.IsAddVariableVisible = Visibility.Collapsed;
                ViewModel.IsPublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsPublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariableVisible = Visibility.Visible;
                ViewModel.IsRemoveVariablesVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else
            {
                ViewModel.IsAddEventsVisible = Visibility.Collapsed;
                ViewModel.IsAddVariableVisible = Visibility.Visible;
                ViewModel.IsPublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsPublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedDataSetVisible = Visibility.Collapsed;
                ViewModel.IsRemovePublishedEventsVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariableVisible = Visibility.Collapsed;
                ViewModel.IsRemoveVariablesVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
        }

        private void DataSetTreeView_OnSelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            UpdateMenuandButtonVisibility( );

            if ( DataSetTreeView.SelectedItem is PublishedDataSetDefinition )
            {
                _publishedDataSetUserControl.PublishedDataSetEditViewModel.Definition =
                DataSetTreeView.SelectedItem as PublishedDataSetDefinition;
                _publishedDataSetUserControl.PublishedDataSetEditViewModel.Initialize( );
                PubSubContentControl.Content = _publishedDataSetUserControl;
            }
            else if ( DataSetTreeView.SelectedItem is DataSetMetaDataDefinition )
            {
                _dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Definition =
                DataSetTreeView.SelectedItem as DataSetMetaDataDefinition;
                _dataSetMetaDataUserControl.DataSetMetaDataEditViewModel.Initialize( );
                PubSubContentControl.Content = _dataSetMetaDataUserControl;
            }
            else if ( DataSetTreeView.SelectedItem is PublishedDataSetItemDefinition )
            {
                _publishedDataItemUserControl.PublishedDataItemEditViewModel.Definition =
                DataSetTreeView.SelectedItem as PublishedDataSetItemDefinition;
                _publishedDataItemUserControl.PublishedDataItemEditViewModel.Initialize( );
                PubSubContentControl.Content = _publishedDataItemUserControl;
            }
            else
            {
                PubSubContentControl.Content = null;
            }
        }

        private void AddPublishedDataSet_Click( object sender, RoutedEventArgs e )
        {
            ViewModel.AddPublishedDataSet( );
        }

        private void AddVariable_Click( object sender, RoutedEventArgs e )
        {
            ViewModel.AddVariable( DataSetTreeView.SelectedItem as PublishedDataDefinition );
        }

        private void RemoveVariable_Click( object sender, RoutedEventArgs e )
        {
            if ( DataSetTreeView.SelectedItem is PublishedDataSetItemDefinition )
            {
                var _PublishedDataSetDefinition = (DataSetTreeView.SelectedItem as PublishedDataSetItemDefinition)
                .ParentNode.ParentNode as PublishedDataSetDefinition;
                var variableIndexs = new List< uint >( );
                uint count = 0;
                foreach ( PublishedDataSetItemDefinition _PublishedDataSetItem in
                    (DataSetTreeView.SelectedItem as PublishedDataSetItemDefinition).ParentNode.Children )
                    if ( _PublishedDataSetItem == DataSetTreeView.SelectedItem ) variableIndexs.Add( count++ );
                if ( variableIndexs.Count > 0 )
                    ViewModel.RemoveVariables(
                        (DataSetTreeView.SelectedItem as PublishedDataSetItemDefinition).ParentNode,
                        _PublishedDataSetDefinition, _PublishedDataSetDefinition.ConfigurationVersionDataType,
                        variableIndexs );
            }
        }

        private void RemoveVariables_Click( object sender, RoutedEventArgs e )
        {
            if ( DataSetTreeView.SelectedItem is PublishedDataDefinition )
            {
                var _PublishedDataSetDefinition =
                (DataSetTreeView.SelectedItem as PublishedDataDefinition).ParentNode as PublishedDataSetDefinition;
                var variableIndexs = new List< uint >( );
                uint count = 0;
                foreach ( PublishedDataSetItemDefinition _PublishedDataSetItem in
                    (DataSetTreeView.SelectedItem as PublishedDataDefinition).Children ) variableIndexs.Add( count++ );
                if ( variableIndexs.Count > 0 )
                    ViewModel.RemoveVariables( DataSetTreeView.SelectedItem as PublishedDataDefinition,
                                               _PublishedDataSetDefinition,
                                               _PublishedDataSetDefinition.ConfigurationVersionDataType,
                                               variableIndexs );
            }
        }

        private void RemovePublishedDataSet_Click( object sender, RoutedEventArgs e )
        {
            if ( DataSetTreeView.SelectedItem is PublishedDataSetDefinition )
            {
                var _PublishedDataSetDefinition = DataSetTreeView.SelectedItem as PublishedDataSetDefinition;
                ViewModel.RemovePublishedDataSet( _PublishedDataSetDefinition );
            }
        }

        private void AddPublishedEvents_Click( object sender, RoutedEventArgs e )
        {
        }

        private void AddEvents_Click( object sender, RoutedEventArgs e )
        {
        }

        private void RemoveEvents_Click( object sender, RoutedEventArgs e )
        {
        }

        private void RemovePublishedEvents_Click( object sender, RoutedEventArgs e )
        {
        }

        private void OnUpdate_Click( object sender, RoutedEventArgs e )
        {
        }

        private void OnCancel_Click( object sender, RoutedEventArgs e )
        {
        }

        private void Refresh_Click( object sender, RoutedEventArgs e )
        {
            ViewModel.Initialize( MainViewModel.Rootnode );
        }

        #endregion

        #region Constructors

        public PublishedDataSetView( OPCUAClientAdaptor opcuaClientAdaptor )
        {
            InitializeComponent( );
            ViewModel = new PublishedDataSetMainViewModel( opcuaClientAdaptor );
            DataContext = ViewModel;

            _publishedDataSetUserControl = new PublishedDataSetUserControl( );
            _dataSetMetaDataUserControl = new DataSetMetaDataUserControl( );
            _publishedDataItemUserControl = new PublishedDataItemUserControl( );
            UpdateMenuandButtonVisibility( );
        }

        #endregion

        #region Public Property

        public PublishedDataSetMainViewModel ViewModel
        {
            get { return _viewModel; }
            set { _viewModel = value; }
        }

        #endregion
    }
}