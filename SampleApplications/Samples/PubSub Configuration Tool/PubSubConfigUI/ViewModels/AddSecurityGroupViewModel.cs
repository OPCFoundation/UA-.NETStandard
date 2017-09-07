using System.Collections.ObjectModel;
using System.Windows;
using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class SecurityGroupViewModel : BaseViewModel
    {
        #region Private Member 

        private readonly IOPCUAClientAdaptor _ClientAdaptor;
        private Visibility _IsCancelVisible = Visibility.Collapsed;
        private Visibility _IsSecurityAddGroupVisible = Visibility.Visible;
        private Visibility _IsSecurityAddKeysVisible = Visibility.Collapsed;
        private Visibility _IsSecurityGroupRemoveVisible = Visibility.Collapsed;
        private Visibility _IsUpdateVisible = Visibility.Collapsed;
        private ObservableCollection< SecurityBase > _SecurityCollection;

        #endregion

        #region Constructors

        public SecurityGroupViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            _ClientAdaptor = OPCUAClientAdaptor;
            SecurityCollection = new ObservableCollection< SecurityBase >( );
        }

        #endregion

        #region Public Property

        public Visibility IsSecurityAddGroupVisible
        {
            get { return _IsSecurityAddGroupVisible; }
            set
            {
                _IsSecurityAddGroupVisible = value;
                OnPropertyChanged( "IsSecurityAddGroupVisible" );
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

        public Visibility IsSecurityAddKeysVisible
        {
            get { return _IsSecurityAddKeysVisible; }
            set
            {
                _IsSecurityAddKeysVisible = value;
                OnPropertyChanged( "IsSecurityAddKeysVisible" );
            }
        }

        public Visibility IsSecurityGroupRemoveVisible
        {
            get { return _IsSecurityGroupRemoveVisible; }
            set
            {
                _IsSecurityGroupRemoveVisible = value;
                OnPropertyChanged( "IsSecurityGroupRemoveVisible" );
            }
        }

        public ObservableCollection< SecurityBase > SecurityCollection
        {
            get { return _SecurityCollection; }
            set
            {
                _SecurityCollection = value;
                OnPropertyChanged( "SecurityCollection" );
            }
        }

        #endregion

        #region Public Methods

        public bool AddSecurityGroup( string name )
        {
            SecurityGroup _SecurityGroup;
            var errorMessage = _ClientAdaptor.AddNewSecurityGroup( name, out _SecurityGroup );
            if ( _SecurityGroup != null )
            {
                SecurityCollection.Add( _SecurityGroup );
            }
            else
            {
                MessageBox.Show( errorMessage, "Add Security Group" );
                return false;
            }
            return true;
        }

        public ObservableCollection< SecurityGroup > InitailizeSecurityGroups( )
        {
            SecurityCollection.Clear( );
            var securityGroups = _ClientAdaptor.GetSecurityGroups( );
            foreach ( var securityGroup in securityGroups ) SecurityCollection.Add( securityGroup );
            return securityGroups;
        }

        public void Initialize( )
        {
            InitailizeSecurityGroups( );
            foreach ( SecurityGroup group in SecurityCollection )
            {
                SecurityKeys _SecurityKeys;
                _ClientAdaptor.GetSecurityKeys( group.SecurityGroupId, uint.MaxValue, out _SecurityKeys );
                if ( _SecurityKeys != null )
                {
                    _SecurityKeys.ParentNode = group;
                    group.Children.Add( _SecurityKeys );
                }
            }
        }

        public void RemoveSecurityGroup( SecurityGroup SecurityGroup )
        {
            var errorMessage = _ClientAdaptor.RemoveSecurityGroup( SecurityGroup.GroupNodeId );

            if ( string.IsNullOrWhiteSpace( errorMessage ) ) SecurityCollection.Remove( SecurityGroup );
            else MessageBox.Show( errorMessage );
        }

        public void SetSecurityKeys( SecurityBase _SecurityBase, SecurityKeys _Securitykeys )
        {
            var errmsg = _ClientAdaptor.SetSecurityKeys( _Securitykeys );
            if ( !string.IsNullOrWhiteSpace( errmsg ) )
            {
                MessageBox.Show( errmsg, "Set Security keys" );
                return;
            }
            _SecurityBase.Children.Clear( );
            _SecurityBase.Children.Add( _Securitykeys );
        }

        #endregion

        //}
        //        _ClientAdaptor.UpdateSecurityGroup(name, nodeId);
        //{ 

        //public void Update(string name, NodeId nodeId)
    }
}