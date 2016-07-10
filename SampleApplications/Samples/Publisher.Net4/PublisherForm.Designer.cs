/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
    partial class PublisherForm
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
            this.MainMenu = new System.Windows.Forms.MenuStrip();
            this.FileMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FileLoadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FileSaveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FileSaveAsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
            this.FileExit = new System.Windows.Forms.ToolStripMenuItem();
            this.TaskMI = new System.Windows.Forms.ToolStripMenuItem();
            this.NewWindowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PerformanceTestMI = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.Task_TestMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DiscoveyrMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DiscoverServersMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Discovery_RegisterMI = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.contentsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusStrip = new System.Windows.Forms.StatusStrip();
            this.ServerUrlLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerStatusLB = new System.Windows.Forms.ToolStripStatusLabel();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.SessionsPanel = new System.Windows.Forms.SplitContainer();
            this.SessionsCTRL = new Opc.Ua.Sample.Controls.SessionTreeCtrl();
            this.BrowseCTRL = new Opc.Ua.Sample.Controls.BrowseTreeCtrl();
            this.NotificationsCTRL = new Opc.Ua.Sample.Controls.NotificationMessageListCtrl();
            this.EndpointSelectorCTRL = new Opc.Ua.Client.Controls.EndpointSelectorCtrl();
            this.serverHeaderBranding1 = new Opc.Ua.Server.Controls.ServerHeaderBranding();
            this.MainMenu.SuspendLayout();
            this.StatusStrip.SuspendLayout();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SessionsPanel.Panel1.SuspendLayout();
            this.SessionsPanel.Panel2.SuspendLayout();
            this.SessionsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainMenu
            // 
            this.MainMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMI,
            this.TaskMI,
            this.DiscoveyrMI,
            this.helpToolStripMenuItem});
            this.MainMenu.Location = new System.Drawing.Point(0, 0);
            this.MainMenu.Name = "MainMenu";
            this.MainMenu.Size = new System.Drawing.Size(553, 24);
            this.MainMenu.TabIndex = 1;
            this.MainMenu.Text = "MainMenu";
            // 
            // FileMI
            // 
            this.FileMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileLoadMI,
            this.FileSaveMI,
            this.FileSaveAsMI,
            this.toolStripMenuItem1,
            this.FileExit});
            this.FileMI.Name = "FileMI";
            this.FileMI.Size = new System.Drawing.Size(35, 20);
            this.FileMI.Text = "File";
            // 
            // FileLoadMI
            // 
            this.FileLoadMI.Name = "FileLoadMI";
            this.FileLoadMI.Size = new System.Drawing.Size(125, 22);
            this.FileLoadMI.Text = "Load...";
            // 
            // FileSaveMI
            // 
            this.FileSaveMI.Name = "FileSaveMI";
            this.FileSaveMI.Size = new System.Drawing.Size(125, 22);
            this.FileSaveMI.Text = "Save";
            // 
            // FileSaveAsMI
            // 
            this.FileSaveAsMI.Name = "FileSaveAsMI";
            this.FileSaveAsMI.Size = new System.Drawing.Size(125, 22);
            this.FileSaveAsMI.Text = "Save As...";
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(122, 6);
            // 
            // FileExit
            // 
            this.FileExit.Name = "FileExit";
            this.FileExit.Size = new System.Drawing.Size(125, 22);
            this.FileExit.Text = "Exit";
            this.FileExit.Click += new System.EventHandler(this.FileExit_Click);
            // 
            // TaskMI
            // 
            this.TaskMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewWindowMI,
            this.PerformanceTestMI,
            this.toolStripSeparator1,
            this.Task_TestMI});
            this.TaskMI.Name = "TaskMI";
            this.TaskMI.Size = new System.Drawing.Size(41, 20);
            this.TaskMI.Text = "Task";
            // 
            // NewWindowMI
            // 
            this.NewWindowMI.Name = "NewWindowMI";
            this.NewWindowMI.Size = new System.Drawing.Size(148, 22);
            this.NewWindowMI.Text = "New Window...";
            this.NewWindowMI.Click += new System.EventHandler(this.NewWindowMI_Click);
            // 
            // PerformanceTestMI
            // 
            this.PerformanceTestMI.Name = "PerformanceTestMI";
            this.PerformanceTestMI.Size = new System.Drawing.Size(148, 22);
            this.PerformanceTestMI.Text = "Stack Test...";
            this.PerformanceTestMI.Click += new System.EventHandler(this.PerformanceTestMI_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(145, 6);
            // 
            // Task_TestMI
            // 
            this.Task_TestMI.Name = "Task_TestMI";
            this.Task_TestMI.Size = new System.Drawing.Size(148, 22);
            this.Task_TestMI.Text = "Test 1";
            this.Task_TestMI.Click += new System.EventHandler(this.Task_TestMI_Click);
            // 
            // DiscoveyrMI
            // 
            this.DiscoveyrMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DiscoverServersMI,
            this.Discovery_RegisterMI});
            this.DiscoveyrMI.Name = "DiscoveyrMI";
            this.DiscoveyrMI.Size = new System.Drawing.Size(66, 20);
            this.DiscoveyrMI.Text = "Discovery";
            // 
            // DiscoverServersMI
            // 
            this.DiscoverServersMI.Name = "DiscoverServersMI";
            this.DiscoverServersMI.Size = new System.Drawing.Size(138, 22);
            this.DiscoverServersMI.Text = "Servers...";
            this.DiscoverServersMI.Click += new System.EventHandler(this.DiscoverServersMI_Click);
            // 
            // Discovery_RegisterMI
            // 
            this.Discovery_RegisterMI.Name = "Discovery_RegisterMI";
            this.Discovery_RegisterMI.Size = new System.Drawing.Size(138, 22);
            this.Discovery_RegisterMI.Text = "Register Now";
            this.Discovery_RegisterMI.Click += new System.EventHandler(this.Discovery_RegisterMI_Click);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.contentsToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(40, 20);
            this.helpToolStripMenuItem.Text = "&Help";
            // 
            // contentsToolStripMenuItem
            // 
            this.contentsToolStripMenuItem.Name = "contentsToolStripMenuItem";
            this.contentsToolStripMenuItem.Size = new System.Drawing.Size(118, 22);
            this.contentsToolStripMenuItem.Text = "&Contents";
            this.contentsToolStripMenuItem.Click += new System.EventHandler(this.contentsToolStripMenuItem_Click);
            // 
            // StatusStrip
            // 
            this.StatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerUrlLB,
            this.ServerStatusLB});
            this.StatusStrip.Location = new System.Drawing.Point(0, 459);
            this.StatusStrip.Name = "StatusStrip";
            this.StatusStrip.Size = new System.Drawing.Size(553, 22);
            this.StatusStrip.TabIndex = 6;
            this.StatusStrip.Text = "statusStrip1";
            // 
            // ServerUrlLB
            // 
            this.ServerUrlLB.Name = "ServerUrlLB";
            this.ServerUrlLB.Size = new System.Drawing.Size(71, 17);
            this.ServerUrlLB.Text = "Disconnected";
            // 
            // ServerStatusLB
            // 
            this.ServerStatusLB.Name = "ServerStatusLB";
            this.ServerStatusLB.Size = new System.Drawing.Size(51, 17);
            this.ServerStatusLB.Text = "00:00:00";
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 141);
            this.MainPN.Name = "MainPN";
            this.MainPN.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.SessionsPanel);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.NotificationsCTRL);
            this.MainPN.Size = new System.Drawing.Size(553, 318);
            this.MainPN.SplitterDistance = 207;
            this.MainPN.TabIndex = 8;
            // 
            // SessionsPanel
            // 
            this.SessionsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SessionsPanel.Location = new System.Drawing.Point(0, 0);
            this.SessionsPanel.Name = "SessionsPanel";
            // 
            // SessionsPanel.Panel1
            // 
            this.SessionsPanel.Panel1.Controls.Add(this.SessionsCTRL);
            // 
            // SessionsPanel.Panel2
            // 
            this.SessionsPanel.Panel2.Controls.Add(this.BrowseCTRL);
            this.SessionsPanel.Size = new System.Drawing.Size(553, 207);
            this.SessionsPanel.SplitterDistance = 162;
            this.SessionsPanel.TabIndex = 5;
            // 
            // SessionsCTRL
            // 
            this.SessionsCTRL.AddressSpaceCtrl = this.BrowseCTRL;
            this.SessionsCTRL.Configuration = null;
            this.SessionsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SessionsCTRL.EnableDragging = false;
            this.SessionsCTRL.Location = new System.Drawing.Point(0, 0);
            this.SessionsCTRL.MessageContext = null;
            this.SessionsCTRL.Name = "SessionsCTRL";
            this.SessionsCTRL.NotificationMessagesCtrl = this.NotificationsCTRL;
            this.SessionsCTRL.PreferredLocales = null;
            this.SessionsCTRL.ServerStatusCtrl = null;
            this.SessionsCTRL.Size = new System.Drawing.Size(162, 207);
            this.SessionsCTRL.TabIndex = 0;
            // 
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesCtrl = null;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.EnableDragging = false;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.SessionTreeCtrl = this.SessionsCTRL;
            this.BrowseCTRL.Size = new System.Drawing.Size(387, 207);
            this.BrowseCTRL.TabIndex = 0;
            // 
            // NotificationsCTRL
            // 
            this.NotificationsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.NotificationsCTRL.Instructions = "Create a subscription to see notifications";
            this.NotificationsCTRL.Location = new System.Drawing.Point(0, 0);
            this.NotificationsCTRL.MaxMessageCount = 10;
            this.NotificationsCTRL.Name = "NotificationsCTRL";
            this.NotificationsCTRL.Size = new System.Drawing.Size(553, 107);
            this.NotificationsCTRL.TabIndex = 0;
            // 
            // EndpointSelectorCTRL
            // 
            this.EndpointSelectorCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.EndpointSelectorCTRL.Location = new System.Drawing.Point(0, 114);
            this.EndpointSelectorCTRL.MaximumSize = new System.Drawing.Size(2048, 27);
            this.EndpointSelectorCTRL.MinimumSize = new System.Drawing.Size(100, 27);
            this.EndpointSelectorCTRL.Name = "EndpointSelectorCTRL";
            this.EndpointSelectorCTRL.Padding = new System.Windows.Forms.Padding(1, 0, 0, 0);
            this.EndpointSelectorCTRL.SelectedEndpoint = null;
            this.EndpointSelectorCTRL.Size = new System.Drawing.Size(553, 27);
            this.EndpointSelectorCTRL.TabIndex = 2;
            this.EndpointSelectorCTRL.EndpointsChanged += new System.EventHandler(this.EndpointSelectorCTRL_OnChange);
            this.EndpointSelectorCTRL.ConnectEndpoint += new Opc.Ua.Client.Controls.ConnectEndpointEventHandler(this.EndpointSelectorCTRL_ConnectEndpoint);
            // 
            // serverHeaderBranding1
            // 
            this.serverHeaderBranding1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.serverHeaderBranding1.BackColor = System.Drawing.Color.White;
            this.serverHeaderBranding1.Dock = System.Windows.Forms.DockStyle.Top;
            this.serverHeaderBranding1.Location = new System.Drawing.Point(0, 24);
            this.serverHeaderBranding1.MaximumSize = new System.Drawing.Size(0, 100);
            this.serverHeaderBranding1.MinimumSize = new System.Drawing.Size(500, 90);
            this.serverHeaderBranding1.Name = "serverHeaderBranding1";
            this.serverHeaderBranding1.Padding = new System.Windows.Forms.Padding(3);
            this.serverHeaderBranding1.Size = new System.Drawing.Size(553, 90);
            this.serverHeaderBranding1.TabIndex = 9;
            // 
            // PublisherForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(553, 481);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusStrip);
            this.Controls.Add(this.EndpointSelectorCTRL);
            this.Controls.Add(this.serverHeaderBranding1);
            this.Controls.Add(this.MainMenu);
            this.MainMenuStrip = this.MainMenu;
            this.Name = "PublisherForm";
            this.Text = "UA Publisher";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MainMenu.ResumeLayout(false);
            this.MainMenu.PerformLayout();
            this.StatusStrip.ResumeLayout(false);
            this.StatusStrip.PerformLayout();
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.SessionsPanel.Panel1.ResumeLayout(false);
            this.SessionsPanel.Panel2.ResumeLayout(false);
            this.SessionsPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MainMenu;
        private System.Windows.Forms.ToolStripMenuItem TaskMI;
        private Opc.Ua.Client.Controls.EndpointSelectorCtrl EndpointSelectorCTRL;
        private System.Windows.Forms.SplitContainer SessionsPanel;
        private System.Windows.Forms.StatusStrip StatusStrip;
        private System.Windows.Forms.ToolStripStatusLabel ServerUrlLB;
        private System.Windows.Forms.SplitContainer MainPN;
        private Opc.Ua.Sample.Controls.NotificationMessageListCtrl NotificationsCTRL;
        private System.Windows.Forms.ToolStripMenuItem PerformanceTestMI;
        private System.Windows.Forms.ToolStripMenuItem FileMI;
        private System.Windows.Forms.ToolStripMenuItem FileLoadMI;
        private System.Windows.Forms.ToolStripMenuItem FileSaveMI;
        private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem FileExit;
        private System.Windows.Forms.ToolStripMenuItem FileSaveAsMI;
        private System.Windows.Forms.ToolStripStatusLabel ServerStatusLB;
        private System.Windows.Forms.ToolStripMenuItem DiscoveyrMI;
        private System.Windows.Forms.ToolStripMenuItem DiscoverServersMI;
        private System.Windows.Forms.ToolStripMenuItem NewWindowMI;
        protected SessionTreeCtrl SessionsCTRL;
        protected BrowseTreeCtrl BrowseCTRL;
        private System.Windows.Forms.ToolStripMenuItem Discovery_RegisterMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem Task_TestMI;
        private Opc.Ua.Server.Controls.ServerHeaderBranding serverHeaderBranding1;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem contentsToolStripMenuItem;
    }
}
