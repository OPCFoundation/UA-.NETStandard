using System.Collections.ObjectModel;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class SecurityKeyEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _CurrentKey;
        private uint _currentTokenId;
        private ObservableCollection< string > _FeatureKeys;
        private double _KeyLifetime;
        private string _name = string.Empty;
        private string _securityGroupId;
        private string _securityPolicyUri;
        private double _TimeToNextKey;

        #endregion

        #region Constructors

        public SecurityKeyEditViewModel( )
        {
            FeatureKeys = new ObservableCollection< string >( );
        }

        #endregion

        #region Public Property

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged( "Name" );
            }
        }

        public string SecurityPolicyUri
        {
            get { return _securityPolicyUri; }
            set
            {
                _securityPolicyUri = value;
                OnPropertyChanged( "SecurityPolicyUri" );
            }
        }

        public uint CurrentTokenId
        {
            get { return _currentTokenId; }
            set
            {
                _currentTokenId = value;
                OnPropertyChanged( "CurrentTokenId" );
            }
        }

        public double TimeToNextKey
        {
            get { return _TimeToNextKey; }
            set
            {
                _TimeToNextKey = value;
                OnPropertyChanged( "TimeToNextKey" );
            }
        }

        public double KeyLifetime
        {
            get { return _KeyLifetime; }
            set
            {
                _KeyLifetime = value;
                OnPropertyChanged( "KeyLifetime" );
            }
        }

        public string CurrentKey
        {
            get { return _CurrentKey; }
            set
            {
                Name = _CurrentKey = value;

                OnPropertyChanged( "CurrentKey" );
            }
        }

        public ObservableCollection< string > FeatureKeys
        {
            get { return _FeatureKeys; }
            set
            {
                _FeatureKeys = value;
                OnPropertyChanged( "FeatureKeys" );
            }
        }

        public string SecurityGroupId
        {
            get { return _securityGroupId; }
            set
            {
                _securityGroupId = value;
                OnPropertyChanged( "SecurityGroupId" );
            }
        }

        #endregion

        #region Public Methods

        public void AddFeatureKeysList( string featureKey )
        {
            FeatureKeys.Add( featureKey );
        }

        public void Initialize( )
        {
            Name = SecurityKeys.CurrentKey;
            SecurityGroupId = SecurityKeys.SecurityGroupId;
            FeatureKeys.Clear( );
            foreach ( var key in SecurityKeys.FeatureKeys ) FeatureKeys.Add( key );
            CurrentKey = SecurityKeys.CurrentKey;
            KeyLifetime = SecurityKeys.KeyLifetime;
            TimeToNextKey = SecurityKeys.TimeToNextKey;
            CurrentTokenId = SecurityKeys.CurrentTokenId;
            SecurityPolicyUri = SecurityKeys.SecurityPolicyUri;
        }

        public void RemoveFeatureKey( string featureKey )
        {
            FeatureKeys.Remove( featureKey );
        }

        #endregion

        public SecurityKeys SecurityKeys;
    }
}