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

namespace Quickstarts.HistoricalAccess.Client
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
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.eToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.AggregatesMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Aggregates_EnableSubscriptionMI = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Aggregates_SelectVariableMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Aggregates_SelectAggregateTypeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewHistoricalConfigurationMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Aggregates_ModifyAggregateFilterMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ReadCTRL = new Opc.Ua.Client.Controls.HistoryDataListView();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.ConsoleTB = new System.Windows.Forms.RichTextBox();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.ServerMI,
            this.AggregatesMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(899, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.eToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(35, 20);
            this.fileToolStripMenuItem.Text = "&File";
            // 
            // eToolStripMenuItem
            // 
            this.eToolStripMenuItem.Name = "eToolStripMenuItem";
            this.eToolStripMenuItem.Size = new System.Drawing.Size(92, 22);
            this.eToolStripMenuItem.Text = "E&xit";
            this.eToolStripMenuItem.Click += new System.EventHandler(this.ExitToolStripMenuItem_Click);
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
            // AggregatesMI
            // 
            this.AggregatesMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Aggregates_EnableSubscriptionMI,
            this.toolStripSeparator1,
            this.Aggregates_SelectVariableMI,
            this.Aggregates_SelectAggregateTypeMI,
            this.ViewHistoricalConfigurationMI,
            this.Aggregates_ModifyAggregateFilterMI});
            this.AggregatesMI.Name = "AggregatesMI";
            this.AggregatesMI.Size = new System.Drawing.Size(75, 20);
            this.AggregatesMI.Text = "Aggregates";
            // 
            // Aggregates_EnableSubscriptionMI
            // 
            this.Aggregates_EnableSubscriptionMI.Checked = true;
            this.Aggregates_EnableSubscriptionMI.CheckOnClick = true;
            this.Aggregates_EnableSubscriptionMI.CheckState = System.Windows.Forms.CheckState.Checked;
            this.Aggregates_EnableSubscriptionMI.Name = "Aggregates_EnableSubscriptionMI";
            this.Aggregates_EnableSubscriptionMI.Size = new System.Drawing.Size(222, 22);
            this.Aggregates_EnableSubscriptionMI.Text = "Enable Subscription";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(219, 6);
            // 
            // Aggregates_SelectVariableMI
            // 
            this.Aggregates_SelectVariableMI.Name = "Aggregates_SelectVariableMI";
            this.Aggregates_SelectVariableMI.Size = new System.Drawing.Size(222, 22);
            this.Aggregates_SelectVariableMI.Text = "Select Variable...";
            this.Aggregates_SelectVariableMI.Click += new System.EventHandler(this.Aggregates_SelectVariableMI_Click);
            // 
            // Aggregates_SelectAggregateTypeMI
            // 
            this.Aggregates_SelectAggregateTypeMI.Name = "Aggregates_SelectAggregateTypeMI";
            this.Aggregates_SelectAggregateTypeMI.Size = new System.Drawing.Size(222, 22);
            this.Aggregates_SelectAggregateTypeMI.Text = "Select Aggregate Type...";
            // 
            // ViewHistoricalConfigurationMI
            // 
            this.ViewHistoricalConfigurationMI.Name = "ViewHistoricalConfigurationMI";
            this.ViewHistoricalConfigurationMI.Size = new System.Drawing.Size(222, 22);
            this.ViewHistoricalConfigurationMI.Text = "View Historical Configuration...";
            this.ViewHistoricalConfigurationMI.Click += new System.EventHandler(this.ViewHistoricalConfigurationMI_Click);
            // 
            // Aggregates_ModifyAggregateFilterMI
            // 
            this.Aggregates_ModifyAggregateFilterMI.Name = "Aggregates_ModifyAggregateFilterMI";
            this.Aggregates_ModifyAggregateFilterMI.Size = new System.Drawing.Size(222, 22);
            this.Aggregates_ModifyAggregateFilterMI.Text = "Modify Aggregate Filter...";
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
            this.Help_ContentsMI.Size = new System.Drawing.Size(152, 22);
            this.Help_ContentsMI.Text = "Contents";
            this.Help_ContentsMI.Click += new System.EventHandler(this.Help_ContentsMI_Click);
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ReadCTRL);
            this.MainPN.Controls.Add(this.ConnectServerCTRL);
            this.MainPN.Controls.Add(this.ConsoleTB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 99);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(899, 427);
            this.MainPN.TabIndex = 3;
            // 
            // ReadCTRL
            // 
            this.ReadCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReadCTRL.EndTime = new System.DateTime(2011, 11, 23, 10, 59, 53, 997);
            this.ReadCTRL.Location = new System.Drawing.Point(2, 25);
            this.ReadCTRL.MaxReturnValues = ((uint)(0u));
            this.ReadCTRL.Name = "ReadCTRL";
            this.ReadCTRL.NodeId = null;
            this.ReadCTRL.ProcessingInterval = 5000;
            this.ReadCTRL.ReadType = Opc.Ua.Client.Controls.HistoryDataListView.HistoryReadType.Raw;
            this.ReadCTRL.ReturnBounds = false;
            this.ReadCTRL.Size = new System.Drawing.Size(895, 402);
            this.ReadCTRL.StartTime = new System.DateTime(((long)(0)));
            this.ReadCTRL.TabIndex = 2;
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.DisableDomainCheck = false;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(2, 2);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.PreferredLocales = null;
            this.ConnectServerCTRL.ServerUrl = "";
            this.ConnectServerCTRL.SessionName = null;
            this.ConnectServerCTRL.Size = new System.Drawing.Size(895, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 3;
            this.ConnectServerCTRL.UserIdentity = null;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectComplete);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectComplete);
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 526);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(899, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // ConsoleTB
            // 
            this.ConsoleTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ConsoleTB.Location = new System.Drawing.Point(2, 2);
            this.ConsoleTB.Name = "ConsoleTB";
            this.ConsoleTB.ReadOnly = true;
            this.ConsoleTB.Size = new System.Drawing.Size(895, 425);
            this.ConsoleTB.TabIndex = 0;
            this.ConsoleTB.Text = "";
            // 
            // clientHeaderBranding1
            // 
            this.clientHeaderBranding1.BackColor = System.Drawing.Color.White;
            this.clientHeaderBranding1.Dock = System.Windows.Forms.DockStyle.Top;
            this.clientHeaderBranding1.Location = new System.Drawing.Point(0, 24);
            this.clientHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 75);
            this.clientHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 75);
            this.clientHeaderBranding1.Name = "clientHeaderBranding1";
            this.clientHeaderBranding1.Padding = new System.Windows.Forms.Padding(3);
            this.clientHeaderBranding1.Size = new System.Drawing.Size(899, 75);
            this.clientHeaderBranding1.TabIndex = 4;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(899, 548);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart HistoricalAccess Client";
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.RichTextBox ConsoleTB;
        private System.Windows.Forms.ToolStripMenuItem AggregatesMI;
        private System.Windows.Forms.ToolStripMenuItem Aggregates_SelectAggregateTypeMI;
        private System.Windows.Forms.ToolStripMenuItem Aggregates_ModifyAggregateFilterMI;
        private System.Windows.Forms.ToolStripMenuItem Aggregates_SelectVariableMI;
        private Opc.Ua.Client.Controls.HistoryDataListView ReadCTRL;
        private System.Windows.Forms.ToolStripMenuItem Aggregates_EnableSubscriptionMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ViewHistoricalConfigurationMI;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem eToolStripMenuItem;
    }
}
