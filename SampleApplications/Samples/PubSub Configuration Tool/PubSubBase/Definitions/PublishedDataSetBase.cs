using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
   public class PublishedDataSetBase:BaseViewModel
    {
        string _name = string.Empty;
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                OnPropertyChanged("Name");
            }
        }
        private ObservableCollection<PublishedDataSetBase> _Children = new ObservableCollection<PublishedDataSetBase>();

        public ObservableCollection<PublishedDataSetBase> Children
        {
            get { return _Children; }
            set
            {
                _Children = value;
                OnPropertyChanged("Children");
            }
        }
        public PublishedDataSetBase ParentNode { get; set; }
    }
}
