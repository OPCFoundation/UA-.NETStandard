using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class SecurityGroupEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _groupNodeId;
        private string _name = string.Empty;
        private string _securityGroupId;

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

        public string GroupNodeId
        {
            get { return _groupNodeId; }
            set
            {
                _groupNodeId = value;
                OnPropertyChanged( "GroupNodeId" );
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

        public void Initialize( )
        {
            Name = SecurityGroup.Name;
            SecurityGroupId = SecurityGroup.SecurityGroupId;
            GroupNodeId = SecurityGroup.GroupNodeId.ToString( );
        }

        #endregion

        public SecurityGroup SecurityGroup;
    }
}