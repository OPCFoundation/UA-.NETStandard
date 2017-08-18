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
    public partial class CertificatesStoreDialog : Form
    {
        public CertificatesStoreDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        public void ShowDialog(ApplicationConfiguration configuration)
        {
            CertificatesControl.Initialize(
                configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath,
                configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath,
                configuration.SecurityConfiguration.RejectedCertificateStore.StorePath);

            ApplicationNameLabel.Text = configuration.ApplicationName;
            ApplicationUriLabel.Text = configuration.ApplicationUri;

            base.ShowDialog();
        }
    }
}
