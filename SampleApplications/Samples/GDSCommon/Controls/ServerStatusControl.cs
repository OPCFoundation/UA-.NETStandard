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

        private PushConfigurationServer m_server;

        public void Initialize(PushConfigurationServer server)
        {
            m_server = server;
            ServerBrowseControl.Initialize((server != null) ? server.Session : null, Opc.Ua.ObjectIds.ObjectsFolder, ReferenceTypeIds.HierarchicalReferences);
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

        private void ApplyChangesButton_Click(object sender, EventArgs e)
        {
            if (m_server == null)
            {
                return;
            }

            try
            {
                m_server.ApplyChanges();
            }
            catch (Exception exception)
            {
                var se = exception as ServiceResultException;

                if (se == null || se.StatusCode != StatusCodes.BadServerHalted)
                {
                    MessageBox.Show(Parent.Text + ": " + exception.Message);
                }
            }

            try
            {
                m_server.Disconnect();
            }
            catch (Exception)
            {
                // ignore.
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
