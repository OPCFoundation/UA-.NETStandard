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
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer.Forms
{

    public partial class ViewForm : Form
    {
        List<List<BaseDataVariableState>> nodesList;
        IServerInternal IRefServer;
        Quickstarts.ReferenceServer.ReferenceNodeManager nodeManager;
        Timer MyTimer = new Timer();
        List<string> oldLines = new List<string>();

        public ViewForm()
        {
            InitializeComponent();
        }

        public ViewForm(IServerInternal IRefServer)
        {
            InitializeComponent();

            this.IRefServer = IRefServer;
            nodeManager = (ReferenceNodeManager)IRefServer.NodeManager.NodeManagers[2];
            nodesList = nodeManager.m_prod_nodes_list;
            makeDataGrid();
            foreach (var nodes in nodesList)
            {
                listBox1.Items.Add(nodes[0].Parent.DisplayName.ToString());
            }


            MyTimer.Interval = 1000; 
            MyTimer.Tick += new EventHandler(listBox1_SelectedIndexChanged);
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            //MyTimer.Start();
            //foreach (var nodes in nodesList)
            //{
            //    if (nodes[0].Parent.DisplayName.ToString() == comboBox1.SelectedItem.ToString())
            //    {
            //        comboBox1.Items.Add(nodes[0].Parent.DisplayName.ToString());
            //        lblCurrentSpeed.Text = nodes[0].Value.ToString();
            //        lblGoodPieces.Text = nodes[2].Value.ToString();
            //        lblBadPieces.Text = nodes[1].Value.ToString();
            //        lblDownTimes.Text = nodes[3].Value.ToString();
            //        lblLowerSpeedLimit.Text = nodes[5].Value.ToString();
            //        lblUpperSpeedLimit.Text = nodes[6].Value.ToString();
            //    }
            //}
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedLines = listBox1.SelectedItems;
            foreach (string line in selectedLines)
            {
                fillDataGrid(line);
                oldLines.Add(line);                
            }

            try
            {
                foreach (string line in oldLines)
                {
                    if (!selectedLines.Contains(line))
                    {
                        deleteRow(line); oldLines.Remove(line);
                    }
                }
            } catch (Exception) { };
        }

        private void fillDataGrid(string lijn)
        {
            MyTimer.Start();

            deleteRow(lijn);

            string[] row = new string[] { };
            foreach (var nodes in nodesList)
            {
                if (nodes[0].Parent.DisplayName.ToString() == lijn)
                {
                    row = new string[] { nodes[0].Parent.DisplayName.ToString(), nodes[0].Value.ToString(), nodes[2].Value.ToString(), nodes[1].Value.ToString(), nodes[3].Value.ToString(), nodes[5].Value.ToString(), nodes[6].Value.ToString()};
                }
            }
            
            dataGridView1.Rows.Add(row);

        }

        private void makeDataGrid()
        {
            //dataGridView1.RowCount = nodesList.Count;
            dataGridView1.ColumnCount = 7;

            dataGridView1.Columns[0].Name = "DataLijn";
            dataGridView1.Columns[1].Name = "MachineSpeed";
            dataGridView1.Columns[2].Name = "GoodPieces";
            dataGridView1.Columns[3].Name = "BadPieces";
            dataGridView1.Columns[4].Name = "DownTimes";
            dataGridView1.Columns[5].Name = "LowerSpeedLimit";
            dataGridView1.Columns[6].Name = "UpperSpeedLimit";
        }

        private void deleteRow(string lijn)
        {
            for (int v = 0; v < dataGridView1.Rows.Count; v++)
            {
                if (string.Equals(dataGridView1[0, v].Value as string, lijn))
                {
                    dataGridView1.Rows.RemoveAt(v);
                    v--;
                }
            }
        }
    }
}
