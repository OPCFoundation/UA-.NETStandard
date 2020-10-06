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

namespace Quickstarts.ReferenceClient
{
    public partial class WriteOutput : Form
    {
        public WriteOutput(): this(null)
        {

        }

        public WriteOutput(Opc.Ua.Client.Session session): base()
        {
            InitializeComponent();
            writeRequestListViewCtrl1.ChangeSession(session);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.writeRequestListViewCtrl1.Write();
        }
    }
}
