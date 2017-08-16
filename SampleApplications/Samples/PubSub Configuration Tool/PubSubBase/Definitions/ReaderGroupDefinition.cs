using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class ReaderGroupDefinition : PubSubConfiguationBase
    {
        private string _GroupName;

        public string GroupName
        {
            get
            {
                return _GroupName;
            }
            set
            {
               Name= _GroupName = value;
                OnPropertyChanged("GroupName");
            }
        }

        private string _SecurityGroupId;

        public string SecurityGroupId
        {
            get
            {
                return _SecurityGroupId;
            }
            set
            {
                _SecurityGroupId = value;
                OnPropertyChanged("SecurityGroupId");
            }
        }

        private int _MaxNetworkMessageSize=1500;

        public int MaxNetworkMessageSize
        {
            get
            {
                return _MaxNetworkMessageSize;
            }
            set
            {
                _MaxNetworkMessageSize = value;
                OnPropertyChanged("MaxNetworkMessageSize");
            }
        }

        private int _securityMode;

        public int SecurityMode
        {
            get { return _securityMode; }
            set
            {
                _securityMode = value;
                OnPropertyChanged("SecurityMode");
            }
        }

        private string _QueueName;
        public string QueueName
        {
            get { return _QueueName; }
            set
            {
                _QueueName = value;
                OnPropertyChanged("QueueName");
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


    }
}
