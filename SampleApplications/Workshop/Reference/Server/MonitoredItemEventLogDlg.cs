/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    public partial class MonitoredItemEventLogDlg : Form
    {
        public MonitoredItemEventLogDlg()
        {
            InitializeComponent();
            DataGridCTRL.AutoGenerateColumns = false;
        }
        
        public void Display()
        {
            ServerUtils.EventsEnabled = true;
            RefreshTimer.Enabled = true;
            Show();
        }

        DataSet m_dataset;

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            int count = 0;

            if (m_dataset != null)
            {
                count = m_dataset.Tables[0].Rows.Count;
            }

            m_dataset = ServerUtils.EmptyQueue(m_dataset);
            
            if (count != m_dataset.Tables[0].Rows.Count)
            {
                DataGridCTRL.DataSource = m_dataset.Tables[0];
            }
        }

        private void Events_ClearMI_Click(object sender, EventArgs e)
        {
            if (m_dataset != null)
            {
                m_dataset.Tables[0].Rows.Clear();
                m_dataset.AcceptChanges();
                DataGridCTRL.DataSource = m_dataset.Tables[0];
            }
        }
    }
}
