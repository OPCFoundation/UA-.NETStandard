using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class ReaderGroupEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _GroupName;
        private int _MaxNetworkMessageSize = 1500;
        private string _QueueName;
        private string _SecurityGroupId;
        private int _securityMode = 1;

        #endregion

        #region Public Property

        public string GroupName
        {
            get { return _GroupName; }
            set
            {
                _GroupName = value;
                OnPropertyChanged( "GroupName" );
            }
        }

        public string SecurityGroupId
        {
            get { return _SecurityGroupId; }
            set
            {
                _SecurityGroupId = value;
                OnPropertyChanged( "SecurityGroupId" );
            }
        }

        public int MaxNetworkMessageSize
        {
            get { return _MaxNetworkMessageSize; }
            set
            {
                _MaxNetworkMessageSize = value;
                OnPropertyChanged( "MaxNetworkMessageSize" );
            }
        }

        public int SecurityMode
        {
            get { return _securityMode; }
            set
            {
                _securityMode = value;
                OnPropertyChanged( "SecurityMode" );
            }
        }

        public string QueueName
        {
            get { return _QueueName; }
            set
            {
                _QueueName = value;
                OnPropertyChanged( "QueueName" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            GroupName = ReaderGroupDefinition.GroupName;
            SecurityGroupId = ReaderGroupDefinition.SecurityGroupId;
            MaxNetworkMessageSize = ReaderGroupDefinition.MaxNetworkMessageSize;
            SecurityMode = ReaderGroupDefinition.SecurityMode;
            QueueName = ReaderGroupDefinition.QueueName;
        }

        #endregion

        public ReaderGroupDefinition ReaderGroupDefinition;
    }
}