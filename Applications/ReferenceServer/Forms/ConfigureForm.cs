using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer.Forms
{
    public partial class ConfigureForm : Form
    {
        List<List<BaseDataVariableState>> nodesList;
        IServerInternal IRefServer;
        Quickstarts.ReferenceServer.ReferenceNodeManager nodeManager;

        public ConfigureForm()
        {
            InitializeComponent();
        }

        public ConfigureForm(IServerInternal IRefServer, ApplicationInstance application)
        {
            InitializeComponent();

            this.IRefServer = IRefServer;
            nodeManager = (ReferenceNodeManager)IRefServer.NodeManager.NodeManagers[2];
            nodesList = nodeManager.m_prod_nodes_list;
            foreach (var nodes in nodesList)
            {
                comboBox1.Items.Add(nodes[0].Parent.DisplayName.ToString());
            }
            
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            foreach (var node in nodesList)
            {
                if (node[0].Parent.DisplayName.ToString() == comboBox1.SelectedItem.ToString())
                {
                    foreach (var item in node)
                    {
                        switch (item.DisplayName.Text)
                        {
                            case "LowerSpeedLimit":
                                item.Value = uint.Parse(txtLowerSpeed.Text);
                                break;
                            case "UpperSpeedLimit":
                                item.Value = uint.Parse(txtUpperSpeed.Text);
                                break;
                            case "DoesSpeedChange":
                                item.Value = checkBox1.Checked ? (object)(uint)1 : (object)(uint)0;
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            nodeManager.m_prod_nodes_list = nodesList;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach (var node in nodesList)
            {
                if (node[0].Parent.DisplayName.ToString() == comboBox1.SelectedItem.ToString())
                {
                    txtLowerSpeed.Text = node[5].Value.ToString();
                    txtUpperSpeed.Text = node[6].Value.ToString();
                    checkBox1.Checked = int.Parse(node[7].Value.ToString()) >= 1;
                }
            }
        }
    }
}
