using System;
using System.Windows.Forms;

namespace Opc.Ua.Client.Controls
{
    public partial class HeaderBranding : UserControl
    {
        public HeaderBranding()
        {
            InitializeComponent();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(linkLabel1.Text);
            }
            catch
            {
            }
        }

        private void linkLabel1_Click(object sender, EventArgs e)
        {
            linkLabel1_LinkClicked(sender, null);
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("http://www.opcfoundation.org/certification");
            }
            catch
            {
            }
        }

        private void ServerHeaderBranding_Load(object sender, EventArgs e)
        {
            appName.Text = this.Parent.Text;
        }
    }
}
