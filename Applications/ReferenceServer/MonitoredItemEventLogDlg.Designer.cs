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

namespace Quickstarts.ReferenceServer
{
    partial class MonitoredItemEventLogDlg
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
            this.DataGridCTRL = new System.Windows.Forms.DataGridView();
            this.RefreshTimer = new System.Windows.Forms.Timer(this.components);
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.EventsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Events_MonitorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Events_ClearMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Timestamp = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EventType = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Id = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.NodeId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusCode = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SamplingInterval = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.QueueSize = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Filter = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.DataGridCTRL)).BeginInit();
            this.MenuBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // DataGridCTRL
            // 
            this.DataGridCTRL.AllowUserToAddRows = false;
            this.DataGridCTRL.AllowUserToDeleteRows = false;
            this.DataGridCTRL.AllowUserToOrderColumns = true;
            this.DataGridCTRL.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.DataGridCTRL.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.DataGridCTRL.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.DataGridCTRL.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Timestamp,
            this.EventType,
            this.Id,
            this.NodeId,
            this.Value,
            this.StatusCode,
            this.SamplingInterval,
            this.QueueSize,
            this.Filter});
            this.DataGridCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataGridCTRL.Location = new System.Drawing.Point(0, 24);
            this.DataGridCTRL.Name = "DataGridCTRL";
            this.DataGridCTRL.ReadOnly = true;
            this.DataGridCTRL.Size = new System.Drawing.Size(647, 281);
            this.DataGridCTRL.TabIndex = 0;
            // 
            // RefreshTimer
            // 
            this.RefreshTimer.Interval = 5000;
            this.RefreshTimer.Tick += new System.EventHandler(this.RefreshTimer_Tick);
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.EventsMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(647, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // EventsMI
            // 
            this.EventsMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Events_MonitorMI,
            this.Events_ClearMI});
            this.EventsMI.Name = "EventsMI";
            this.EventsMI.Size = new System.Drawing.Size(53, 20);
            this.EventsMI.Text = "Events";
            // 
            // Events_MonitorMI
            // 
            this.Events_MonitorMI.Name = "Events_MonitorMI";
            this.Events_MonitorMI.Size = new System.Drawing.Size(117, 22);
            this.Events_MonitorMI.Text = "Monitor";
            // 
            // Events_ClearMI
            // 
            this.Events_ClearMI.Name = "Events_ClearMI";
            this.Events_ClearMI.Size = new System.Drawing.Size(117, 22);
            this.Events_ClearMI.Text = "Clear";
            this.Events_ClearMI.Click += new System.EventHandler(this.Events_ClearMI_Click);
            // 
            // Timestamp
            // 
            this.Timestamp.DataPropertyName = "Timestamp";
            this.Timestamp.HeaderText = "Timestamp";
            this.Timestamp.Name = "Timestamp";
            this.Timestamp.ReadOnly = true;
            this.Timestamp.Width = 83;
            // 
            // EventType
            // 
            this.EventType.DataPropertyName = "EventType";
            this.EventType.HeaderText = "EventType";
            this.EventType.Name = "EventType";
            this.EventType.ReadOnly = true;
            this.EventType.Width = 84;
            // 
            // Id
            // 
            this.Id.DataPropertyName = "Id";
            this.Id.HeaderText = "Id";
            this.Id.Name = "Id";
            this.Id.ReadOnly = true;
            this.Id.Width = 41;
            // 
            // NodeId
            // 
            this.NodeId.DataPropertyName = "NodeId";
            this.NodeId.HeaderText = "NodeId";
            this.NodeId.Name = "NodeId";
            this.NodeId.ReadOnly = true;
            this.NodeId.Width = 67;
            // 
            // Value
            // 
            this.Value.DataPropertyName = "Value";
            this.Value.HeaderText = "Value";
            this.Value.Name = "Value";
            this.Value.ReadOnly = true;
            this.Value.Width = 59;
            // 
            // StatusCode
            // 
            this.StatusCode.DataPropertyName = "StatusCode";
            this.StatusCode.HeaderText = "StatusCode";
            this.StatusCode.Name = "StatusCode";
            this.StatusCode.ReadOnly = true;
            this.StatusCode.Width = 87;
            // 
            // SamplingInterval
            // 
            this.SamplingInterval.DataPropertyName = "SamplingInterval";
            this.SamplingInterval.HeaderText = "SamplingInterval";
            this.SamplingInterval.Name = "SamplingInterval";
            this.SamplingInterval.ReadOnly = true;
            this.SamplingInterval.Width = 110;
            // 
            // QueueSize
            // 
            this.QueueSize.DataPropertyName = "QueueSize";
            this.QueueSize.HeaderText = "QueueSize";
            this.QueueSize.Name = "QueueSize";
            this.QueueSize.ReadOnly = true;
            this.QueueSize.Width = 84;
            // 
            // Filter
            // 
            this.Filter.DataPropertyName = "Filter";
            this.Filter.HeaderText = "Filter";
            this.Filter.Name = "Filter";
            this.Filter.ReadOnly = true;
            this.Filter.Width = 54;
            // 
            // MonitoredItemEventLogDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(647, 305);
            this.Controls.Add(this.DataGridCTRL);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MonitoredItemEventLogDlg";
            this.Text = "Monitored Item Event Log";
            ((System.ComponentModel.ISupportInitialize)(this.DataGridCTRL)).EndInit();
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView DataGridCTRL;
        private System.Windows.Forms.Timer RefreshTimer;
        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.ToolStripMenuItem EventsMI;
        private System.Windows.Forms.ToolStripMenuItem Events_MonitorMI;
        private System.Windows.Forms.ToolStripMenuItem Events_ClearMI;
        private System.Windows.Forms.DataGridViewTextBoxColumn Timestamp;
        private System.Windows.Forms.DataGridViewTextBoxColumn EventType;
        private System.Windows.Forms.DataGridViewTextBoxColumn Id;
        private System.Windows.Forms.DataGridViewTextBoxColumn NodeId;
        private System.Windows.Forms.DataGridViewTextBoxColumn Value;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusCode;
        private System.Windows.Forms.DataGridViewTextBoxColumn SamplingInterval;
        private System.Windows.Forms.DataGridViewTextBoxColumn QueueSize;
        private System.Windows.Forms.DataGridViewTextBoxColumn Filter;
    }
}
