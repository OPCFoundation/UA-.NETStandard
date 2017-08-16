using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
   public class BaseViewModel: INotifyPropertyChanged
    {
        #region INotifyPropertyChanged Members

        private PropertyChangedEventHandler _handler;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add { _handler += value; }
            remove
            {
                // ReSharper disable once DelegateSubtraction
                if (_handler != null) _handler -= value;
            }
        }

        protected void OnPropertyChanged(string info)
        {
            _handler?.Invoke(this, new PropertyChangedEventArgs(info));
        }

        #endregion
    }
}
