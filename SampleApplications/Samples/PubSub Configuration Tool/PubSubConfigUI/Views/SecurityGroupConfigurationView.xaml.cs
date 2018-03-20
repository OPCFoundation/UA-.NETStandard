using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAdaptor;
using PubSubBase.Definitions;
using PubSubConfigurationUI.UserControls;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for SecurityGroupConfigurationView.xaml
    /// </summary>
    public partial class SecurityGroupConfigurationView : UserControl
    {
        #region Private Member 

        private readonly SecurityGroupUserControl _SecurityGroupUserControl;
        private readonly SecurityKeysUserControl _SecurityKeysUserControl;
        private SecurityGroupViewModel _viewModel;

        #endregion

        #region Private Methods

        private void OnAddGroupClick( object sender, RoutedEventArgs e )
        {
            var addSecurityGroupView = new AddSecurityGroupView( );
            addSecurityGroupView.Closing += AddSecurityGroupView_Closing;
            addSecurityGroupView.ShowInTaskbar = false;
            addSecurityGroupView.ShowDialog( );
        }

        private void AddSecurityGroupView_Closing( object sender, CancelEventArgs e )
        {
            var addSecurityGroupView = sender as AddSecurityGroupView;
            if ( addSecurityGroupView._isApplied )
            {
                var IsSuccess = ViewModel.AddSecurityGroup( addSecurityGroupView._name );
                if ( !IsSuccess )
                {
                    addSecurityGroupView._isApplied = false;
                    e.Cancel = true;
                }
            }
        }

        private void UIElement_OnMouseRightButtonDown( object sender, MouseButtonEventArgs e )
        {
            if ( e.ChangedButton == MouseButton.Right && !(e.OriginalSource is Image) &&
                 !(e.OriginalSource is TextBlock) && !(e.OriginalSource is Border) )
            {
                ViewModel.IsSecurityAddGroupVisible = Visibility.Visible;
                ViewModel.IsSecurityAddKeysVisible = Visibility.Collapsed;
                ViewModel.IsSecurityGroupRemoveVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
                return;
            }
            UpdateMenuandButtonVisibility( );
        }

        private void UpdateMenuandButtonVisibility( )
        {
            SecurityTreeView.ContextMenu.Visibility = Visibility.Visible;
            if ( SecurityTreeView.SelectedItem is SecurityGroup )
            {
                ViewModel.IsSecurityAddGroupVisible = Visibility.Collapsed;
                ViewModel.IsSecurityAddKeysVisible = Visibility.Visible;

                ViewModel.IsSecurityGroupRemoveVisible = Visibility.Visible;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else if ( SecurityTreeView.SelectedItem is SecurityKeys )
            {
                ViewModel.IsSecurityAddGroupVisible = Visibility.Collapsed;
                ViewModel.IsSecurityAddKeysVisible = Visibility.Collapsed;
                ViewModel.IsSecurityGroupRemoveVisible = Visibility.Collapsed;
                SecurityTreeView.ContextMenu.Visibility = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
            else
            {
                ViewModel.IsSecurityAddGroupVisible = Visibility.Visible;
                ViewModel.IsSecurityAddKeysVisible = Visibility.Collapsed;
                ViewModel.IsSecurityGroupRemoveVisible = Visibility.Collapsed;
                ViewModel.IsUpdateVisible = Visibility.Collapsed;
                ViewModel.IsCancelVisible = Visibility.Collapsed;
            }
        }

        private void OnRemoveSecurityGroupClick( object sender, RoutedEventArgs e )
        {
            if ( SecurityTreeView.SelectedItem is SecurityGroup )
                ViewModel.RemoveSecurityGroup( SecurityTreeView.SelectedItem as SecurityGroup );
        }

        private void OnSecurityKeysClick( object sender, RoutedEventArgs e )
        {
            var addSecurityKeysView = new AddSecurityKeys( );
            addSecurityKeysView._securityKeyViewModel.SecurityGroupId =
            (SecurityTreeView.SelectedItem as SecurityBase).SecurityGroupId;
            addSecurityKeysView.Closing += AddSecurityKeysView_Closing;
            addSecurityKeysView.ShowInTaskbar = false;
            addSecurityKeysView.ShowDialog( );
        }

        private void AddSecurityKeysView_Closing( object sender, CancelEventArgs e )
        {
            var addSecurityKeysView = sender as AddSecurityKeys;
            if ( addSecurityKeysView._isApplied )
            {
                var _securitykeys = new SecurityKeys( );
                _securitykeys.ParentNode = SecurityTreeView.SelectedItem as SecurityGroup;
                _securitykeys.SecurityGroupId = addSecurityKeysView._securityKeyViewModel.SecurityGroupId;
                _securitykeys.SecurityPolicyUri = addSecurityKeysView._securityKeyViewModel.SecurityPolicyUri;
                _securitykeys.CurrentTokenId = addSecurityKeysView._securityKeyViewModel.CurrentTokenId;
                _securitykeys.CurrentKey = addSecurityKeysView._securityKeyViewModel.CurrentKey;
                foreach ( var key in addSecurityKeysView._securityKeyViewModel.FeatureKeys )
                    _securitykeys.FeatureKeys.Add( key );

                _securitykeys.TimeToNextKey = addSecurityKeysView._securityKeyViewModel.TimetoNextKey;
                _securitykeys.KeyLifetime = addSecurityKeysView._securityKeyViewModel.KeyLifeTime;

                ViewModel.SetSecurityKeys( SecurityTreeView.SelectedItem as SecurityGroup, _securitykeys );
            }
        }

        private void SecurityTreeView_OnSelectedItemChanged( object sender, RoutedPropertyChangedEventArgs< object > e )
        {
            UpdateMenuandButtonVisibility( );
            //SecurityContentControl
            if ( SecurityTreeView.SelectedItem is SecurityGroup )
            {
                _SecurityGroupUserControl.SecurityGroupEditViewModel.SecurityGroup =
                SecurityTreeView.SelectedItem as SecurityGroup;
                _SecurityGroupUserControl.SecurityGroupEditViewModel.Initialize( );
                SecurityContentControl.Content = _SecurityGroupUserControl;
            }
            else if ( SecurityTreeView.SelectedItem is SecurityKeys )
            {
                _SecurityKeysUserControl.SecurityKeyEditViewModel.SecurityKeys =
                SecurityTreeView.SelectedItem as SecurityKeys;
                _SecurityKeysUserControl.SecurityKeyEditViewModel.Initialize( );
                SecurityContentControl.Content = _SecurityKeysUserControl;
            }
            else
            {
                SecurityContentControl.Content = null;
            }
        }

        private void Update_Click( object sender, RoutedEventArgs e )
        {
            var _securitykeys = new SecurityKeys( );
            _securitykeys.ParentNode = (SecurityTreeView.SelectedItem as SecurityKeys).ParentNode;
            _securitykeys.SecurityGroupId = _SecurityKeysUserControl.SecurityKeyEditViewModel.SecurityGroupId;
            _securitykeys.SecurityPolicyUri = _SecurityKeysUserControl.SecurityKeyEditViewModel.SecurityPolicyUri;
            _securitykeys.CurrentTokenId = _SecurityKeysUserControl.SecurityKeyEditViewModel.CurrentTokenId;
            _securitykeys.CurrentKey = _SecurityKeysUserControl.SecurityKeyEditViewModel.CurrentKey;
            foreach ( var key in _SecurityKeysUserControl.SecurityKeyEditViewModel.FeatureKeys )
                _securitykeys.FeatureKeys.Add( key );
            _securitykeys.TimeToNextKey = _SecurityKeysUserControl.SecurityKeyEditViewModel.TimeToNextKey;
            _securitykeys.KeyLifetime = _SecurityKeysUserControl.SecurityKeyEditViewModel.KeyLifetime;
            ViewModel.SetSecurityKeys( (SecurityTreeView.SelectedItem as SecurityKeys).ParentNode, _securitykeys );
        }

        private void Cancel_Click( object sender, RoutedEventArgs e )
        {
            _SecurityKeysUserControl.SecurityKeyEditViewModel.Initialize( );
        }

        #endregion

        #region Constructors

        public SecurityGroupConfigurationView( OPCUAClientAdaptor _OPCUAClientAdaptor, Window owner)
        {
            InitializeComponent( );
            ViewModel = new SecurityGroupViewModel( _OPCUAClientAdaptor );
            ViewModel.OwnerWindow = owner;
            DataContext = ViewModel;
            _SecurityGroupUserControl = new SecurityGroupUserControl( );
            _SecurityKeysUserControl = new SecurityKeysUserControl( );
            UpdateMenuandButtonVisibility( );
        }

        #endregion

        #region Public Property

        public SecurityGroupViewModel ViewModel
        {
            get { return _viewModel; }
            set { _viewModel = value; }
        }

        #endregion
    }
}