using System.Collections.ObjectModel;
using System.Windows;
using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class AddPublishedDataSetViewModel : BaseViewModel
    {
        #region Private Member 

        private readonly IOPCUAClientAdaptor _OPCUAClientAdaptor;
        private Visibility _PublisherNameVisibility = Visibility.Visible;
        private readonly TreeViewNode _RootNode;
        private ObservableCollection< TreeViewNode > _serverItems = new ObservableCollection< TreeViewNode >( );

        private ObservableCollection< PublishedDataSetItemDefinition > _VariableListDefinitionCollection =
        new ObservableCollection< PublishedDataSetItemDefinition >( );

        #endregion

        #region Constructors

        public AddPublishedDataSetViewModel( IOPCUAClientAdaptor opcUAClientAdaptor, TreeViewNode RootNode )
        {
            _RootNode = RootNode;
            _OPCUAClientAdaptor = opcUAClientAdaptor;
        }

        #endregion

        #region Public Property

        public Visibility PublisherNameVisibility
        {
            get { return _PublisherNameVisibility; }
            set
            {
                _PublisherNameVisibility = value;
                OnPropertyChanged( "PublisherNameVisibility" );
            }
        }

        public ObservableCollection< TreeViewNode > ServerItems
        {
            get { return _serverItems; }
            set
            {
                _serverItems = value;
                OnPropertyChanged( "ServerItems" );
            }
        }

        public ObservableCollection< PublishedDataSetItemDefinition > VariableListDefinitionCollection
        {
            get { return _VariableListDefinitionCollection; }
            set
            {
                _VariableListDefinitionCollection = value;
                OnPropertyChanged( "VariableListDefinitionCollection" );
            }
        }

        #endregion

        #region Public Methods

        public void AddVariable( PublishedDataSetItemDefinition _PublishedDataSetItemDefinition )
        {
            VariableListDefinitionCollection.Add( _PublishedDataSetItemDefinition );
        }

        public void Initialize( )
        {
            if ( _RootNode != null ) _RootNode.Header = "Root";
            ServerItems.Clear( );
            ServerItems.Add( _RootNode );
        }

        internal void Rebrowse( ref TreeViewNode node )
        {
            _OPCUAClientAdaptor.Rebrowse( ref node );
        }

        public void RemoveVariable( PublishedDataSetItemDefinition _PublishedDataSetItemDefinition )
        {
            VariableListDefinitionCollection.Remove( _PublishedDataSetItemDefinition );
        }

        #endregion
    }
}