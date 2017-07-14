using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Opc.Ua.Gds
{
    public partial class EndpointUrlDialog : Form
    {
        public EndpointUrlDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        public string ShowDialog(string url)
        {
            if (url != null)
            {
                EndpointUrlTextBox.Text = url;
            }

            OkButton.Enabled = Uri.IsWellFormedUriString(EndpointUrlTextBox.Text, UriKind.Absolute);

            if (base.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return null;
            }

            return EndpointUrlTextBox.Text;
        }

        private void EndpointUrlTextBox_TextChanged(object sender, EventArgs e)
        {
            OkButton.Enabled = Uri.IsWellFormedUriString(EndpointUrlTextBox.Text, UriKind.Absolute);
        }
    }
}
