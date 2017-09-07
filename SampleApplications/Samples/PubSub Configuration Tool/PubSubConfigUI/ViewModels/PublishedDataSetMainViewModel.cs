using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ClientAdaptor;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    public class PublishedDataSetMainViewModel : BaseViewModel
    {
        #region Private Member 

        private readonly IOPCUAClientAdaptor _ClientAdaptor;
        private Visibility _IsAddEventsVisible = Visibility.Collapsed;
        private Visibility _IsAddVariableVisible = Visibility.Collapsed;
        private Visibility _IsCancelVisible = Visibility.Collapsed;
        private Visibility _IsPublishedDataSetVisible = Visibility.Collapsed;
        private Visibility _IsPublishedEventsVisible = Visibility.Collapsed;
        private Visibility _IsRemoveEventsVisible = Visibility.Collapsed;
        private Visibility _IsRemovePublishedDataSetVisible = Visibility.Collapsed;
        private Visibility _IsRemovePublishedEventsVisible = Visibility.Collapsed;
        private Visibility _IsRemoveVariablesVisible = Visibility.Collapsed;
        private Visibility _IsRemoveVariableVisible = Visibility.Collapsed;
        private Visibility _IsUpdateVisible = Visibility.Collapsed;

        private ObservableCollection< PublishedDataSetBase > _PublishedDataSetCollection =
        new ObservableCollection< PublishedDataSetBase >( );

        private PublishedDataDefinition AddPublisherVaraibleParentNode;
        private TreeViewNode RootNode;

        #endregion

        #region Private Methods

        private void AddPublishedDataView_Closing( object sender, CancelEventArgs e )
        {
            var _AddPublishedData = sender as AddPublishedDataSetDialog;
            if ( _AddPublishedData._isApplied )
            {
                var _PublishedDataSetBase =
                _ClientAdaptor.AddPublishedDataSet( _AddPublishedData.PublisherName.Text,
                                                    _AddPublishedData
                                                    .AddPublishedDataSetViewModel.VariableListDefinitionCollection );
                if ( _PublishedDataSetBase != null ) PublishedDataSetCollection.Add( _PublishedDataSetBase );
            }
        }

        private void AddVariableDataView_Closing( object sender, CancelEventArgs e )
        {
            var _AddPublishedData = sender as AddPublishedDataSetDialog;
            if ( _AddPublishedData._isApplied )
            {
                var _PublishedDataSetDefinition =
                AddPublisherVaraibleParentNode.ParentNode as PublishedDataSetDefinition;
                ConfigurationVersionDataType _NewConfigurationVersion;
                var errMsg =
                _ClientAdaptor.AddVariableToPublisher( _PublishedDataSetDefinition.Name,
                                                       _PublishedDataSetDefinition.PublishedDataSetNodeId,
                                                       _PublishedDataSetDefinition.ConfigurationVersionDataType,
                                                       _AddPublishedData
                                                       .AddPublishedDataSetViewModel.VariableListDefinitionCollection,
                                                       out _NewConfigurationVersion );
                if ( !string.IsNullOrWhiteSpace( errMsg ) )
                {
                    MessageBox.Show( errMsg, "Add Variables" );
                    _AddPublishedData._isApplied = false;
                    e.Cancel = true;
                    return;
                }
                (AddPublisherVaraibleParentNode.ParentNode as PublishedDataSetDefinition).ConfigurationVersionDataType =
                _NewConfigurationVersion;
            }
        }

        #endregion

        #region Constructors

        public PublishedDataSetMainViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            _ClientAdaptor = OPCUAClientAdaptor;
            PublishedDataSetCollection = new ObservableCollection< PublishedDataSetBase >( );
        }

        #endregion

        #region Public Property

        public Visibility IsPublishedDataSetVisible
        {
            get { return _IsPublishedDataSetVisible; }
            set
            {
                _IsPublishedDataSetVisible = value;
                OnPropertyChanged( "IsPublishedDataSetVisible" );
            }
        }

        public Visibility IsPublishedEventsVisible
        {
            get { return _IsPublishedEventsVisible; }
            set
            {
                _IsPublishedEventsVisible = value;
                OnPropertyChanged( "IsPublishedEventsVisible" );
            }
        }

        public Visibility IsAddVariableVisible
        {
            get { return _IsAddVariableVisible; }
            set
            {
                _IsAddVariableVisible = value;
                OnPropertyChanged( "IsAddVariableVisible" );
            }
        }

        public Visibility IsRemoveVariablesVisible
        {
            get { return _IsRemoveVariablesVisible; }
            set
            {
                _IsRemoveVariablesVisible = value;
                OnPropertyChanged( "IsRemoveVariablesVisible" );
            }
        }

        public Visibility IsUpdateVisible
        {
            get { return _IsUpdateVisible; }
            set
            {
                _IsUpdateVisible = value;
                OnPropertyChanged( "IsUpdateVisible" );
            }
        }

        public Visibility IsCancelVisible
        {
            get { return _IsCancelVisible; }
            set
            {
                _IsCancelVisible = value;
                OnPropertyChanged( "IsCancelVisible" );
            }
        }

        public Visibility IsRemoveVariableVisible
        {
            get { return _IsRemoveVariableVisible; }
            set
            {
                _IsRemoveVariableVisible = value;
                OnPropertyChanged( "IsRemoveVariableVisible" );
            }
        }

        public Visibility IsRemovePublishedDataSetVisible
        {
            get { return _IsRemovePublishedDataSetVisible; }
            set
            {
                _IsRemovePublishedDataSetVisible = value;
                OnPropertyChanged( "IsRemovePublishedDataSetVisible" );
            }
        }

        public Visibility IsAddEventsVisible
        {
            get { return _IsAddEventsVisible; }
            set
            {
                _IsAddEventsVisible = value;
                OnPropertyChanged( "IsAddEventsVisible" );
            }
        }

        public Visibility IsRemoveEventsVisible
        {
            get { return _IsRemoveEventsVisible; }
            set
            {
                _IsRemoveEventsVisible = value;
                OnPropertyChanged( "IsRemoveEventsVisible" );
            }
        }

        public Visibility IsRemovePublishedEventsVisible
        {
            get { return _IsRemovePublishedEventsVisible; }
            set
            {
                _IsRemovePublishedEventsVisible = value;
                OnPropertyChanged( "IsRemovePublishedEventsVisible" );
            }
        }

        public ObservableCollection< PublishedDataSetBase > PublishedDataSetCollection
        {
            get { return _PublishedDataSetCollection; }
            set
            {
                _PublishedDataSetCollection = value;
                OnPropertyChanged( "PublishedDataSetCollection" );
            }
        }

        #endregion

        #region Public Methods

        public void AddPublishedDataSet( )
        {
            var _AddPublishedData = new AddPublishedDataSetDialog( _ClientAdaptor, RootNode, Visibility.Visible );
            _AddPublishedData.Closing += AddPublishedDataView_Closing;
            _AddPublishedData.PubDataSetUserControl.PublishedDataItemTxt.Visibility = Visibility.Collapsed;
            ;
            _AddPublishedData.ShowInTaskbar = false;
            _AddPublishedData.ShowDialog( );
        }

        public void AddVariable( PublishedDataDefinition ParentNode )
        {
            var _AddPublishedData = new AddPublishedDataSetDialog( _ClientAdaptor, RootNode, Visibility.Collapsed );
            _AddPublishedData.Closing += AddVariableDataView_Closing;
            AddPublisherVaraibleParentNode = ParentNode;
            _AddPublishedData.ShowDialog( );
        }

        public void Initialize( TreeViewNode rootNode )
        {
            RootNode = rootNode;
            //Browse and load the user interface here.
            PublishedDataSetCollection = _ClientAdaptor.GetPublishedDataSets( );
        }

        public void RemovePublishedDataSet( PublishedDataSetDefinition _PublishedDataSetDefinition )
        {
            var errMessage =
            _ClientAdaptor.RemovePublishedDataSet( _PublishedDataSetDefinition.PublishedDataSetNodeId );
            if ( !string.IsNullOrWhiteSpace( errMessage ) )
            {
                MessageBox.Show( errMessage, "Remove Published DataSet" );
                return;
            }
            PublishedDataSetCollection.Remove( _PublishedDataSetDefinition );
        }

        public void RemoveVariables(
            PublishedDataSetBase _PublishedDataSetBase, PublishedDataSetDefinition _PublishedDataSetDefinition,
            ConfigurationVersionDataType ConfigurationVersionDataType, List< uint > variableIndexs )
        {
            ConfigurationVersionDataType NewConfigurationVersion;
            var errmsg =
            _ClientAdaptor.RemovePublishedDataSetVariables( _PublishedDataSetDefinition.Name,
                                                            _PublishedDataSetDefinition.PublishedDataSetNodeId,
                                                            ConfigurationVersionDataType, variableIndexs,
                                                            out NewConfigurationVersion );

            if ( !string.IsNullOrWhiteSpace( errmsg ) )
            {
                MessageBox.Show( errmsg, "Remove Variables" );
                return;
            }
            foreach ( var index in variableIndexs ) _PublishedDataSetBase.Children.RemoveAt( ( int ) index );
            _PublishedDataSetDefinition.ConfigurationVersionDataType = NewConfigurationVersion;
        }

        #endregion
    }
}