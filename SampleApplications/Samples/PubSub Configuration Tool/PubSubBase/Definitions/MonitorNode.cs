using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PubSubBase.Definitions
{
    public class MonitorNode : BaseViewModel
    {
        public string ParentId { get; set; }
        public string Id { get; set; }
        private string _Value = string.Empty;
        public string Value
        {
            get
            {
                return _Value;
            }
            set
            {
                _Value = value;
                OnPropertyChanged("Value");
            }
        }
        public NodeId MonitorNodeId { get; set; }
        public NodeId ParantNodeId { get; set; }
        public NodeId EnableNodeId { get; set; }
        public NodeId DisableNodeId { get; set; }

        public string DisplayName { get; set; }

        private Visibility _IsMethodEnable = Visibility.Collapsed;
        public Visibility IsMethodEnable
        {
            get { return _IsMethodEnable; }
            set { _IsMethodEnable = value; OnPropertyChanged("IsMethodEnable"); }
        }

        private Visibility _IsMethodDisable = Visibility.Collapsed;
        public Visibility IsMethodDisable
        {
            get { return _IsMethodDisable; }
            set { _IsMethodDisable = value; OnPropertyChanged("IsMethodDisable"); }
        }
    }
}
