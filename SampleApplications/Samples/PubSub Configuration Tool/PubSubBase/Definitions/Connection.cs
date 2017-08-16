using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class Connection : PubSubConfiguationBase
    { 
        private string _address = String.Empty;

        public string Address
        {
            get
            {
                return _address;
            }
            set
            {
                _address = value;
                OnPropertyChanged("Address");
            }
        }

        private object _publisherId;

        public object PublisherId
        {
            get
            {
                return _publisherId;
            }
            set
            {
                _publisherId = value;
                OnPropertyChanged("PublisherId");
            }
        }
        int _PublisherDataType = 0;
        public int PublisherDataType
        {
            get
            {
                return _PublisherDataType;
            }
            set
            {
                _PublisherDataType = value;
                OnPropertyChanged("PublisherDataType");
            }
        }
        private int _ConnectionType; 
        public int ConnectionType
        {
            get
            {
                return _ConnectionType;
            }
            set
            {
                _ConnectionType = value;
                OnPropertyChanged("ConnectionType");
            }
        }
        private NodeId _ConnectionNodeId;
        public NodeId ConnectionNodeId
        {
            get
            {
                return _ConnectionNodeId;
            }
            set
            {
                _ConnectionNodeId = value;
                OnPropertyChanged("ConnectionNodeId");
            }
        }
    }
     


    public class PubSubConfiguationBase : BaseViewModel
    {
        private string _Name;
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value;
                OnPropertyChanged("Name");
            }
        }

        private ObservableCollection<PubSubConfiguationBase> _Children = new ObservableCollection<PubSubConfiguationBase>();

        public ObservableCollection<PubSubConfiguationBase> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged("Children");
            }
        }
        public PubSubConfiguationBase ParentNode { get; set; }
    }

}
