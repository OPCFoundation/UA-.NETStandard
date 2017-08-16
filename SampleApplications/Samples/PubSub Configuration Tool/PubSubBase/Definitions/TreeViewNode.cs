using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PubSubBase.Definitions
{
    public class TreeViewNode : BaseViewModel
    {
        #region Constructors

        public TreeViewNode()
        {
            Children = new ObservableCollection<TreeViewNode>();
            Reference = new ClientReferenceDescription();
        }
        #endregion

        #region Properties

        private string _header;

        public string Header
        {
            get
            {

                return _header;
            }
            set
            {
                _header = value;
            }
        }

        public string ParentId { get; set; }

        public ClientReferenceDescription Reference { get; set; }

        public ObservableCollection<TreeViewNode> Children { get; set; }

        public string Id { get; set; }

        public bool IsRoot { get; set; }

        public bool IsExpanded { get; set; }

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

        private Visibility _IsMethodEnable=Visibility.Collapsed;
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

        #endregion
    }
}
