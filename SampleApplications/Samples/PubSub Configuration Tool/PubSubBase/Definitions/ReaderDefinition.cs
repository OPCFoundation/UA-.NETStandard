using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;

namespace PubSubBase.Definitions
{
    public class DataSetReaderDefinition : PubSubConfiguationBase
    {
        private string _DataSetReaderName;

        public string DataSetReaderName
        {
            get
            {
                return _DataSetReaderName;
            }
            set
            {
               Name= _DataSetReaderName = value;
                OnPropertyChanged("DataSetReaderName");
            }
        }

        private object _PublisherId;

        public object PublisherId
        {
            get
            {
                return _PublisherId;
            }
            set
            {
                _PublisherId = value;
                OnPropertyChanged("PublisherId");
            }
        }

        private int _DataSetWriterId;

        public int DataSetWriterId
        {
            get
            {
                return _DataSetWriterId;
            }
            set
            {
                _DataSetWriterId = value;
                OnPropertyChanged("DataSetWriterId");
            }
        }

        private DataSetMetaDataType _DataSetMetaDataType = new DataSetMetaDataType();
        public DataSetMetaDataType DataSetMetaDataType
        {
            get
            {
                return _DataSetMetaDataType;
            }
            set
            {
                _DataSetMetaDataType = value;
                OnPropertyChanged("DataSetMetaDataType");
            }
        }

        private double _MessageReceiveTimeOut;
        public double MessageReceiveTimeOut
        {
            get
            {
                return _MessageReceiveTimeOut;
            }
            set
            {
                _MessageReceiveTimeOut = value;
                OnPropertyChanged("MessageReceiveTimeOut");
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
        private int _NetworkMessageContentMask;

        public int NetworkMessageContentMask
        {
            get { return _NetworkMessageContentMask; }
            set
            {
                _NetworkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

        private double _publishingInterval;

        public double PublishingInterval
        {
            get
            {
                return _publishingInterval;
            }
            set
            {
                _publishingInterval = value;
                OnPropertyChanged("PublishingInterval");
            }
        }

        private NodeId _DataSetReaderNodeId;

        public NodeId DataSetReaderNodeId
        {
            get
            {
                return _DataSetReaderNodeId;
            }
            set
            {
                _DataSetReaderNodeId = value;
                OnPropertyChanged("DataSetReaderNodeId");
            }
        }

    }
}
