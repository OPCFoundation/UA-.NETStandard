using System.Windows;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class DataSetWriterGroupEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _EncodingMimeType = string.Empty;
        private NodeId _groupId;
        private string _groupName;
        private Visibility _IsAMQP = Visibility.Collapsed;
        private Visibility _IsUADP = Visibility.Visible;
        private double _keepAliveTime;
        private int _maxNetworkMessageSize = 1500;
        private int _messageSecurityMode = 1;
        private int _netWorkMessageContentMask;
        private int _priority;
        private double _publishingInterval;
        private double _publishingOffset;
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

        public double PublishingInterval
        {
            get { return _publishingInterval; }
            set
            {
                _publishingInterval = value;
                OnPropertyChanged( "PublishingInterval" );
            }
        }

        public double PublishingOffset
        {
            get { return _publishingOffset; }
            set
            {
                _publishingOffset = value;
                OnPropertyChanged( "PublishingOffset" );
            }
        }

        public double KeepAliveTime
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

        public int MaxNetworkMessageSize
        {
            get { return _maxNetworkMessageSize; }
            set
            {
                _maxNetworkMessageSize = value;
                OnPropertyChanged( "MaxNetworkMessageSize" );
            }
        }

        public NodeId GroupId
        {
            get { return _groupId; }
            set
            {
                _groupId = value;
                OnPropertyChanged( "GroupId" );
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

        public int NetworkMessageContentMask
        {
            get { return _netWorkMessageContentMask; }
            set
            {
                _netWorkMessageContentMask = value;
                OnPropertyChanged( "NetworkMessageContentMask" );
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

        #region Public Methods

        //CheckBox

        public void Initialize( )
        {
            GroupName = DataSetWriterGroup.GroupName;
            PublishingInterval = DataSetWriterGroup.PublishingInterval;
            PublishingOffset = DataSetWriterGroup.PublishingOffset;
            KeepAliveTime = DataSetWriterGroup.KeepAliveTime;
            Priority = DataSetWriterGroup.Priority;
            SecurityGroupId = DataSetWriterGroup.SecurityGroupId;
            MaxNetworkMessageSize = DataSetWriterGroup.MaxNetworkMessageSize;
            WriterGroupId = DataSetWriterGroup.WriterGroupId;
            QueueName = DataSetWriterGroup.QueueName;
            EncodingMimeType = DataSetWriterGroup.EncodingMimeType;
            NetworkMessageContentMask = DataSetWriterGroup.NetworkMessageContentMask;
            MessageSecurityMode = DataSetWriterGroup.MessageSecurityMode;
        }

        #endregion

        public DataSetWriterGroup DataSetWriterGroup;
    }
}