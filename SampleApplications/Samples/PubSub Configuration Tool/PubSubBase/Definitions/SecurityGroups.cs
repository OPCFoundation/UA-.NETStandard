
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class SecurityBase : BaseViewModel
    {
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

        private ObservableCollection<SecurityBase> _Children = new ObservableCollection<SecurityBase>();

        public ObservableCollection<SecurityBase> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged("Children");
            }
        }
        public SecurityBase ParentNode { get; set; }
    }

    public class SecurityGroup : SecurityBase
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
        

        private NodeId _groupNodeId;
        public NodeId GroupNodeId
        {
            get
            {
                return _groupNodeId;
            }
            set
            {
                _groupNodeId = value;
                OnPropertyChanged("GroupNodeId");
            }
        }
         
    }

  
}
