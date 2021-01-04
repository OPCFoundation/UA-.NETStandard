/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Windows.Forms;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Controls
{
    /// <summary>
    /// Prompts the user to edit a value.
    /// </summary>
    public partial class EditMonitoredItemDlg : Form
    {
        #region Constructors
        /// <summary>
        /// Creates an empty form.
        /// </summary>
        public EditMonitoredItemDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            // add the attributes in numerical order.
            foreach (uint attributeId in Attributes.GetIdentifiers())
            {
                AttributeCB.Items.Add(Attributes.GetBrowseName(attributeId));
            }

            AttributeCB.SelectedIndex = 0;

            MonitoringModeCB.Items.Add(MonitoringMode.Reporting);
            MonitoringModeCB.Items.Add(MonitoringMode.Sampling);
            MonitoringModeCB.Items.Add(MonitoringMode.Disabled);
            MonitoringModeCB.SelectedIndex = 0;
            
            DeadbandTypeCB.Items.Add(DeadbandType.None);
            DeadbandTypeCB.Items.Add(DeadbandType.Absolute);
            DeadbandTypeCB.Items.Add(DeadbandType.Percent);
            DeadbandTypeCB.SelectedIndex = 0;

            TriggerTypeCB.Items.Add(DataChangeTrigger.StatusValue);
            TriggerTypeCB.Items.Add(DataChangeTrigger.Status);
            TriggerTypeCB.Items.Add(DataChangeTrigger.StatusValueTimestamp);
            TriggerTypeCB.SelectedIndex = 0;
        }
        #endregion

        #region EncodingInfo Class
        /// <summary>
        /// Stores information about a data encoding.
        /// </summary>
        private class EncodingInfo
        {
            public QualifiedName EncodingName;

            public override string ToString()
            {
                if (EncodingName != null)
                {
                    return EncodingName.ToString();
                }

                return "Not Set";
            }
        }
        #endregion
      
        #region Private Fields
        #endregion

        #region Public Interface
        /// <summary>
        /// Prompts the user to edit the monitored item.
        /// </summary>
        public bool ShowDialog(Session session, MonitoredItem monitoredItem, bool isEvent)
        {
            if (!monitoredItem.Created)
            {
                NodeBTN.Session = session;
                NodeBTN.SelectedNode = monitoredItem.StartNodeId;
            }

            // hide fields not used for events.
            NodeLB.Visible = !monitoredItem.Created;
            NodeTB.Visible = !monitoredItem.Created;
            NodeBTN.Visible = !monitoredItem.Created;
            AttributeLB.Visible = !isEvent && !monitoredItem.Created;
            AttributeCB.Visible = !isEvent && !monitoredItem.Created;
            IndexRangeLB.Visible = !isEvent && !monitoredItem.Created;
            IndexRangeTB.Visible = !isEvent && !monitoredItem.Created;
            DataEncodingLB.Visible = !isEvent && !monitoredItem.Created;
            DataEncodingCB.Visible = !isEvent && !monitoredItem.Created;
            MonitoringModeLB.Visible = !monitoredItem.Created;
            MonitoringModeCB.Visible = !monitoredItem.Created;
            SamplingIntervalLB.Visible = true;
            SamplingIntervalUP.Visible = true;
            QueueSizeLB.Visible = !isEvent;
            QueueSizeUP.Visible = !isEvent;
            DiscardOldestLB.Visible = true;
            DiscardOldestCK.Visible = true;
            DeadbandTypeLB.Visible = !isEvent;
            DeadbandTypeCB.Visible = !isEvent;
            DeadbandValueLB.Visible = !isEvent;
            DeadbandValueUP.Visible = !isEvent;
            TriggerTypeLB.Visible = !isEvent;
            TriggerTypeCB.Visible = !isEvent;
            
            // fill in values.
            SamplingIntervalUP.Value = monitoredItem.SamplingInterval;
            DiscardOldestCK.Checked = monitoredItem.DiscardOldest;

            if (!isEvent)
            {
                AttributeCB.SelectedIndex = (int)(monitoredItem.AttributeId - 1);
                IndexRangeTB.Text = monitoredItem.IndexRange;
                MonitoringModeCB.SelectedItem = monitoredItem.MonitoringMode;
                QueueSizeUP.Value = monitoredItem.QueueSize;

                DataChangeFilter filter = monitoredItem.Filter as DataChangeFilter;

                if (filter != null)
                {
                    DeadbandTypeCB.SelectedItem = (DeadbandType)filter.DeadbandType;
                    DeadbandValueUP.Value = (decimal)filter.DeadbandValue;
                    TriggerTypeCB.SelectedItem = filter.Trigger;
                }

                if (!monitoredItem.Created)
                {
                    // fetch the available encodings for the first node in the list from the server.
                    IVariableBase variable = session.NodeCache.Find(monitoredItem.StartNodeId) as IVariableBase;

                    DataEncodingCB.Items.Add(new EncodingInfo());
                    DataEncodingCB.SelectedIndex = 0;

                    if (variable != null)
                    {
                        if (session.NodeCache.IsTypeOf(variable.DataType, Opc.Ua.DataTypeIds.Structure))
                        {
                            foreach (INode encoding in session.NodeCache.Find(variable.DataType, Opc.Ua.ReferenceTypeIds.HasEncoding, false, true))
                            {
                                DataEncodingCB.Items.Add(new EncodingInfo() { EncodingName = encoding.BrowseName });

                                if (monitoredItem.Encoding == encoding.BrowseName)
                                {
                                    DataEncodingCB.SelectedIndex = DataEncodingCB.Items.Count - 1;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                AttributeCB.SelectedIndex = ((int)Attributes.EventNotifier - 1);
            }

            if (base.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            // update monitored item.
            if (!monitoredItem.Created)
            {
                monitoredItem.StartNodeId = NodeBTN.SelectedNode;
                monitoredItem.DisplayName = session.NodeCache.GetDisplayText(monitoredItem.StartNodeId);
                monitoredItem.RelativePath = null;
                monitoredItem.AttributeId = (uint)(AttributeCB.SelectedIndex + 1);
                monitoredItem.MonitoringMode = (MonitoringMode)MonitoringModeCB.SelectedItem;
            }

            monitoredItem.SamplingInterval = (int)SamplingIntervalUP.Value;
            monitoredItem.DiscardOldest = DiscardOldestCK.Checked;

            if (!isEvent)
            {
                if (!monitoredItem.Created)
                {
                    monitoredItem.IndexRange = IndexRangeTB.Text.Trim();
                    monitoredItem.Encoding = ((EncodingInfo)DataEncodingCB.SelectedItem).EncodingName;
                }

                monitoredItem.QueueSize = (uint)QueueSizeUP.Value;

                DataChangeTrigger trigger = (DataChangeTrigger)TriggerTypeCB.SelectedItem;
                DeadbandType deadbandType = (DeadbandType)DeadbandTypeCB.SelectedItem;

                if (monitoredItem.Filter != null || deadbandType != DeadbandType.None || trigger != DataChangeTrigger.StatusValue)
                {
                    DataChangeFilter filter = new DataChangeFilter();
                    filter.DeadbandType = (uint)deadbandType;
                    filter.DeadbandValue = (double)DeadbandValueUP.Value;
                    filter.Trigger = trigger;
                    monitoredItem.Filter = filter;
                }
            }
            else
            {
                if (!monitoredItem.Created)
                {
                    monitoredItem.IndexRange = null;
                    monitoredItem.Encoding = null;
                }

                monitoredItem.QueueSize = 0;
                monitoredItem.Filter = new EventFilter();
            }
            
            return true;
        }

        /// <summary>
        /// Prompts the user to specify a monitoring mode.
        /// </summary>
        public MonitoringMode ShowDialog(MonitoringMode monitoringMode)
        {
            NodeLB.Visible = false;
            NodeTB.Visible = false;
            NodeBTN.Visible = false;
            AttributeLB.Visible = false;
            AttributeCB.Visible = false;
            IndexRangeLB.Visible = false;
            IndexRangeTB.Visible = false;
            DataEncodingLB.Visible = false;
            DataEncodingCB.Visible = false;
            MonitoringModeLB.Visible = true;
            MonitoringModeCB.Visible = true;
            SamplingIntervalLB.Visible = false;
            SamplingIntervalUP.Visible = false;
            QueueSizeLB.Visible = false;
            QueueSizeUP.Visible = false;
            DiscardOldestLB.Visible = false;
            DiscardOldestCK.Visible = false;
            DeadbandTypeLB.Visible = false;
            DeadbandTypeCB.Visible = false;
            DeadbandValueLB.Visible = false;
            DeadbandValueUP.Visible = false;
            TriggerTypeLB.Visible = false;
            TriggerTypeCB.Visible = false;

            MonitoringModeCB.SelectedItem = monitoringMode;

            if (base.ShowDialog() != DialogResult.OK)
            {
                return monitoringMode;
            }

            return (MonitoringMode)MonitoringModeCB.SelectedItem;
        }
        #endregion
        
        #region Event Handlers
        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (IndexRangeTB.Visible)
                {
                    NumericRange.Parse(IndexRangeTB.Text);
                }

                DialogResult = DialogResult.OK;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }
        #endregion
    }
}
