using ClientAdaptor;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class ConnectionViewModel : BaseViewModel
    {
        #region Private Member 

        private IOPCUAClientAdaptor _ClientAdaptor;

        #endregion

        #region Constructors

        public ConnectionViewModel( IOPCUAClientAdaptor OPCUAClientAdaptor )
        {
            _ClientAdaptor = OPCUAClientAdaptor;
        }

        #endregion
    }
}