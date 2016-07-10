using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace Opc.Ua.Server.Controls
{
    public partial class ClientHeaderBranding : UserControl
    {
        public ClientHeaderBranding()
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

        private void pictureBox1_Click(object sender, EventArgs e)
        {

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

        private void ClientHeaderBranding_Load(object sender, EventArgs e)
        {
            appName.Text = this.Parent.Text;
            labelBuild.Text = string.Format("UA .NET API Build: {0}", Assembly.GetExecutingAssembly().GetName().Version.ToString());
        }
    }
}
