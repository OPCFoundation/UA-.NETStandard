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

namespace Opc.Ua.Client.Controls
{
    partial class SubscribeDataListViewCtrl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SubscribeDataListViewCtrl));
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.NewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetMonitoringModeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ResultsDV = new System.Windows.Forms.DataGridView();
            this.Icon = new System.Windows.Forms.DataGridViewImageColumn();
            this.NodeAttributeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IndexRangeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DataEncodingCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MonitoringModeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SamplingIntervalCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.QueueSizeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DiscardOldestCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.FilterCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.OperationStatusCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DataTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusCodeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SourceTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RightPN = new System.Windows.Forms.Panel();
            this.StatusCTRL = new System.Windows.Forms.StatusStrip();
            this.SubscriptionStateLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SubscriptionStateTB = new System.Windows.Forms.ToolStripDropDownButton();
            this.Subscription_EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SequenceNumberLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SequenceNumberTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastNotificationLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastNotificationTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).BeginInit();
            this.RightPN.SuspendLayout();
            this.StatusCTRL.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewMI,
            this.EditMI,
            this.DeleteMI,
            this.ViewValueMI,
            this.SetMonitoringModeMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(197, 114);
            this.PopupMenu.Opening += new System.ComponentModel.CancelEventHandler(this.PopupMenu_Opening);
            // 
            // NewMI
            // 
            this.NewMI.Name = "NewMI";
            this.NewMI.Size = new System.Drawing.Size(196, 22);
            this.NewMI.Text = "New...";
            this.NewMI.Click += new System.EventHandler(this.NewMI_Click);
            // 
            // EditMI
            // 
            this.EditMI.Name = "EditMI";
            this.EditMI.Size = new System.Drawing.Size(196, 22);
            this.EditMI.Text = "Edit...";
            this.EditMI.Click += new System.EventHandler(this.EditMI_Click);
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(196, 22);
            this.DeleteMI.Text = "Delete";
            this.DeleteMI.Click += new System.EventHandler(this.DeleteMI_Click);
            // 
            // ViewValueMI
            // 
            this.ViewValueMI.Name = "ViewValueMI";
            this.ViewValueMI.Size = new System.Drawing.Size(196, 22);
            this.ViewValueMI.Text = "View Value....";
            this.ViewValueMI.Click += new System.EventHandler(this.ViewValueMI_Click);
            // 
            // SetMonitoringModeMI
            // 
            this.SetMonitoringModeMI.Name = "SetMonitoringModeMI";
            this.SetMonitoringModeMI.Size = new System.Drawing.Size(196, 22);
            this.SetMonitoringModeMI.Text = "Set Monitoring Mode...";
            this.SetMonitoringModeMI.Click += new System.EventHandler(this.SetMonitoringModeMI_Click);
            // 
            // ResultsDV
            // 
            this.ResultsDV.AllowUserToAddRows = false;
            this.ResultsDV.AllowUserToDeleteRows = false;
            this.ResultsDV.AllowUserToResizeRows = false;
            this.ResultsDV.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ResultsDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ResultsDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Icon,
            this.NodeAttributeCH,
            this.IndexRangeCH,
            this.DataEncodingCH,
            this.MonitoringModeCH,
            this.SamplingIntervalCH,
            this.QueueSizeCH,
            this.DiscardOldestCH,
            this.FilterCH,
            this.OperationStatusCH,
            this.DataTypeCH,
            this.ValueCH,
            this.StatusCodeCH,
            this.SourceTimestampCH,
            this.ServerTimestampCH});
            this.ResultsDV.ContextMenuStrip = this.PopupMenu;
            this.ResultsDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsDV.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ResultsDV.Location = new System.Drawing.Point(0, 0);
            this.ResultsDV.Name = "ResultsDV";
            this.ResultsDV.RowHeadersVisible = false;
            this.ResultsDV.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ResultsDV.Size = new System.Drawing.Size(754, 346);
            this.ResultsDV.TabIndex = 0;
            this.ResultsDV.DoubleClick += new System.EventHandler(this.ResultsDV_DoubleClick);
            // 
            // Icon
            // 
            this.Icon.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.Icon.DataPropertyName = "Icon";
            this.Icon.HeaderText = "";
            this.Icon.Name = "Icon";
            this.Icon.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.Icon.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.Icon.Width = 19;
            // 
            // NodeAttributeCH
            // 
            this.NodeAttributeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.NodeAttributeCH.DataPropertyName = "NodeAttribute";
            this.NodeAttributeCH.HeaderText = "Item Name";
            this.NodeAttributeCH.Name = "NodeAttributeCH";
            this.NodeAttributeCH.ReadOnly = true;
            this.NodeAttributeCH.Width = 83;
            // 
            // IndexRangeCH
            // 
            this.IndexRangeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.IndexRangeCH.DataPropertyName = "IndexRange";
            this.IndexRangeCH.HeaderText = "Index Range";
            this.IndexRangeCH.Name = "IndexRangeCH";
            this.IndexRangeCH.ReadOnly = true;
            this.IndexRangeCH.Visible = false;
            // 
            // DataEncodingCH
            // 
            this.DataEncodingCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DataEncodingCH.DataPropertyName = "DataEncoding";
            this.DataEncodingCH.HeaderText = "Data Encoding";
            this.DataEncodingCH.Name = "DataEncodingCH";
            this.DataEncodingCH.ReadOnly = true;
            this.DataEncodingCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.DataEncodingCH.Visible = false;
            // 
            // MonitoringModeCH
            // 
            this.MonitoringModeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.MonitoringModeCH.DataPropertyName = "MonitoringMode";
            this.MonitoringModeCH.HeaderText = "Monitoring Mode";
            this.MonitoringModeCH.Name = "MonitoringModeCH";
            this.MonitoringModeCH.ReadOnly = true;
            this.MonitoringModeCH.Width = 111;
            // 
            // SamplingIntervalCH
            // 
            this.SamplingIntervalCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.SamplingIntervalCH.DataPropertyName = "SamplingInterval";
            this.SamplingIntervalCH.HeaderText = "Sampling Interval";
            this.SamplingIntervalCH.Name = "SamplingIntervalCH";
            this.SamplingIntervalCH.Visible = false;
            // 
            // QueueSizeCH
            // 
            this.QueueSizeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.QueueSizeCH.DataPropertyName = "QueueSize";
            this.QueueSizeCH.HeaderText = "Queue Size";
            this.QueueSizeCH.Name = "QueueSizeCH";
            this.QueueSizeCH.ReadOnly = true;
            this.QueueSizeCH.Visible = false;
            // 
            // DiscardOldestCH
            // 
            this.DiscardOldestCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DiscardOldestCH.DataPropertyName = "DiscardOldest";
            this.DiscardOldestCH.HeaderText = "Discard Oldest";
            this.DiscardOldestCH.Name = "DiscardOldestCH";
            this.DiscardOldestCH.ReadOnly = true;
            this.DiscardOldestCH.Visible = false;
            // 
            // FilterCH
            // 
            this.FilterCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.FilterCH.DataPropertyName = "Filter";
            this.FilterCH.HeaderText = "Filter";
            this.FilterCH.Name = "FilterCH";
            this.FilterCH.ReadOnly = true;
            this.FilterCH.Visible = false;
            // 
            // OperationStatusCH
            // 
            this.OperationStatusCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.OperationStatusCH.DataPropertyName = "OperationStatus";
            this.OperationStatusCH.HeaderText = "Operation Status";
            this.OperationStatusCH.Name = "OperationStatusCH";
            this.OperationStatusCH.ReadOnly = true;
            this.OperationStatusCH.Visible = false;
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DataTypeCH.DataPropertyName = "DataType";
            this.DataTypeCH.HeaderText = "Data Type";
            this.DataTypeCH.Name = "DataTypeCH";
            this.DataTypeCH.ReadOnly = true;
            this.DataTypeCH.Visible = false;
            // 
            // ValueCH
            // 
            this.ValueCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ValueCH.DataPropertyName = "Value";
            this.ValueCH.HeaderText = "Value";
            this.ValueCH.Name = "ValueCH";
            this.ValueCH.ReadOnly = true;
            this.ValueCH.Visible = false;
            // 
            // StatusCodeCH
            // 
            this.StatusCodeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.StatusCodeCH.DataPropertyName = "StatusCode";
            this.StatusCodeCH.HeaderText = "Status";
            this.StatusCodeCH.Name = "StatusCodeCH";
            this.StatusCodeCH.ReadOnly = true;
            this.StatusCodeCH.Visible = false;
            // 
            // SourceTimestampCH
            // 
            this.SourceTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.SourceTimestampCH.DataPropertyName = "SourceTimestamp";
            this.SourceTimestampCH.HeaderText = "Source Time";
            this.SourceTimestampCH.Name = "SourceTimestampCH";
            this.SourceTimestampCH.ReadOnly = true;
            this.SourceTimestampCH.Visible = false;
            // 
            // ServerTimestampCH
            // 
            this.ServerTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.ServerTimestampCH.DataPropertyName = "ServerTimestamp";
            this.ServerTimestampCH.HeaderText = "Server Time";
            this.ServerTimestampCH.Name = "ServerTimestampCH";
            this.ServerTimestampCH.ReadOnly = true;
            this.ServerTimestampCH.Visible = false;
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.StatusCTRL);
            this.RightPN.Controls.Add(this.ResultsDV);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(0, 0);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(754, 346);
            this.RightPN.TabIndex = 3;
            // 
            // StatusCTRL
            // 
            this.StatusCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SubscriptionStateLB,
            this.SubscriptionStateTB,
            this.SequenceNumberLB,
            this.SequenceNumberTB,
            this.LastNotificationLB,
            this.LastNotificationTB});
            this.StatusCTRL.Location = new System.Drawing.Point(0, 324);
            this.StatusCTRL.Name = "StatusCTRL";
            this.StatusCTRL.Size = new System.Drawing.Size(754, 22);
            this.StatusCTRL.SizingGrip = false;
            this.StatusCTRL.TabIndex = 1;
            // 
            // SubscriptionStateLB
            // 
            this.SubscriptionStateLB.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.SubscriptionStateLB.Name = "SubscriptionStateLB";
            this.SubscriptionStateLB.Size = new System.Drawing.Size(76, 17);
            this.SubscriptionStateLB.Text = "Subscription";
            // 
            // SubscriptionStateTB
            // 
            this.SubscriptionStateTB.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.SubscriptionStateTB.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Subscription_EditMI});
            this.SubscriptionStateTB.Image = ((System.Drawing.Image)(resources.GetObject("SubscriptionStateTB.Image")));
            this.SubscriptionStateTB.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.SubscriptionStateTB.Name = "SubscriptionStateTB";
            this.SubscriptionStateTB.Size = new System.Drawing.Size(67, 20);
            this.SubscriptionStateTB.Text = "<status>";
            // 
            // Subscription_EditMI
            // 
            this.Subscription_EditMI.Name = "Subscription_EditMI";
            this.Subscription_EditMI.Size = new System.Drawing.Size(152, 22);
            this.Subscription_EditMI.Text = "Edit...";
            this.Subscription_EditMI.Click += new System.EventHandler(this.Subscription_EditMI_Click);
            // 
            // SequenceNumberLB
            // 
            this.SequenceNumberLB.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.SequenceNumberLB.Name = "SequenceNumberLB";
            this.SequenceNumberLB.Size = new System.Drawing.Size(111, 17);
            this.SequenceNumberLB.Text = "Sequence Number";
            // 
            // SequenceNumberTB
            // 
            this.SequenceNumberTB.Name = "SequenceNumberTB";
            this.SequenceNumberTB.Size = new System.Drawing.Size(44, 17);
            this.SequenceNumberTB.Text = "<###>";
            // 
            // LastNotificationLB
            // 
            this.LastNotificationLB.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.LastNotificationLB.Name = "LastNotificationLB";
            this.LastNotificationLB.Size = new System.Drawing.Size(98, 17);
            this.LastNotificationLB.Text = "Last Notification";
            // 
            // LastNotificationTB
            // 
            this.LastNotificationTB.Name = "LastNotificationTB";
            this.LastNotificationTB.Size = new System.Drawing.Size(75, 17);
            this.LastNotificationTB.Text = "<hh:mm:ss>";
            // 
            // ImageList
            // 
            this.ImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
            this.ImageList.ImageSize = new System.Drawing.Size(16, 16);
            this.ImageList.TransparentColor = System.Drawing.Color.White;
            // 
            // SubscribeDataListViewCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Name = "SubscribeDataListViewCtrl";
            this.Size = new System.Drawing.Size(754, 346);
            this.PopupMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).EndInit();
            this.RightPN.ResumeLayout(false);
            this.RightPN.PerformLayout();
            this.StatusCTRL.ResumeLayout(false);
            this.StatusCTRL.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.DataGridView ResultsDV;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.ToolStripMenuItem EditMI;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.ToolStripMenuItem NewMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.ToolStripMenuItem ViewValueMI;
        private System.Windows.Forms.DataGridViewImageColumn Icon;
        private System.Windows.Forms.DataGridViewTextBoxColumn NodeAttributeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn IndexRangeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DataEncodingCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn MonitoringModeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn SamplingIntervalCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn QueueSizeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DiscardOldestCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn FilterCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn OperationStatusCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DataTypeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusCodeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn SourceTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerTimestampCH;
        private System.Windows.Forms.ToolStripMenuItem SetMonitoringModeMI;
        private System.Windows.Forms.StatusStrip StatusCTRL;
        private System.Windows.Forms.ToolStripStatusLabel SubscriptionStateLB;
        private System.Windows.Forms.ToolStripDropDownButton SubscriptionStateTB;
        private System.Windows.Forms.ToolStripMenuItem Subscription_EditMI;
        private System.Windows.Forms.ToolStripStatusLabel SequenceNumberLB;
        private System.Windows.Forms.ToolStripStatusLabel SequenceNumberTB;
        private System.Windows.Forms.ToolStripStatusLabel LastNotificationLB;
        private System.Windows.Forms.ToolStripStatusLabel LastNotificationTB;
    }
}
