using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClientAdaptor;
using PubSubBase.Definitions;
using PubSubConfigurationUI.ViewModels;

namespace PubSubConfigurationUI.Views
{
    /// <summary>
    ///   Interaction logic for PubSubView.xaml
    /// </summary>
    public partial class PubSubStatusView : UserControl
    {
        #region Private Member 

        private bool IsinAction;

        #endregion

        #region Private Methods

        private void ViewModel_PropertyChanged( object sender, PropertyChangedEventArgs e )
        {
            if ( e.PropertyName == "StatusMonitoredItems" )
                if ( SubscriptionGrid.SelectedItem == null ) SubscriptionGrid.SelectedIndex = 0;
        }

        private void OnEnableMethodClick( object sender, MouseButtonEventArgs e )
        {
            if ( IsinAction ) return;
            IsinAction = true;
            var _MonitorNode = (sender as Button).DataContext as MonitorNode;
            ViewModel.EnablePubSubState( _MonitorNode );
            IsinAction = false;
        }

        private void OnDisableMethodClick( object sender, MouseButtonEventArgs e )
        {
            if ( IsinAction ) return;
            IsinAction = true;
            var _MonitorNode = (sender as Button).DataContext as MonitorNode;
            ViewModel.DisablePubSubState( _MonitorNode );
            IsinAction = false;
        }

        private void Refresh_Click( object sender, RoutedEventArgs e )
        {
            ViewModel.Initialize( );
        }

        #endregion

        #region Constructors

        public PubSubStatusView( IOPCUAClientAdaptor _OPCUAClientAdaptor )
        {
            InitializeComponent( );
            ViewModel = new PubSubStatusViewModel( _OPCUAClientAdaptor );
            DataContext = ViewModel;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        #endregion

        public PubSubStatusViewModel ViewModel;
    }
}