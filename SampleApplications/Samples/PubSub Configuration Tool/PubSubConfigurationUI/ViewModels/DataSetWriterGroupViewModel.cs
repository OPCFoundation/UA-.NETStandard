using System.Windows;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class DataSetWriterGroupViewModel : BaseViewModel
    {
        #region Private Member 

        private string _EncodingMimeType = string.Empty;
        private string _groupName;
        private Visibility _IsAMQP = Visibility.Collapsed;
        private Visibility _IsUADP = Visibility.Visible;
        private int _keepAliveTime;
        private int _maxNetworkMessageSize = 1500;
        private int _messageSecurityMode = 1;
        private int _netWorkMessageContentMask;
        private int _priority;
        private int _publishingInterval;
        private int _publishingOffset;
        private string _QueueName = string.Empty;
        private string _securityGroupId;
        private int _writerGroupId;

        #endregion

        #region Public Property

        public string GroupName
        {
            get { return _groupName; }
            set
            {
                _groupName = value;
                OnPropertyChanged( "GroupName" );
            }
        }

        public int PublishingInterval
        {
            get { return _publishingInterval; }
            set
            {
                _publishingInterval = value;
                OnPropertyChanged( "PublishingInterval" );
            }
        }

        public int PublishingOffset
        {
            get { return _publishingOffset; }
            set
            {
                _publishingOffset = value;
                OnPropertyChanged( "PublishingOffset" );
            }
        }

        public int KeepAliveTime
        {
            get { return _keepAliveTime; }
            set
            {
                _keepAliveTime = value;
                OnPropertyChanged( "KeepAliveTime" );
            }
        }

        public int Priority
        {
            get { return _priority; }
            set
            {
                _priority = value;
                OnPropertyChanged( "Priority" );
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

        public string QueueName
        {
            get { return _QueueName; }
            set
            {
                _QueueName = value;
                OnPropertyChanged( "QueueName" );
            }
        }

        public string EncodingMimeType
        {
            get { return _EncodingMimeType; }
            set
            {
                _EncodingMimeType = value;
                OnPropertyChanged( "EncodingMimeType" );
            }
        }

        public int MaxNetworkMessageSize
        {
            get { return _maxNetworkMessageSize; }
            set
            {
                _maxNetworkMessageSize = value;
                OnPropertyChanged( "MaxNetworkMessageSize" );
            }
        }

        public int WriterGroupId
        {
            get { return _writerGroupId; }
            set
            {
                _writerGroupId = value;
                OnPropertyChanged( "WriterGroupId" );
            }
        }

        public int MessageSecurityMode
        {
            get { return _messageSecurityMode; }
            set
            {
                _messageSecurityMode = value;
                OnPropertyChanged( "MessageSecurityMode" );
            }
        }

        public int NetworkMessageContentMask
        {
            get { return _netWorkMessageContentMask; }
            set
            {
                _netWorkMessageContentMask = value;
                OnPropertyChanged( "NetworkMessageContentMask" );
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