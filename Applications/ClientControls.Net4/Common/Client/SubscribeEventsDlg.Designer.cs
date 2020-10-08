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
    partial class SubscribeEventsDlg
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SubscribeEventsDlg));
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.ButtonsPN = new System.Windows.Forms.FlowLayoutPanel();
            this.NextBTN = new System.Windows.Forms.Button();
            this.BackBTN = new System.Windows.Forms.Button();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.StatusCTRL = new System.Windows.Forms.StatusStrip();
            this.SubscriptionStateLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SubscriptionStateTB = new System.Windows.Forms.ToolStripDropDownButton();
            this.Subscription_EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SequenceNumberLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SequenceNumberTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastNotificationLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastNotificationTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ItemsDV = new System.Windows.Forms.DataGridView();
            this.IconCH = new System.Windows.Forms.DataGridViewImageColumn();
            this.NodeAttributeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MonitoringModeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SamplingIntervalCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DiscardOldestCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.OperationStatusCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.NewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetMonitoringModeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EventTypePN = new System.Windows.Forms.SplitContainer();
            this.BrowseCTRL = new Opc.Ua.Client.Controls.BrowseTreeViewCtrl();
            this.EventTypeCTRL = new Opc.Ua.Client.Controls.TypeFieldsListViewCtrl();
            this.EventsCTRL = new Opc.Ua.Client.Controls.EventListViewCtrl();
            this.EventFilterCTRL = new Opc.Ua.Client.Controls.EventFilterListViewCtrl();
            this.ButtonsPN.SuspendLayout();
            this.StatusCTRL.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemsDV)).BeginInit();
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.EventTypePN)).BeginInit();
            this.EventTypePN.Panel1.SuspendLayout();
            this.EventTypePN.Panel2.SuspendLayout();
            this.EventTypePN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(706, 3);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 5;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            this.CancelBTN.Click += new System.EventHandler(this.CancelBTN_Click);
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(625, 3);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 4;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.NextBTN);
            this.ButtonsPN.Controls.Add(this.BackBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 333);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.ButtonsPN.Size = new System.Drawing.Size(784, 29);
            this.ButtonsPN.TabIndex = 12;
            // 
            // NextBTN
            // 
            this.NextBTN.Location = new System.Drawing.Point(544, 3);
            this.NextBTN.Name = "NextBTN";
            this.NextBTN.Size = new System.Drawing.Size(75, 23);
            this.NextBTN.TabIndex = 6;
            this.NextBTN.Text = "Next";
            this.NextBTN.UseVisualStyleBackColor = true;
            this.NextBTN.Click += new System.EventHandler(this.NextBTN_Click);
            // 
            // BackBTN
            // 
            this.BackBTN.Location = new System.Drawing.Point(463, 3);
            this.BackBTN.Name = "BackBTN";
            this.BackBTN.Size = new System.Drawing.Size(75, 23);
            this.BackBTN.TabIndex = 7;
            this.BackBTN.Text = "Back";
            this.BackBTN.UseVisualStyleBackColor = true;
            this.BackBTN.Click += new System.EventHandler(this.BackBTN_Click);
            // 
            // ImageList
            // 
            this.ImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
            this.ImageList.ImageSize = new System.Drawing.Size(16, 16);
            this.ImageList.TransparentColor = System.Drawing.Color.Transparent;
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
            this.StatusCTRL.Location = new System.Drawing.Point(0, 311);
            this.StatusCTRL.Name = "StatusCTRL";
            this.StatusCTRL.Size = new System.Drawing.Size(784, 22);
            this.StatusCTRL.SizingGrip = false;
            this.StatusCTRL.TabIndex = 13;
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
            this.SubscriptionStateTB.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this.SubscriptionStateTB_DropDownItemClicked);
            // 
            // Subscription_EditMI
            // 
            this.Subscription_EditMI.Name = "Subscription_EditMI";
            this.Subscription_EditMI.Size = new System.Drawing.Size(103, 22);
            this.Subscription_EditMI.Text = "Edit...";
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
            // ItemsDV
            // 
            this.ItemsDV.AllowUserToAddRows = false;
            this.ItemsDV.AllowUserToDeleteRows = false;
            this.ItemsDV.AllowUserToResizeRows = false;
            this.ItemsDV.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ItemsDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ItemsDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.IconCH,
            this.NodeAttributeCH,
            this.MonitoringModeCH,
            this.SamplingIntervalCH,
            this.DiscardOldestCH,
            this.OperationStatusCH});
            this.ItemsDV.ContextMenuStrip = this.PopupMenu;
            this.ItemsDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ItemsDV.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ItemsDV.Location = new System.Drawing.Point(0, 0);
            this.ItemsDV.Name = "ItemsDV";
            this.ItemsDV.RowHeadersVisible = false;
            this.ItemsDV.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ItemsDV.Size = new System.Drawing.Size(784, 311);
            this.ItemsDV.TabIndex = 14;
            this.ItemsDV.DoubleClick += new System.EventHandler(this.ItemsDV_DoubleClick);
            // 
            // IconCH
            // 
            this.IconCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.IconCH.DataPropertyName = "Icon";
            this.IconCH.HeaderText = "";
            this.IconCH.Name = "IconCH";
            this.IconCH.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.IconCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.IconCH.Width = 19;
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
            // DiscardOldestCH
            // 
            this.DiscardOldestCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DiscardOldestCH.DataPropertyName = "DiscardOldest";
            this.DiscardOldestCH.HeaderText = "Discard Oldest";
            this.DiscardOldestCH.Name = "DiscardOldestCH";
            this.DiscardOldestCH.ReadOnly = true;
            this.DiscardOldestCH.Visible = false;
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
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewMI,
            this.EditMI,
            this.DeleteMI,
            this.SetMonitoringModeMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(197, 92);
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
            // SetMonitoringModeMI
            // 
            this.SetMonitoringModeMI.Name = "SetMonitoringModeMI";
            this.SetMonitoringModeMI.Size = new System.Drawing.Size(196, 22);
            this.SetMonitoringModeMI.Text = "Set Monitoring Mode...";
            this.SetMonitoringModeMI.Click += new System.EventHandler(this.SetMonitoringModeMI_Click);
            // 
            // EventTypePN
            // 
            this.EventTypePN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventTypePN.Location = new System.Drawing.Point(0, 0);
            this.EventTypePN.Name = "EventTypePN";
            // 
            // EventTypePN.Panel1
            // 
            this.EventTypePN.Panel1.Controls.Add(this.BrowseCTRL);
            // 
            // EventTypePN.Panel2
            // 
            this.EventTypePN.Panel2.Controls.Add(this.EventTypeCTRL);
            this.EventTypePN.Size = new System.Drawing.Size(784, 311);
            this.EventTypePN.SplitterDistance = 261;
            this.EventTypePN.TabIndex = 17;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesControl = null;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.Size = new System.Drawing.Size(261, 311);
            this.BrowseCTRL.TabIndex = 15;
            this.BrowseCTRL.View = null;
            this.BrowseCTRL.AfterSelect += new System.EventHandler(this.BrowseCTRL_AfterSelect);
            // 
            // EventTypeCTRL
            // 
            this.EventTypeCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventTypeCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventTypeCTRL.Name = "EventTypeCTRL";
            this.EventTypeCTRL.Size = new System.Drawing.Size(519, 311);
            this.EventTypeCTRL.TabIndex = 16;
            // 
            // EventsCTRL
            // 
            this.EventsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventsCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventsCTRL.Name = "EventsCTRL";
            this.EventsCTRL.Size = new System.Drawing.Size(784, 311);
            this.EventsCTRL.TabIndex = 18;
            // 
            // EventFilterCTRL
            // 
            this.EventFilterCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventFilterCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventFilterCTRL.Name = "EventFilterCTRL";
            this.EventFilterCTRL.Size = new System.Drawing.Size(784, 311);
            this.EventFilterCTRL.TabIndex = 17;
            // 
            // SubscribeEventsDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(784, 362);
            this.Controls.Add(this.EventsCTRL);
            this.Controls.Add(this.EventTypePN);
            this.Controls.Add(this.ItemsDV);
            this.Controls.Add(this.EventFilterCTRL);
            this.Controls.Add(this.StatusCTRL);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SubscribeEventsDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Event Subscription";
            this.ButtonsPN.ResumeLayout(false);
            this.StatusCTRL.ResumeLayout(false);
            this.StatusCTRL.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemsDV)).EndInit();
            this.PopupMenu.ResumeLayout(false);
            this.EventTypePN.Panel1.ResumeLayout(false);
            this.EventTypePN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.EventTypePN)).EndInit();
            this.EventTypePN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.FlowLayoutPanel ButtonsPN;
        private System.Windows.Forms.Button NextBTN;
        private System.Windows.Forms.Button BackBTN;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.StatusStrip StatusCTRL;
        private System.Windows.Forms.ToolStripStatusLabel SubscriptionStateLB;
        private System.Windows.Forms.ToolStripDropDownButton SubscriptionStateTB;
        private System.Windows.Forms.ToolStripMenuItem Subscription_EditMI;
        private System.Windows.Forms.ToolStripStatusLabel SequenceNumberLB;
        private System.Windows.Forms.ToolStripStatusLabel SequenceNumberTB;
        private System.Windows.Forms.ToolStripStatusLabel LastNotificationLB;
        private System.Windows.Forms.ToolStripStatusLabel LastNotificationTB;
        private System.Windows.Forms.DataGridView ItemsDV;
        private BrowseTreeViewCtrl BrowseCTRL;
        private System.Windows.Forms.DataGridViewImageColumn IconCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn NodeAttributeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn MonitoringModeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn SamplingIntervalCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DiscardOldestCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn OperationStatusCH;
        private TypeFieldsListViewCtrl EventTypeCTRL;
        private System.Windows.Forms.SplitContainer EventTypePN;
        private EventFilterListViewCtrl EventFilterCTRL;
        private EventListViewCtrl EventsCTRL;
        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem NewMI;
        private System.Windows.Forms.ToolStripMenuItem EditMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.ToolStripMenuItem SetMonitoringModeMI;
    }
}
