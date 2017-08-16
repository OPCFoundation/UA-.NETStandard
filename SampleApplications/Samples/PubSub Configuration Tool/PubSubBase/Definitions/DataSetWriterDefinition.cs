using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class DataSetWriterDefinition : PubSubConfiguationBase
    {

        private string _dataSetWriterName;

        public string DataSetWriterName
        {
            get
            {
                return _dataSetWriterName;
            }
            set
            {
                Name = _dataSetWriterName = value;
                OnPropertyChanged("DataSetWriterName");
            }
        }

        private NodeId _publisherDataSetNodeId=new NodeId("0",1);

        public NodeId PublisherDataSetNodeId
        {
            get
            {
                return _publisherDataSetNodeId;
            }
            set
            {
                _publisherDataSetNodeId = value;
                OnPropertyChanged("PublisherDataSetNodeId");
            }
        }
        private string _PublisherDataSetId;

        public string PublisherDataSetId
        {
            get
            {
                return _PublisherDataSetId;
            }
            set
            {
                _PublisherDataSetId = value;
                OnPropertyChanged("PublisherDataSetId");
            }
        }

        private int _keyFrameCount;

        public int KeyFrameCount
        {
            get
            {
                return _keyFrameCount;
            }
            set
            {
                _keyFrameCount = value;
                OnPropertyChanged("KeyFrameCount");
            }
        }

        private int _dataSetWriterId;

        public int DataSetWriterId
        {
            get
            {
                return _dataSetWriterId;
            }
            set
            {
                _dataSetWriterId = value;
                OnPropertyChanged("DataSetWriterId");
            }
        }

        private string _queueName;

        public string QueueName
        {
            get
            {
                return _queueName;
            }
            set
            {
                _queueName = value;
                OnPropertyChanged("QueueName");
            }
        }

        private string _metadataQueueName;
        public string MetadataQueueName
        {
            get
            {
                return _metadataQueueName;
            }
            set
            {
                _metadataQueueName = value;
                OnPropertyChanged("MetadataQueueName");
            }
        }

        private int _metadataUpdateTime;

        public int MetadataUpdataTime
        {
            get
            {
                return _metadataUpdateTime;
            }
            set
            {
                _metadataUpdateTime = value;
                OnPropertyChanged("MetadataUpdataTime");
            }
        }

        private int _maxMessageSize;
        public int MaxMessageSize
        {
            get
            {
                return _maxMessageSize;
            }
            set
            {
                _maxMessageSize = value;
                OnPropertyChanged("MaxMessageSize");
            }
        }

        private NodeId _writerNodeId;
        public NodeId WriterNodeId
        {
            get
            {
                return _writerNodeId;

            }
            set
            {
                _writerNodeId = value;
                OnPropertyChanged("WriterNodeId");
            }
        }

        private int _revisedKeyFrameCount;
        public int RevisedKeyFrameCount
        {
            get
            {
                return _revisedKeyFrameCount;

            }
            set
            {
                _revisedKeyFrameCount = value;
                OnPropertyChanged("RevisedKeyFrameCount");
            }
        }

        private int _revisedMaxMessageSize;
        public int RevisedMaxMessageSize
        {
            get
            {
                return _revisedMaxMessageSize;

            }
            set
            {
                _revisedMaxMessageSize = value;
                OnPropertyChanged("RevisedMaxMessageSize");
            }
        }

        private int _dataSetContentMask;

        public int DataSetContentMask
        {
            get { return _dataSetContentMask; }
            set
            {
                _dataSetContentMask = value;
                OnPropertyChanged("DataSetContentMask");
            }
        }

    }
}
