using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Client;
using Opc.Ua.Gds;

namespace Opc.Ua.Gds
{
    public partial class ServerStatusControl : UserControl
    {
        public ServerStatusControl()
        {
            InitializeComponent();
        }

        public void SetServerStatus(ServerStatusDataType status)
        {
            ProductNameTextBox.Text = "---";
            ProductUriTextBox.Text = "---";
            ManufacturerNameTextBox.Text = "---";
            SoftwareVersionTextBox.Text = "---";
            BuildNumberTextBox.Text = "---";
            BuildDateTextBox.Text = "---";
            StartTimeTextBox.Text = "---";
            CurrentTimeTextBox.Text = "---";
            StateTextBox.Text = "---";
            SecondsUntilShutdownTextBox.Text = "---";
            ShutdownReasonTextBox.Text = "---";

            if (status != null)
            {
                if (status.BuildInfo != null)
                {
                    ProductNameTextBox.Text = status.BuildInfo.ProductName;
                    ProductUriTextBox.Text = status.BuildInfo.ProductUri;
                    ManufacturerNameTextBox.Text = status.BuildInfo.ManufacturerName;
                    SoftwareVersionTextBox.Text = status.BuildInfo.SoftwareVersion;
                    BuildNumberTextBox.Text = status.BuildInfo.BuildNumber;
                    BuildDateTextBox.Text = status.BuildInfo.BuildDate.ToLocalTime().ToString("yyyy-MM-dd");
                }

                StartTimeTextBox.Text = status.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                CurrentTimeTextBox.Text = status.CurrentTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                SecondsUntilShutdownTextBox.Text = (status.SecondsTillShutdown > 0) ? status.SecondsTillShutdown.ToString() : "";
                ShutdownReasonTextBox.Text = (status.SecondsTillShutdown > 0) ? String.Format("{0}", status.ShutdownReason) : "";
                StateTextBox.Text = status.State.ToString();
            }
        }
        
        private void Button_MouseEnter(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.CornflowerBlue;
        }

        private void Button_MouseLeave(object sender, EventArgs e)
        {
            ((Control)sender).BackColor = Color.MidnightBlue;
        }
    }
}
