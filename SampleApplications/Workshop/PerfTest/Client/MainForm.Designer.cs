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

namespace Quickstarts.PerfTestClient
{
    partial class MainForm
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
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.ConnectedLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerUrlLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.LastKeepAliveTimeLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.StopBTN = new System.Windows.Forms.Button();
            this.TotalItemUpdateRateLB = new System.Windows.Forms.Label();
            this.MessageRateLB = new System.Windows.Forms.Label();
            this.ItemCountCTRL = new System.Windows.Forms.NumericUpDown();
            this.UpdateRateCTRL = new System.Windows.Forms.NumericUpDown();
            this.ItemCountLB = new System.Windows.Forms.Label();
            this.UpdateRateLB = new System.Windows.Forms.Label();
            this.TotalItemUpdateRateTB = new System.Windows.Forms.TextBox();
            this.MessageRateTB = new System.Windows.Forms.TextBox();
            this.TotalItemUpdateCountTB = new System.Windows.Forms.TextBox();
            this.TotalItemUpdateCountLB = new System.Windows.Forms.Label();
            this.MessageCountTB = new System.Windows.Forms.TextBox();
            this.MessageCountLB = new System.Windows.Forms.Label();
            this.LogTB = new System.Windows.Forms.RichTextBox();
            this.UpdateTimer = new System.Windows.Forms.Timer(this.components);
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.StatusBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemCountCTRL)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.UpdateRateCTRL)).BeginInit();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(807, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // ServerMI
            // 
            this.ServerMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Server_DiscoverMI,
            this.Server_ConnectMI,
            this.Server_DisconnectMI});
            this.ServerMI.Name = "ServerMI";
            this.ServerMI.Size = new System.Drawing.Size(51, 20);
            this.ServerMI.Text = "Server";
            // 
            // Server_DiscoverMI
            // 
            this.Server_DiscoverMI.Name = "Server_DiscoverMI";
            this.Server_DiscoverMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            // 
            // Server_ConnectMI
            // 
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            // 
            // Server_DisconnectMI
            // 
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(127, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            // 
            // HelpMI
            // 
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_ContentsMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(40, 20);
            this.HelpMI.Text = "Help";
            // 
            // Help_ContentsMI
            // 
            this.Help_ContentsMI.Name = "Help_ContentsMI";
            this.Help_ContentsMI.Size = new System.Drawing.Size(118, 22);
            this.Help_ContentsMI.Text = "Contents";
            // 
            // StatusBar
            // 
            this.StatusBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ConnectedLB,
            this.ServerUrlLB,
            this.LastKeepAliveTimeLB});
            this.StatusBar.Location = new System.Drawing.Point(0, 435);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(807, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // ConnectedLB
            // 
            this.ConnectedLB.Name = "ConnectedLB";
            this.ConnectedLB.Size = new System.Drawing.Size(71, 17);
            this.ConnectedLB.Text = "Disconnected";
            // 
            // ServerUrlLB
            // 
            this.ServerUrlLB.Name = "ServerUrlLB";
            this.ServerUrlLB.Size = new System.Drawing.Size(19, 17);
            this.ServerUrlLB.Text = "---";
            // 
            // LastKeepAliveTimeLB
            // 
            this.LastKeepAliveTimeLB.Name = "LastKeepAliveTimeLB";
            this.LastKeepAliveTimeLB.Size = new System.Drawing.Size(51, 17);
            this.LastKeepAliveTimeLB.Text = "00:00:00";
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.StopBTN);
            this.MainPN.Controls.Add(this.TotalItemUpdateRateLB);
            this.MainPN.Controls.Add(this.MessageRateLB);
            this.MainPN.Controls.Add(this.ItemCountCTRL);
            this.MainPN.Controls.Add(this.UpdateRateCTRL);
            this.MainPN.Controls.Add(this.ItemCountLB);
            this.MainPN.Controls.Add(this.UpdateRateLB);
            this.MainPN.Controls.Add(this.TotalItemUpdateRateTB);
            this.MainPN.Controls.Add(this.MessageRateTB);
            this.MainPN.Controls.Add(this.TotalItemUpdateCountTB);
            this.MainPN.Controls.Add(this.TotalItemUpdateCountLB);
            this.MainPN.Controls.Add(this.MessageCountTB);
            this.MainPN.Controls.Add(this.MessageCountLB);
            this.MainPN.Controls.Add(this.LogTB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(807, 313);
            this.MainPN.TabIndex = 3;
            // 
            // StopBTN
            // 
            this.StopBTN.Location = new System.Drawing.Point(600, 9);
            this.StopBTN.Name = "StopBTN";
            this.StopBTN.Size = new System.Drawing.Size(74, 45);
            this.StopBTN.TabIndex = 28;
            this.StopBTN.Text = "Stop";
            this.StopBTN.UseVisualStyleBackColor = true;
            this.StopBTN.Click += new System.EventHandler(this.StopBTN_Click);
            // 
            // TotalItemUpdateRateLB
            // 
            this.TotalItemUpdateRateLB.AutoSize = true;
            this.TotalItemUpdateRateLB.Location = new System.Drawing.Point(374, 37);
            this.TotalItemUpdateRateLB.Name = "TotalItemUpdateRateLB";
            this.TotalItemUpdateRateLB.Size = new System.Drawing.Size(91, 13);
            this.TotalItemUpdateRateLB.TabIndex = 27;
            this.TotalItemUpdateRateLB.Text = "Item Update Rate";
            // 
            // MessageRateLB
            // 
            this.MessageRateLB.AutoSize = true;
            this.MessageRateLB.Location = new System.Drawing.Point(374, 14);
            this.MessageRateLB.Name = "MessageRateLB";
            this.MessageRateLB.Size = new System.Drawing.Size(76, 13);
            this.MessageRateLB.TabIndex = 26;
            this.MessageRateLB.Text = "Message Rate";
            // 
            // ItemCountCTRL
            // 
            this.ItemCountCTRL.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.ItemCountCTRL.Location = new System.Drawing.Point(115, 33);
            this.ItemCountCTRL.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.ItemCountCTRL.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.ItemCountCTRL.Name = "ItemCountCTRL";
            this.ItemCountCTRL.Size = new System.Drawing.Size(58, 20);
            this.ItemCountCTRL.TabIndex = 25;
            this.ItemCountCTRL.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // UpdateRateCTRL
            // 
            this.UpdateRateCTRL.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.UpdateRateCTRL.Location = new System.Drawing.Point(115, 10);
            this.UpdateRateCTRL.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.UpdateRateCTRL.Minimum = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.UpdateRateCTRL.Name = "UpdateRateCTRL";
            this.UpdateRateCTRL.Size = new System.Drawing.Size(58, 20);
            this.UpdateRateCTRL.TabIndex = 24;
            this.UpdateRateCTRL.Value = new decimal(new int[] {
            200,
            0,
            0,
            0});
            // 
            // ItemCountLB
            // 
            this.ItemCountLB.AutoSize = true;
            this.ItemCountLB.Location = new System.Drawing.Point(13, 37);
            this.ItemCountLB.Name = "ItemCountLB";
            this.ItemCountLB.Size = new System.Drawing.Size(58, 13);
            this.ItemCountLB.TabIndex = 23;
            this.ItemCountLB.Text = "Item Count";
            // 
            // UpdateRateLB
            // 
            this.UpdateRateLB.AutoSize = true;
            this.UpdateRateLB.Location = new System.Drawing.Point(13, 12);
            this.UpdateRateLB.Name = "UpdateRateLB";
            this.UpdateRateLB.Size = new System.Drawing.Size(98, 13);
            this.UpdateRateLB.TabIndex = 22;
            this.UpdateRateLB.Text = "Sampling Rate (ms)";
            // 
            // TotalItemUpdateRateTB
            // 
            this.TotalItemUpdateRateTB.Location = new System.Drawing.Point(471, 33);
            this.TotalItemUpdateRateTB.Name = "TotalItemUpdateRateTB";
            this.TotalItemUpdateRateTB.Size = new System.Drawing.Size(123, 20);
            this.TotalItemUpdateRateTB.TabIndex = 21;
            // 
            // MessageRateTB
            // 
            this.MessageRateTB.Location = new System.Drawing.Point(471, 9);
            this.MessageRateTB.Name = "MessageRateTB";
            this.MessageRateTB.Size = new System.Drawing.Size(123, 20);
            this.MessageRateTB.TabIndex = 20;
            // 
            // TotalItemUpdateCountTB
            // 
            this.TotalItemUpdateCountTB.Location = new System.Drawing.Point(283, 34);
            this.TotalItemUpdateCountTB.Name = "TotalItemUpdateCountTB";
            this.TotalItemUpdateCountTB.Size = new System.Drawing.Size(85, 20);
            this.TotalItemUpdateCountTB.TabIndex = 19;
            // 
            // TotalItemUpdateCountLB
            // 
            this.TotalItemUpdateCountLB.AutoSize = true;
            this.TotalItemUpdateCountLB.Location = new System.Drawing.Point(189, 37);
            this.TotalItemUpdateCountLB.Name = "TotalItemUpdateCountLB";
            this.TotalItemUpdateCountLB.Size = new System.Drawing.Size(96, 13);
            this.TotalItemUpdateCountLB.TabIndex = 18;
            this.TotalItemUpdateCountLB.Text = "Item Update Count";
            // 
            // MessageCountTB
            // 
            this.MessageCountTB.Location = new System.Drawing.Point(283, 10);
            this.MessageCountTB.Name = "MessageCountTB";
            this.MessageCountTB.Size = new System.Drawing.Size(85, 20);
            this.MessageCountTB.TabIndex = 17;
            // 
            // MessageCountLB
            // 
            this.MessageCountLB.AutoSize = true;
            this.MessageCountLB.Location = new System.Drawing.Point(189, 14);
            this.MessageCountLB.Name = "MessageCountLB";
            this.MessageCountLB.Size = new System.Drawing.Size(81, 13);
            this.MessageCountLB.TabIndex = 16;
            this.MessageCountLB.Text = "Message Count";
            // 
            // LogTB
            // 
            this.LogTB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.LogTB.Location = new System.Drawing.Point(16, 59);
            this.LogTB.Name = "LogTB";
            this.LogTB.Size = new System.Drawing.Size(779, 242);
            this.LogTB.TabIndex = 5;
            this.LogTB.Text = "";
            // 
            // UpdateTimer
            // 
            this.UpdateTimer.Interval = 1000;
            this.UpdateTimer.Tick += new System.EventHandler(this.UpdateTimer_Tick);
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 99);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(807, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 4;
            this.ConnectServerCTRL.UserIdentity = null;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectComplete);
            this.ConnectServerCTRL.ReconnectStarting += new System.EventHandler(this.Server_ReconnectStarting);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectComplete);
            // 
            // clientHeaderBranding1
            // 
            this.clientHeaderBranding1.Dock = System.Windows.Forms.DockStyle.Top;
            this.clientHeaderBranding1.Location = new System.Drawing.Point(0, 24);
            this.clientHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 75);
            this.clientHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 75);
            this.clientHeaderBranding1.Name = "clientHeaderBranding1";
            this.clientHeaderBranding1.Size = new System.Drawing.Size(807, 75);
            this.clientHeaderBranding1.TabIndex = 5;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(807, 457);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart Performance Test Client";
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.StatusBar.ResumeLayout(false);
            this.StatusBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ItemCountCTRL)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.UpdateRateCTRL)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.ToolStripStatusLabel ConnectedLB;
        private System.Windows.Forms.ToolStripStatusLabel ServerUrlLB;
        private System.Windows.Forms.ToolStripStatusLabel LastKeepAliveTimeLB;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Timer UpdateTimer;
        private System.Windows.Forms.RichTextBox LogTB;
        private System.Windows.Forms.Label TotalItemUpdateRateLB;
        private System.Windows.Forms.Label MessageRateLB;
        private System.Windows.Forms.NumericUpDown ItemCountCTRL;
        private System.Windows.Forms.NumericUpDown UpdateRateCTRL;
        private System.Windows.Forms.Label ItemCountLB;
        private System.Windows.Forms.Label UpdateRateLB;
        private System.Windows.Forms.TextBox TotalItemUpdateRateTB;
        private System.Windows.Forms.TextBox MessageRateTB;
        private System.Windows.Forms.TextBox TotalItemUpdateCountTB;
        private System.Windows.Forms.Label TotalItemUpdateCountLB;
        private System.Windows.Forms.TextBox MessageCountTB;
        private System.Windows.Forms.Label MessageCountLB;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private System.Windows.Forms.Button StopBTN;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
