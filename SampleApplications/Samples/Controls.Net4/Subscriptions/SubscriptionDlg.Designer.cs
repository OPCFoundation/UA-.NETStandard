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
    partial class SubscriptionDlg
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
            this.EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionEnablePublishingMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionCreateItemMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SubscriptionCreateItemFromTypeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowMonitoredItemsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowEventsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.WindowDataChangesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ConditionsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ConditionRefreshMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBarCTRL = new System.Windows.Forms.StatusStrip();
            this.PublishingEnabledLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.PublishingEnabledTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastUpdateTimeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastUpdateTimeTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastMessageIdLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastMessageIdTB = new System.Windows.Forms.ToolStripStatusLabel();
            this.SplitterPN = new System.Windows.Forms.SplitContainer();
            this.MonitoredItemsCTRL = new Opc.Ua.Sample.Controls.MonitoredItemConfigCtrl();
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
            this.WindowMI,
            this.ConditionsMI});
            this.MainMenuCTRL.Location = new System.Drawing.Point(0, 0);
            this.MainMenuCTRL.Name = "MainMenuCTRL";
            this.MainMenuCTRL.Size = new System.Drawing.Size(811, 24);
            this.MainMenuCTRL.TabIndex = 2;
            this.MainMenuCTRL.Text = "menuStrip1";
            // 
            // SubscriptionMI
            // 
            this.SubscriptionMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.EditMI,
            this.SubscriptionEnablePublishingMI,
            this.SubscriptionCreateItemMI,
            this.SubscriptionCreateItemFromTypeMI});
            this.SubscriptionMI.Name = "SubscriptionMI";
            this.SubscriptionMI.Size = new System.Drawing.Size(85, 20);
            this.SubscriptionMI.Text = "Subscription";
            this.SubscriptionMI.DropDownOpening += new System.EventHandler(this.SubscriptionMI_DropDownOpening);
            // 
            // EditMI
            // 
            this.EditMI.Name = "EditMI";
            this.EditMI.Size = new System.Drawing.Size(306, 22);
            this.EditMI.Text = "Edit...";
            this.EditMI.Click += new System.EventHandler(this.EditMI_Click);
            // 
            // SubscriptionEnablePublishingMI
            // 
            this.SubscriptionEnablePublishingMI.CheckOnClick = true;
            this.SubscriptionEnablePublishingMI.Name = "SubscriptionEnablePublishingMI";
            this.SubscriptionEnablePublishingMI.Size = new System.Drawing.Size(306, 22);
            this.SubscriptionEnablePublishingMI.Text = "Enable Publishing";
            this.SubscriptionEnablePublishingMI.Click += new System.EventHandler(this.SubscriptionEnablePublishingMI_Click);
            // 
            // SubscriptionCreateItemMI
            // 
            this.SubscriptionCreateItemMI.Name = "SubscriptionCreateItemMI";
            this.SubscriptionCreateItemMI.Size = new System.Drawing.Size(306, 22);
            this.SubscriptionCreateItemMI.Text = "Create Monitored Items...";
            this.SubscriptionCreateItemMI.Click += new System.EventHandler(this.SubscriptionCreateItemMI_Click);
            // 
            // SubscriptionCreateItemFromTypeMI
            // 
            this.SubscriptionCreateItemFromTypeMI.Name = "SubscriptionCreateItemFromTypeMI";
            this.SubscriptionCreateItemFromTypeMI.Size = new System.Drawing.Size(306, 22);
            this.SubscriptionCreateItemFromTypeMI.Text = "Create Monitored Items from Type Model....";
            this.SubscriptionCreateItemFromTypeMI.Click += new System.EventHandler(this.SubscriptionCreateItemFromTypeMI_Click);
            // 
            // WindowMI
            // 
            this.WindowMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.WindowMonitoredItemsMI,
            this.WindowEventsMI,
            this.WindowDataChangesMI});
            this.WindowMI.Name = "WindowMI";
            this.WindowMI.Size = new System.Drawing.Size(63, 20);
            this.WindowMI.Text = "Window";
            // 
            // WindowMonitoredItemsMI
            // 
            this.WindowMonitoredItemsMI.Name = "WindowMonitoredItemsMI";
            this.WindowMonitoredItemsMI.Size = new System.Drawing.Size(162, 22);
            this.WindowMonitoredItemsMI.Text = "Monitored Items";
            this.WindowMonitoredItemsMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // WindowEventsMI
            // 
            this.WindowEventsMI.Name = "WindowEventsMI";
            this.WindowEventsMI.Size = new System.Drawing.Size(162, 22);
            this.WindowEventsMI.Text = "Events";
            this.WindowEventsMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // WindowDataChangesMI
            // 
            this.WindowDataChangesMI.Name = "WindowDataChangesMI";
            this.WindowDataChangesMI.Size = new System.Drawing.Size(162, 22);
            this.WindowDataChangesMI.Text = "Data Changes";
            this.WindowDataChangesMI.Click += new System.EventHandler(this.WindowMI_Click);
            // 
            // ConditionsMI
            // 
            this.ConditionsMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ConditionRefreshMI});
            this.ConditionsMI.Name = "ConditionsMI";
            this.ConditionsMI.Size = new System.Drawing.Size(77, 20);
            this.ConditionsMI.Text = "Conditions";
            // 
            // ConditionRefreshMI
            // 
            this.ConditionRefreshMI.Name = "ConditionRefreshMI";
            this.ConditionRefreshMI.Size = new System.Drawing.Size(122, 22);
            this.ConditionRefreshMI.Text = "Refresh...";
            this.ConditionRefreshMI.Click += new System.EventHandler(this.ConditionRefreshMI_Click);
            // 
            // StatusBarCTRL
            // 
            this.StatusBarCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.PublishingEnabledLB,
            this.PublishingEnabledTB,
            this.LastUpdateTimeLB,
            this.LastUpdateTimeTB,
            this.LastMessageIdLB,
            this.LastMessageIdTB});
            this.StatusBarCTRL.Location = new System.Drawing.Point(0, 577);
            this.StatusBarCTRL.Name = "StatusBarCTRL";
            this.StatusBarCTRL.Size = new System.Drawing.Size(811, 22);
            this.StatusBarCTRL.TabIndex = 3;
            this.StatusBarCTRL.Text = "statusStrip1";
            // 
            // PublishingEnabledLB
            // 
            this.PublishingEnabledLB.Name = "PublishingEnabledLB";
            this.PublishingEnabledLB.Size = new System.Drawing.Size(66, 17);
            this.PublishingEnabledLB.Text = "Publishing:";
            // 
            // PublishingEnabledTB
            // 
            this.PublishingEnabledTB.Name = "PublishingEnabledTB";
            this.PublishingEnabledTB.Size = new System.Drawing.Size(49, 17);
            this.PublishingEnabledTB.Text = "Enabled";
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
            this.SplitterPN.Panel2.Controls.Add(this.DataChangesCTRL);
            this.SplitterPN.Panel2.Controls.Add(this.EventsCTRL);
            this.SplitterPN.Size = new System.Drawing.Size(803, 545);
            this.SplitterPN.SplitterDistance = 165;
            this.SplitterPN.TabIndex = 6;
            // 
            // MonitoredItemsCTRL
            // 
            this.MonitoredItemsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MonitoredItemsCTRL.Instructions = "There are no monitored items in this subscription.";
            this.MonitoredItemsCTRL.Location = new System.Drawing.Point(0, 0);
            this.MonitoredItemsCTRL.Name = "MonitoredItemsCTRL";
            this.MonitoredItemsCTRL.Size = new System.Drawing.Size(803, 165);
            this.MonitoredItemsCTRL.TabIndex = 4;
            // 
            // DataChangesCTRL
            // 
            this.DataChangesCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DataChangesCTRL.Instructions = "There are no data change notifications to display.";
            this.DataChangesCTRL.Location = new System.Drawing.Point(0, 0);
            this.DataChangesCTRL.Name = "DataChangesCTRL";
            this.DataChangesCTRL.Size = new System.Drawing.Size(803, 376);
            this.DataChangesCTRL.TabIndex = 5;
            // 
            // EventsCTRL
            // 
            this.EventsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventsCTRL.Instructions = "There are no event notifications to display.";
            this.EventsCTRL.Location = new System.Drawing.Point(0, 0);
            this.EventsCTRL.Name = "EventsCTRL";
            this.EventsCTRL.Size = new System.Drawing.Size(803, 376);
            this.EventsCTRL.TabIndex = 1;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.SplitterPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 24);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(4);
            this.MainPN.Size = new System.Drawing.Size(811, 553);
            this.MainPN.TabIndex = 7;
            // 
            // SubscriptionDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(811, 599);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBarCTRL);
            this.Controls.Add(this.MainMenuCTRL);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MainMenuStrip = this.MainMenuCTRL;
            this.MaximizeBox = false;
            this.Name = "SubscriptionDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Monitor Subscription";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SubscriptionDlg_FormClosing);
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

        private EventNotificationListListCtrl EventsCTRL;
        private System.Windows.Forms.MenuStrip MainMenuCTRL;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionEnablePublishingMI;
        private System.Windows.Forms.ToolStripMenuItem WindowMI;
        private System.Windows.Forms.StatusStrip StatusBarCTRL;
        private System.Windows.Forms.ToolStripStatusLabel LastUpdateTimeLB;
        private System.Windows.Forms.ToolStripStatusLabel PublishingEnabledLB;
        private System.Windows.Forms.ToolStripStatusLabel PublishingEnabledTB;
        private System.Windows.Forms.ToolStripStatusLabel LastUpdateTimeTB;
        private System.Windows.Forms.ToolStripStatusLabel LastMessageIdLB;
        private System.Windows.Forms.ToolStripStatusLabel LastMessageIdTB;
        private MonitoredItemConfigCtrl MonitoredItemsCTRL;
        private System.Windows.Forms.ToolStripMenuItem WindowMonitoredItemsMI;
        private System.Windows.Forms.ToolStripMenuItem WindowEventsMI;
        private System.Windows.Forms.ToolStripMenuItem WindowDataChangesMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionCreateItemMI;
        private DataChangeNotificationListCtrl DataChangesCTRL;
        private System.Windows.Forms.SplitContainer SplitterPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem EditMI;
        private System.Windows.Forms.ToolStripMenuItem SubscriptionCreateItemFromTypeMI;
        private System.Windows.Forms.ToolStripMenuItem ConditionsMI;
        private System.Windows.Forms.ToolStripMenuItem ConditionRefreshMI;
    }
}
