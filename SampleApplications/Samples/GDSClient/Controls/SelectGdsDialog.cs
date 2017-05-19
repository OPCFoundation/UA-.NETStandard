using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Opc.Ua.Gds
{
    public partial class SelectGdsDialog : Form
    {
        public SelectGdsDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        private GlobalDiscoveryServer m_gds;

        public string ShowDialog(IWin32Window owner, GlobalDiscoveryServer gds, IList<string> serverUrls)
        {
            m_gds = gds;

            ServersListBox.Items.Clear();

            foreach (var serverUrl in serverUrls)
            {
                ServersListBox.Items.Add(serverUrl);
            }

            ServerUrlTextBox.Text = gds.EndpointUrl;
            OkButton.Enabled = Uri.IsWellFormedUriString(ServerUrlTextBox.Text.Trim(), UriKind.Absolute);

            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            return ServerUrlTextBox.Text.Trim();
        }

        private void ServersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ServerUrlTextBox.Text = ServersListBox.SelectedItem as string;
        }

        private void ServerUrlTextBox_TextChanged(object sender, EventArgs e)
        {
            OkButton.Enabled = Uri.IsWellFormedUriString(ServerUrlTextBox.Text.Trim(), UriKind.Absolute);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            try
            {
                string url = ServerUrlTextBox.Text.Trim();

                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    throw new ArgumentException("The URL is not valid: " + url, "ServerUrl");
                }

                try
                {
                    Cursor = Cursors.WaitCursor;
                    m_gds.Connect(url);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                DialogResult = System.Windows.Forms.DialogResult.OK;
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }
    }
}
