using System.Windows.Forms;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Gds
{
    public partial class SelectServerDialog : Form
    {
        public SelectServerDialog()
        {
            InitializeComponent();
            Icon = ClientUtils.GetAppIcon();
        }

        public EndpointDescription ShowDialog(
            IWin32Window owner,
            ConfiguredEndpointCollection endpoints,
            LocalDiscoveryServer lds,
            GlobalDiscoveryServer gds,
            QueryServersFilter filters)
        {
            DiscoveryControl.Initialize(endpoints, lds, gds, filters);

            if (base.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
            {
                return null;
            }

            return DiscoveryControl.SelectedEndpoint;
        }
    }
}
