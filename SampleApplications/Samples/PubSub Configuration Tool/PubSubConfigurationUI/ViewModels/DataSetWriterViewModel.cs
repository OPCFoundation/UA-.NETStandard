using System.Windows;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class DataSetWriterViewModel : BaseViewModel
    {
        #region Private Member 

        private int _dataSetContentMask;
        private int _dataSetWriterId;
        private string _dataSetWriterName;
        private Visibility _IsAMQP = Visibility.Collapsed;
        private Visibility _IsUADP = Visibility.Visible;
        private int _keyFrameCount;
        private int _maxMessageSize;
        private string _metadataQueueName;
        private int _metadataUpdateTime;
        private string _publisherDataSetId;
        private NodeId _publisherDataSetNodeId;
        private string _queueName;

        #endregion

        #region Public Property

        public string DataSetWriterName
        {
            get { return _dataSetWriterName; }
            set
            {
                _dataSetWriterName = value;
                OnPropertyChanged( "DataSetWriterName" );
            }
        }

        public string PublisherDataSetId
        {
            get { return _publisherDataSetId; }
            set
            {
                _publisherDataSetId = value;
                OnPropertyChanged( "PublisherDataSetId" );
            }
        }

        public NodeId PublisherDataSetNodeId
        {
            get { return _publisherDataSetNodeId; }
            set
            {
                _publisherDataSetNodeId = value;
                OnPropertyChanged( "PublisherDataSetNodeId" );
            }
        }

        public int KeyFrameCount
        {
            get { return _keyFrameCount; }
            set
            {
                _keyFrameCount = value;
                OnPropertyChanged( "KeyFrameCount" );
            }
        }

        public int DataSetWriterId
        {
            get { return _dataSetWriterId; }
            set
            {
                _dataSetWriterId = value;
                OnPropertyChanged( "DataSetWriterId" );
            }
        }

        public string QueueName
        {
            get { return _queueName; }
            set
            {
                _queueName = value;
                OnPropertyChanged( "QueueName" );
            }
        }

        public string MetadataQueueName
        {
            get { return _metadataQueueName; }
            set
            {
                _metadataQueueName = value;
                OnPropertyChanged( "MetadataQueueName" );
            }
        }

        public int MetadataUpdataTime
        {
            get { return _metadataUpdateTime; }
            set
            {
                _metadataUpdateTime = value;
                OnPropertyChanged( "MetadataUpdataTime" );
            }
        }

        public int MaxMessageSize
        {
            get { return _maxMessageSize; }
            set
            {
                _maxMessageSize = value;
                OnPropertyChanged( "MaxMessageSize" );
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

        public Visibility IsUADP
        {
            get { return _IsUADP; }
            set
            {
                _IsUADP = value;
                OnPropertyChanged( "IsUADP" );
            }
        }

        public Visibility IsAMQP
        {
            get { return _IsAMQP; }
            set
            {
                _IsAMQP = value;
                OnPropertyChanged( "IsAMQP" );
            }
        }

        #endregion
    }
}