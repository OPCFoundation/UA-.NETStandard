using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubConfigurationUI.Definitions
{
    [Serializable]
    public class SystemNode 
    {
        private ObservableCollection<ServerNode> _children;
        public string Name { get; set; }
        public string IpAddress { get; set; }

        public ObservableCollection<ServerNode> Children
        {
            get { return _children; }
            set { _children = value; }
        }
        public SystemNode()
        {
            Children = new ObservableCollection<ServerNode>();
        }
    }
}
