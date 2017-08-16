using System.Collections.ObjectModel;
using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class TargetVariablesViewModel : BaseViewModel
    {
        #region Private Member 

        private int _MajorVersion;
        private int _MinorVersion;
        private readonly IOPCUAClientAdaptor _OPCUAClientAdaptor;
        private readonly TreeViewNode _RootNode;
        private ObservableCollection< TreeViewNode > _serverItems = new ObservableCollection< TreeViewNode >( );

        private ObservableCollection< FieldTargetVariableDefinition > _VariableListDefinitionCollection =
        new ObservableCollection< FieldTargetVariableDefinition >( );

        #endregion

        #region Constructors

        public TargetVariablesViewModel( IOPCUAClientAdaptor opcUAClientAdaptor, TreeViewNode RootNode )
        {
            _RootNode = RootNode;
            _OPCUAClientAdaptor = opcUAClientAdaptor;
        }

        #endregion

        #region Public Property

        public int MinorVersion
        {
            get { return _MinorVersion; }
            set
            {
                _MinorVersion = value;
                OnPropertyChanged( "MinorVersion" );
            }
        }

        public int MajorVersion
        {
            get { return _MajorVersion; }
            set
            {
                _MajorVersion = value;
                OnPropertyChanged( "MajorVersion" );
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

        public ObservableCollection< FieldTargetVariableDefinition > VariableListDefinitionCollection
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

        public void AddVariable( FieldTargetVariableDefinition _FieldTargetVariableDefinition )
        {
            VariableListDefinitionCollection.Add( _FieldTargetVariableDefinition );
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

        public void RemoveVariable( FieldTargetVariableDefinition _FieldTargetVariableDefinition )
        {
            VariableListDefinitionCollection.Remove( _FieldTargetVariableDefinition );
        }

        #endregion
    }
}