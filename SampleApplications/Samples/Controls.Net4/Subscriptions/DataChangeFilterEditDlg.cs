/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    public partial class DataChangeFilterEditDlg : Form
    {
        public DataChangeFilterEditDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
            
            Array values = Enum.GetValues(typeof(DataChangeTrigger));

            foreach (object value in values)
            {
                TriggerCB.Items.Add(value);
            }
                        
            values = Enum.GetValues(typeof(DeadbandType));

            foreach (object value in values)
            {
                DeadbandTypeCB.Items.Add(value);
            }
        }

        /// <summary>
        /// Prompts the user to specify the browse options.
        /// </summary>
        public bool ShowDialog(Session session, MonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException("monitoredItem");

            DataChangeFilter filter = monitoredItem.Filter as DataChangeFilter;

            if (filter == null)
            {
                filter = new DataChangeFilter();

                filter.Trigger       = DataChangeTrigger.StatusValue;
                filter.DeadbandValue = 0;
                filter.DeadbandType  = (uint)(int)DeadbandType.None;
            }

            TriggerCB.SelectedItem      = filter.Trigger;
            DeadbandTypeCB.SelectedItem = (DeadbandType)(int)filter.DeadbandType;
            DeadbandNC.Value            = (decimal)filter.DeadbandValue;
            
            if (ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            filter.Trigger       = (DataChangeTrigger)TriggerCB.SelectedItem;
            filter.DeadbandType  = Convert.ToUInt32(DeadbandTypeCB.SelectedItem);
            filter.DeadbandValue = (double)DeadbandNC.Value;

            monitoredItem.Filter = filter;

            return true;
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {              
            DialogResult = DialogResult.OK;
        }

        private void DeadbandTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            DeadbandType deadbandType = (DeadbandType)DeadbandTypeCB.SelectedItem;

            DeadbandNC.Enabled = deadbandType != DeadbandType.None;

            if (deadbandType == DeadbandType.Percent)
            {
                DeadbandNC.Minimum = 0;
                DeadbandNC.Maximum = 100;
            }
            else
            {
                DeadbandNC.Minimum = Decimal.MinValue;
                DeadbandNC.Maximum = Decimal.MaxValue;
            }
        }
    }
}
