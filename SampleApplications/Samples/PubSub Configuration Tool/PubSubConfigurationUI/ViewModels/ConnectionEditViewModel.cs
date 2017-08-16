using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class ConnectionEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _address = string.Empty;
        private string _ConnectionName = string.Empty;
        private int _ConnectionType;
        private object _publisherId;

        #endregion

        #region Public Property

        public string ConnectionName
        {
            get { return _ConnectionName; }
            set
            {
                _ConnectionName = value;
                OnPropertyChanged( "ConnectionName" );
            }
        }

        public string Address
        {
            get { return _address; }
            set
            {
                _address = value;
                OnPropertyChanged( "Address" );
            }
        }

        public object PublisherId
        {
            get { return _publisherId; }
            set
            {
                _publisherId = value;
                OnPropertyChanged( "PublisherId" );
            }
        }

        public int ConnectionType
        {
            get { return _ConnectionType; }
            set
            {
                _ConnectionType = value;
                OnPropertyChanged( "ConnectionType" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            ConnectionName = Connection.Name;
            Address = Connection.Address;
            PublisherId = Connection.PublisherId;
            ConnectionType = Connection.ConnectionType;
        }

        #endregion

        public Connection Connection;
    }
}