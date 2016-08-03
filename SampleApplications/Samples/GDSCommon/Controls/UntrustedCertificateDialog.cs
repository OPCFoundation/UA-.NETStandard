using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Gds
{
    public partial class UntrustedCertificateDialog : Form
    {
        public UntrustedCertificateDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
        }

        public DialogResult ShowDialog(IWin32Window owner, X509Certificate2 certificate)
        {
            CertificateValueControl.ShowValue(null, null, new CertificateWrapper() { Certificate = certificate }, true);
 
            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return DialogResult.Cancel;
            }

            return DialogResult.OK;
        }
    }
}
