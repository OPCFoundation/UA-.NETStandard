using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Opc.Ua.Gds;

namespace Opc.Ua.Gds
{
    public partial class DiscoveryUrlsDialog : Form
    {
        public DiscoveryUrlsDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        private List<string> m_discoveryUrls;

        public List<string> ShowDialog(IWin32Window owner, IList<string> discoveryUrls)
        {
            StringBuilder builder = new StringBuilder();

            if (discoveryUrls != null)
            {
                foreach (var discoveryUrl in discoveryUrls)
                {
                    if (discoveryUrl != null && !String.IsNullOrEmpty(discoveryUrl.Trim()))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append("\r\n");
                        }

                        builder.Append(discoveryUrl.Trim());
                    }
                }
            }

            DiscoveryUrlsTextBox.Text = builder.ToString();

            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            return m_discoveryUrls;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            try
            {
                List<string> validatedUrls = new List<string>();

                string[] discoveryUrls = DiscoveryUrlsTextBox.Text.Split('\n');

                foreach (var discoveryUrl in discoveryUrls)
                {
                    if (discoveryUrl != null && !String.IsNullOrEmpty(discoveryUrl.Trim()))
                    {
                        string url = discoveryUrl.Trim();

                        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                        {
                            throw new ArgumentException("'" + discoveryUrl + "' is not a valid URL.", "discoveryUrls");
                        }

                        validatedUrls.Add(url);
                    }
                }

                m_discoveryUrls = validatedUrls;
                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                MessageBox.Show(Text + ": " + exception.Message);
            }
        }

        private void DiscoveryUrlsDialog_VisibleChanged(object sender, EventArgs e)
        {
            DiscoveryUrlsTextBox.SelectedText = "";
        }
    }
}
