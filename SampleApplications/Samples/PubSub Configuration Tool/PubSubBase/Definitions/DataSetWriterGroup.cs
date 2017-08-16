using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class DataSetWriterGroup : PubSubConfiguationBase
    {

        private string _groupName;

        public string GroupName
        {
            get
            {
                return _groupName;
            }
            set
            {
               Name = _groupName = value;
                OnPropertyChanged("GroupName");
            }
        }

        private string _QueueName =string.Empty;
        public string QueueName
        {
            get
            {
                return _QueueName;
            }
            set
            {
                _QueueName = value;
                OnPropertyChanged("QueueName");
            }
        }

        private string _EncodingMimeType = string.Empty;
        public string EncodingMimeType
        {
            get
            {
                return _EncodingMimeType;
            }
            set
            {
                _EncodingMimeType = value;
                OnPropertyChanged("EncodingMimeType");
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

        private double _publishingOffset;

        public double PublishingOffset
        {
            get
            {
                return _publishingOffset;
            }
            set
            {
                _publishingOffset = value;
                OnPropertyChanged("PublishingOffset");
            }
        }

        private double _keepAliveTime;

        public double KeepAliveTime
        {
            get
            {
                return _keepAliveTime;
            }
            set
            {
                _keepAliveTime = value;
                OnPropertyChanged("KeepAliveTime");
            }
        }

        private int _priority;

        public int Priority
        {
            get
            {
                return _priority;
            }
            set
            {
                _priority = value;
                OnPropertyChanged("Priority");
            }
        }

        private string _securityGroupId;

        public string SecurityGroupId
        {
            get
            {
                return _securityGroupId;
            }
            set
            {
                _securityGroupId = value;
                OnPropertyChanged("SecurityGroupId");
            }
        }


        private int _maxNetworkMessageSize=1500;

        public int MaxNetworkMessageSize
        {
            get
            {
                return _maxNetworkMessageSize;
            }
            set
            {
                _maxNetworkMessageSize = value;
                OnPropertyChanged("MaxNetworkMessageSize");
            }
        }


        private int _writerGroupId;

        public int WriterGroupId
        {
            get
            {
                return _writerGroupId;
            }
            set
            {
                _writerGroupId = value;
                OnPropertyChanged("WriterGroupId");
            }
        }

        private NodeId _groupId;
        public NodeId GroupId
        {
            get
            {
                return _groupId;
               
            }
            set
            {
                _groupId = value;
                OnPropertyChanged("GroupId");
            }
        }

        private int _messageSecurityMode;

        public int MessageSecurityMode
        {
            get { return _messageSecurityMode; }
            set
            {
                _messageSecurityMode = value;
                OnPropertyChanged("MessageSecurityMode");
            }
        }

        private int _netWorkMessageContentMask;

        public int NetworkMessageContentMask
        {
            get { return _netWorkMessageContentMask; }
            set
            {
                _netWorkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

       
    }
}
