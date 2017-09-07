using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class DataSetReaderEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _ConnectionPublisherId;
        private int _dataSetContentMask;
        private DataSetMetaDataType _DataSetMetaDataType = new DataSetMetaDataType( );
        private string _DataSetReaderName;
        private int _DataSetWriterId;
        private double _MessageReceiveTimeOut;
        private int _NetworkMessageContentMask;
        private object _PublisherId;
        private double _publishingInterval;

        #endregion

        #region Public Property

        public string DataSetReaderName
        {
            get { return _DataSetReaderName; }
            set
            {
                _DataSetReaderName = value;
                OnPropertyChanged( "DataSetReaderName" );
            }
        }

        public object PublisherId
        {
            get { return _PublisherId; }
            set
            {
                _PublisherId = value;
                OnPropertyChanged( "PublisherId" );
            }
        }

        public string ConnectionPublisherId
        {
            get
            {
                if ( PublisherId != null ) PublisherId.ToString( );
                return _ConnectionPublisherId;
            }
            set
            {
                _ConnectionPublisherId = value;
                OnPropertyChanged( "ConnectionPublisherId" );
            }
        }

        public int DataSetWriterId
        {
            get { return _DataSetWriterId; }
            set
            {
                _DataSetWriterId = value;
                OnPropertyChanged( "DataSetWriterId" );
            }
        }

        public DataSetMetaDataType DataSetMetaDataType
        {
            get { return _DataSetMetaDataType; }
            set
            {
                _DataSetMetaDataType = value;
                OnPropertyChanged( "DataSetMetaDataType" );
            }
        }

        public double MessageReceiveTimeOut
        {
            get { return _MessageReceiveTimeOut; }
            set
            {
                _MessageReceiveTimeOut = value;
                OnPropertyChanged( "MessageReceiveTimeOut" );
            }
        }

        public int DataSetContentMask
        {
            get { return _dataSetContentMask; }
            set
            {
                _dataSetContentMask = value;
                OnPropertyChanged( "DataSetContentMask" );
            }
        }

        public int NetworkMessageContentMask
        {
            get { return _NetworkMessageContentMask; }
            set
            {
                _NetworkMessageContentMask = value;
                OnPropertyChanged( "NetworkMessageContentMask" );
            }
        }

        public double PublishingInterval
        {
            get { return _publishingInterval; }
            set
            {
                _publishingInterval = value;
                OnPropertyChanged( "PublishingInterval" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            DataSetReaderName = ReaderDefinition.DataSetReaderName;
            PublisherId = ReaderDefinition.PublisherId;
            ConnectionPublisherId = PublisherId.ToString( );
            DataSetWriterId = ReaderDefinition.DataSetWriterId;
            DataSetMetaDataType = ReaderDefinition.DataSetMetaDataType;
            MessageReceiveTimeOut = ReaderDefinition.MessageReceiveTimeOut;
            DataSetContentMask = ReaderDefinition.DataSetContentMask;
            PublishingInterval = ReaderDefinition.PublishingInterval;
            NetworkMessageContentMask = ReaderDefinition.NetworkMessageContentMask;
        }

        #endregion

        public DataSetReaderDefinition ReaderDefinition;
    }
}