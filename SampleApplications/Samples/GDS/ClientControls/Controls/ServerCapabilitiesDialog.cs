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
    public partial class ServerCapabilitiesDialog : Form
    {
        public ServerCapabilitiesDialog()
        {
            InitializeComponent();
            Icon = ImageListControl.AppIcon;
            m_capabilities = new ServerCapabilities();
        }

        private ServerCapabilities m_capabilities;

        public List<string> ShowDialog(IWin32Window owner, IList<string> serverCapabilities)
        {
            CapabilitiesListBox.Items.Clear();

            foreach (var capability in m_capabilities)
            {
                CapabilitiesListBox.Items.Add(capability);
            }

            if (serverCapabilities != null)
            {
                foreach (var capability in serverCapabilities)
                {
                    bool found = false;

                    for (int ii = 0; ii < CapabilitiesListBox.Items.Count; ii++)
                    {
                        var item = (ServerCapability)CapabilitiesListBox.Items[ii];

                        if (item.Id == capability)
                        {
                            found = true;
                            CapabilitiesListBox.SetItemChecked(ii, true);
                            break;
                        }
                    }

                    if (!found)
                    {
                        CapabilitiesListBox.Items.Add(new ServerCapability() { Id = capability, Description = capability }, true);
                    }
                }
            }

            if (base.ShowDialog(owner) != DialogResult.OK)
            {
                return null;
            }

            List<string> result = new List<string>();

            foreach (ServerCapability item in CapabilitiesListBox.CheckedItems)
            {
                result.Add(item.Id);
            }

            return result;
        }
    }
}
