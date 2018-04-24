using PubSubBase.Definitions;
using System.Windows;

namespace PubSubConfigurationUI.Views
{
    public class AddConnectionViewViewModel : BaseViewModel
    {
        Visibility m_transportDatagramVisibility = Visibility.Visible;
        public Visibility TransportDatagramVisibility
        {
            get
            {
                return m_transportDatagramVisibility;
            }
            set
            {
                m_transportDatagramVisibility = value;
                OnPropertyChanged("TransportDatagramVisibility");
            }
        }

        Visibility m_transportBrokerVisibility = Visibility.Collapsed;
        public Visibility TransportBrokerVisibility
        {
            get
            {
                return m_transportBrokerVisibility;
            }
            set
            {
                m_transportBrokerVisibility = value;
                OnPropertyChanged("TransportBrokerVisibility");
            }
        }

    }
}