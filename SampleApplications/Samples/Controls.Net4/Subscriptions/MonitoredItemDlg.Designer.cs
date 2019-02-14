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

namespace Opc.Ua.Sample.Controls
{
    partial class MonitoredItemDlg
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
            this.MainMenuCTRL = new System.Windows.Forms.MenuStrip();
            this.SubscriptionMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MonitoredItemEditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MonitoringModeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MonitoringModeReportingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MonitoringModeSamplingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MonitoringModeDisabledMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowStatusMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowLatestValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowHistoryMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBarCTRL = new System.Windows.Forms.StatusStrip();
            this.MonitoringModeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.MonitoringModeTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastUpdateTimeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastUpdateTimeTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastMessageIdLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastMessageIdTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SplitterPN = new System.Windows.Forms.SplitContainer();
            this.MonitoredItemsCTRL = new Opc.Ua.Sample.Controls.MonitoredItemStatusCtrl();
            this.LatestValueCTRL = new Opc.Ua.Sample.Controls.DataListCtrl();
            this.DataChangesCTRL = new Opc.Ua.Sample.Controls.DataChangeNotificationListCtrl();
            this.EventsCTRL = new Opc.Ua.Sample.Controls.EventNotificationListListCtrl();
            this.MainPN = new System.Windows.Forms.Panel();
            this.MainMenuCTRL.SuspendLayout();
            this.StatusBarCTRL.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.SplitterPN)).BeginInit();
            this.SplitterPN.Panel1.SuspendLayout();
            this.SplitterPN.Panel2.SuspendLayout();
            this.SplitterPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainMenuCTRL
            // 
            this.MainMenuCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SubscriptionMI,
            this.WindowMI});
            this.MainMenuCTRL.Location = new System.Drawing.Point(0, 0);
            this.MainMenuCTRL.Name = "MainMenuCTRL";
            this.MainMenuCTRL.Size = new System.Drawing.Size(648, 24);
            this.MainMenuCTRL.TabIndex = 2;
            this.MainMenuCTRL.Text = "menuStrip1";
            // 
            // SubscriptionMI
            // 
            this.SubscriptionMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.MonitoredItemEditMI,
            this.MonitoringModeMI});
            this.SubscriptionMI.Name = "SubscriptionMI";
            this.SubscriptionMI.Size = new System.Drawing.Size(102, 20);
            this.SubscriptionMI.Text = "Monitored Item";
            // 
            // MonitoredItemEditMI
            // 
            this.MonitoredItemEditMI.Name = "MonitoredItemEditMI";
            this.MonitoredItemEditMI.Size = new System.Drawing.Size(168, 22);
            this.MonitoredItemEditMI.Text = "Edit..";
            // 
            // MonitoringModeMI
            // 
            this.MonitoringModeMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.MonitoringModeReportingMI,
            this.MonitoringModeSamplingMI,
            this.MonitoringModeDisabledMI});
            this.MonitoringModeMI.Name = "MonitoringModeMI";
            this.MonitoringModeMI.Size = new System.Drawing.Size(168, 22);
            this.MonitoringModeMI.Text = "Monitoring Mode";
            this.MonitoringModeMI.DropDownOpening += new System.EventHandler(this.MonitoringModeMI_DropDownOpening);
            // 
            // MonitoringModeReportingMI
            // 
            this.MonitoringModeReportingMI.Name = "MonitoringModeReportingMI";
            this.MonitoringModeReportingMI.Size = new System.Drawing.Size(126, 22);
            this.MonitoringModeReportingMI.Text = "Reporting";
            this.MonitoringModeReportingMI.Click += new System.EventHandler(this.MonitoringModeMI_Click);
            // 
            // MonitoringModeSamplingMI
            // 
            this.MonitoringModeSamplingMI.Name = "MonitoringModeSamplingMI";
            this.MonitoringModeSamplingMI.Size = new System.Drawing.Size(126, 22);
            this.MonitoringModeSamplingMI.Text = "Sampling";
            this.MonitoringModeSamplingMI.Click += new System.EventHandler(this.MonitoringModeMI_Click);
            // 
            // MonitoringModeDisabledMI
            // 
            this.MonitoringModeDisabledMI.Name = "MonitoringModeDisabledMI";
            this.MonitoringModeDisabledMI.Size = new System.Drawing.Size(126, 22);
            this.MonitoringModeDisabledMI.Text = "Disabled";
            this.MonitoringModeDisabledMI.Click += new System.EventHandler(this.MonitoringModeMI_Click);
            // 
            // WindowMI
            // 
            this.WindowMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.WindowStatusMI,
            this.WindowLatestValueMI,
            this.WindowHistoryMI});
            this.WindowMI.Name = "WindowMI";
            this.WindowMI.Size = new System.Drawing.Size(63, 20);
            this.WindowMI.Text = "Window";
            // 
            // WindowStatusMI
            // 
            this.WindowStatusMI.Name = "WindowStatusMI";
            this.WindowStatusMI.Size = new System.Drawing.Size(151, 22);
            this.WindowStatusMI.Text = "Status";
            this.WindowStatusMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // WindowLatestValueMI
            // 
            this.WindowLatestValueMI.Name = "WindowLatestValueMI";
            this.WindowLatestValueMI.Size = new System.Drawing.Size(151, 22);
            this.WindowLatestValueMI.Text = "Latest Value";
            this.WindowLatestValueMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // WindowHistoryMI
            // 
            this.WindowHistoryMI.Name = "WindowHistoryMI";
            this.WindowHistoryMI.Size = new System.Drawing.Size(151, 22);
            this.WindowHistoryMI.Text = "Recent History";
            this.WindowHistoryMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // StatusBarCTRL
            // 
            this.StatusBarCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.MonitoringModeLB,
            this.MonitoringModeTB,
            this.LastUpdateTimeLB,
            this.LastUpdateTimeTB,
            this.LastMessageIdLB,
            this.LastMessageIdTB});
            this.StatusBarCTRL.Location = new System.Drawing.Point(0, 577);
            this.StatusBarCTRL.Name = "StatusBarCTRL";
            this.StatusBarCTRL.Size = new System.Drawing.Size(648, 22);
            this.StatusBarCTRL.TabIndex = 3;
            this.StatusBarCTRL.Text = "statusStrip1";
            // 
            // MonitoringModeLB
            // 
            this.MonitoringModeLB.Name = "MonitoringModeLB";
            this.MonitoringModeLB.Size = new System.Drawing.Size(104, 17);
            this.MonitoringModeLB.Text = "Monitoring Mode:";
            // 
            // MonitoringModeTB
            // 
            this.MonitoringModeTB.Name = "MonitoringModeTB";
            this.MonitoringModeTB.Size = new System.Drawing.Size(49, 17);
            this.MonitoringModeTB.Text = "Enabled";
            // 
            // LastUpdateTimeLB
            // 
            this.LastUpdateTimeLB.Name = "LastUpdateTimeLB";
            this.LastUpdateTimeLB.Size = new System.Drawing.Size(73, 17);
            this.LastUpdateTimeLB.Text = "Last Publish:";
            // 
            // LastUpdateTimeTB
            // 
            this.LastUpdateTimeTB.Name = "LastUpdateTimeTB";
            this.LastUpdateTimeTB.Size = new System.Drawing.Size(49, 17);
            this.LastUpdateTimeTB.Text = "12:00:00";
            // 
            // LastMessageIdLB
            // 
            this.LastMessageIdLB.Name = "LastMessageIdLB";
            this.LastMessageIdLB.Size = new System.Drawing.Size(80, 17);
            this.LastMessageIdLB.Text = "Last Message:";
            // 
            // LastMessageIdTB
            // 
            this.LastMessageIdTB.Name = "LastMessageIdTB";
            this.LastMessageIdTB.Size = new System.Drawing.Size(13, 17);
            this.LastMessageIdTB.Text = "1";
            // 
            // SplitterPN
            // 
            this.SplitterPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SplitterPN.Location = new System.Drawing.Point(4, 4);
            this.SplitterPN.Name = "SplitterPN";
            this.SplitterPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // SplitterPN.Panel1
            // 
            this.SplitterPN.Panel1.Controls.Add(this.MonitoredItemsCTRL);
            // 
            // SplitterPN.Panel2
            // 
            this.SplitterPN.Panel2.Controls.Add(this.LatestValueCTRL);
            this.SplitterPN.Panel2.Controls.Add(this.DataChangesCTRL);
            this.SplitterPN.Panel2.Controls.Add(this.EventsCTRL);
            this.SplitterPN.Size = new System.Drawing.Size(640, 545);
            this.SplitterPN.SplitterDistance = 57;
            this.SplitterPN.TabIndex = 6;
            // 
            // MonitoredItemsCTRL
            // 
            this.MonitoredItemsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MonitoredItemsCTRL.Instructions = "There are no monitored items in this subscription.";
            this.MonitoredItemsCTRL.Location = new System.Drawing.Point(0, 0);
            this.MonitoredItemsCTRL.Name = "MonitoredItemsCTRL";
            this.MonitoredItemsCTRL.Size = new System.Drawing.Size(640, 57);
            this.MonitoredItemsCTRL.TabIndex = 4;
            // 
            // LatestValueCTRL
            // 
            this.LatestValueCTRL.AutoUpdate = true;
            this.LatestValueCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.LatestValueCTRL.Instructions = null;
            this.LatestValueCTRL.LatestValue = true;
            this.LatestValueCTRL.Location = new System.Drawing.Point(0, 0);
            this.LatestValueCTRL.MonitoredItem = null;
            this.LatestValueCTRL.Name = "LatestValueCTRL";
            this.LatestValueCTRL.Size = new System.Drawing.Size(640, 484);
            this.LatestValueCTRL.TabIndex = 6;
            // 
            // DataChangesCTRL
            // 
            this.DataChangesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataChangesCTRL.Instructions = "There are no event notifications to display.";
            this.DataChangesCTRL.Location = new System.Drawing.Point(0, 0);
            this.DataChangesCTRL.Name = "DataChangesCTRL";
            this.DataChangesCTRL.ShowHistory = true;
            this.DataChangesCTRL.Size = new System.Drawing.Size(640, 484);
            this.DataChangesCTRL.TabIndex = 5;
            // 
            // EventsCTRL
            // 
            this.EventsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventsCTRL.Instructions = "There are no event notifications to display.";
            this.EventsCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventsCTRL.Name = "EventsCTRL";
            this.EventsCTRL.Size = new System.Drawing.Size(640, 484);
            this.EventsCTRL.TabIndex = 1;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.SplitterPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 24);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(4);
            this.MainPN.Size = new System.Drawing.Size(648, 553);
            this.MainPN.TabIndex = 7;
            // 
            // MonitoredItemDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(648, 599);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBarCTRL);
            this.Controls.Add(this.MainMenuCTRL);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MainMenuStrip = this.MainMenuCTRL;
            this.MaximizeBox = false;
            this.Name = "MonitoredItemDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Monitor Subscription";
            this.MainMenuCTRL.ResumeLayout(false);
            this.MainMenuCTRL.PerformLayout();
            this.StatusBarCTRL.ResumeLayout(false);
            this.StatusBarCTRL.PerformLayout();
            this.SplitterPN.Panel1.ResumeLayout(false);
            this.SplitterPN.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.SplitterPN)).EndInit();
            this.SplitterPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MainMenuCTRL;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionMI;
        private System.Windows.Forms.ToolStripMenuItem MonitoringModeMI;
        private System.Windows.Forms.ToolStripMenuItem WindowMI;
        private System.Windows.Forms.StatusStrip StatusBarCTRL;
        private System.Windows.Forms.ToolStripStatusLabel LastUpdateTimeLB;
        private System.Windows.Forms.ToolStripStatusLabel MonitoringModeLB;
        private System.Windows.Forms.ToolStripStatusLabel MonitoringModeTB;
        private System.Windows.Forms.ToolStripStatusLabel LastUpdateTimeTB;
        private System.Windows.Forms.ToolStripStatusLabel LastMessageIdLB;
        private System.Windows.Forms.ToolStripStatusLabel LastMessageIdTB;
        private MonitoredItemStatusCtrl MonitoredItemsCTRL;
        private System.Windows.Forms.ToolStripMenuItem WindowStatusMI;
        private System.Windows.Forms.ToolStripMenuItem WindowLatestValueMI;
        private System.Windows.Forms.ToolStripMenuItem WindowHistoryMI;
        private System.Windows.Forms.SplitContainer SplitterPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem MonitoringModeReportingMI;
        private System.Windows.Forms.ToolStripMenuItem MonitoringModeSamplingMI;
        private System.Windows.Forms.ToolStripMenuItem MonitoringModeDisabledMI;
        private System.Windows.Forms.ToolStripMenuItem MonitoredItemEditMI;
        private EventNotificationListListCtrl EventsCTRL;
        private DataChangeNotificationListCtrl DataChangesCTRL;
        private DataListCtrl LatestValueCTRL;
    }
}
