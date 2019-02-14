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

namespace Quickstarts.MethodsClient
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
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.Panel();
            this.StartBTN = new System.Windows.Forms.Button();
            this.CurrentStateTB = new System.Windows.Forms.TextBox();
            this.CurrentStateLB = new System.Windows.Forms.Label();
            this.RevisedFinalStateTB = new System.Windows.Forms.TextBox();
            this.RevisedInitialStateTB = new System.Windows.Forms.TextBox();
            this.FinalStateTB = new System.Windows.Forms.TextBox();
            this.InitialStateTB = new System.Windows.Forms.TextBox();
            this.RevisedFinalStateLB = new System.Windows.Forms.Label();
            this.RevisedInitialStateLB = new System.Windows.Forms.Label();
            this.FinalStateLB = new System.Windows.Forms.Label();
            this.InitialStateLB = new System.Windows.Forms.Label();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.clientHeaderBranding1 = new Opc.Ua.Client.Controls.HeaderBranding();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(884, 24);
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
            this.Help_ContentsMI.Size = new System.Drawing.Size(152, 22);
            this.Help_ContentsMI.Text = "Contents";
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 524);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.StartBTN);
            this.MainPN.Controls.Add(this.CurrentStateTB);
            this.MainPN.Controls.Add(this.CurrentStateLB);
            this.MainPN.Controls.Add(this.RevisedFinalStateTB);
            this.MainPN.Controls.Add(this.RevisedInitialStateTB);
            this.MainPN.Controls.Add(this.FinalStateTB);
            this.MainPN.Controls.Add(this.InitialStateTB);
            this.MainPN.Controls.Add(this.RevisedFinalStateLB);
            this.MainPN.Controls.Add(this.RevisedInitialStateLB);
            this.MainPN.Controls.Add(this.FinalStateLB);
            this.MainPN.Controls.Add(this.InitialStateLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 122);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 402);
            this.MainPN.TabIndex = 3;
            // 
            // StartBTN
            // 
            this.StartBTN.Location = new System.Drawing.Point(82, 159);
            this.StartBTN.Name = "StartBTN";
            this.StartBTN.Size = new System.Drawing.Size(75, 23);
            this.StartBTN.TabIndex = 11;
            this.StartBTN.Text = "Start";
            this.StartBTN.UseVisualStyleBackColor = true;
            this.StartBTN.Click += new System.EventHandler(this.StartBTN_Click);
            // 
            // CurrentStateTB
            // 
            this.CurrentStateTB.Location = new System.Drawing.Point(118, 114);
            this.CurrentStateTB.Name = "CurrentStateTB";
            this.CurrentStateTB.ReadOnly = true;
            this.CurrentStateTB.Size = new System.Drawing.Size(100, 20);
            this.CurrentStateTB.TabIndex = 10;
            // 
            // CurrentStateLB
            // 
            this.CurrentStateLB.AutoSize = true;
            this.CurrentStateLB.Location = new System.Drawing.Point(11, 117);
            this.CurrentStateLB.Name = "CurrentStateLB";
            this.CurrentStateLB.Size = new System.Drawing.Size(69, 13);
            this.CurrentStateLB.TabIndex = 9;
            this.CurrentStateLB.Text = "Current State";
            // 
            // RevisedFinalStateTB
            // 
            this.RevisedFinalStateTB.Location = new System.Drawing.Point(118, 88);
            this.RevisedFinalStateTB.Name = "RevisedFinalStateTB";
            this.RevisedFinalStateTB.ReadOnly = true;
            this.RevisedFinalStateTB.Size = new System.Drawing.Size(100, 20);
            this.RevisedFinalStateTB.TabIndex = 8;
            // 
            // RevisedInitialStateTB
            // 
            this.RevisedInitialStateTB.Location = new System.Drawing.Point(118, 62);
            this.RevisedInitialStateTB.Name = "RevisedInitialStateTB";
            this.RevisedInitialStateTB.ReadOnly = true;
            this.RevisedInitialStateTB.Size = new System.Drawing.Size(100, 20);
            this.RevisedInitialStateTB.TabIndex = 7;
            // 
            // FinalStateTB
            // 
            this.FinalStateTB.Location = new System.Drawing.Point(118, 36);
            this.FinalStateTB.Name = "FinalStateTB";
            this.FinalStateTB.Size = new System.Drawing.Size(100, 20);
            this.FinalStateTB.TabIndex = 6;
            // 
            // InitialStateTB
            // 
            this.InitialStateTB.Location = new System.Drawing.Point(118, 10);
            this.InitialStateTB.Name = "InitialStateTB";
            this.InitialStateTB.Size = new System.Drawing.Size(100, 20);
            this.InitialStateTB.TabIndex = 5;
            // 
            // RevisedFinalStateLB
            // 
            this.RevisedFinalStateLB.AutoSize = true;
            this.RevisedFinalStateLB.Location = new System.Drawing.Point(11, 91);
            this.RevisedFinalStateLB.Name = "RevisedFinalStateLB";
            this.RevisedFinalStateLB.Size = new System.Drawing.Size(99, 13);
            this.RevisedFinalStateLB.TabIndex = 4;
            this.RevisedFinalStateLB.Text = "Revised Final State";
            // 
            // RevisedInitialStateLB
            // 
            this.RevisedInitialStateLB.AutoSize = true;
            this.RevisedInitialStateLB.Location = new System.Drawing.Point(11, 65);
            this.RevisedInitialStateLB.Name = "RevisedInitialStateLB";
            this.RevisedInitialStateLB.Size = new System.Drawing.Size(101, 13);
            this.RevisedInitialStateLB.TabIndex = 3;
            this.RevisedInitialStateLB.Text = "Revised Initial State";
            // 
            // FinalStateLB
            // 
            this.FinalStateLB.AutoSize = true;
            this.FinalStateLB.Location = new System.Drawing.Point(11, 39);
            this.FinalStateLB.Name = "FinalStateLB";
            this.FinalStateLB.Size = new System.Drawing.Size(57, 13);
            this.FinalStateLB.TabIndex = 2;
            this.FinalStateLB.Text = "Final State";
            // 
            // InitialStateLB
            // 
            this.InitialStateLB.AutoSize = true;
            this.InitialStateLB.Location = new System.Drawing.Point(11, 13);
            this.InitialStateLB.Name = "InitialStateLB";
            this.InitialStateLB.Size = new System.Drawing.Size(59, 13);
            this.InitialStateLB.TabIndex = 1;
            this.InitialStateLB.Text = "Initial State";
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
            this.ConnectServerCTRL.Size = new System.Drawing.Size(884, 23);
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
            this.clientHeaderBranding1.Size = new System.Drawing.Size(884, 75);
            this.clientHeaderBranding1.TabIndex = 5;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.clientHeaderBranding1);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart Methods Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
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
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Button StartBTN;
        private System.Windows.Forms.TextBox CurrentStateTB;
        private System.Windows.Forms.Label CurrentStateLB;
        private System.Windows.Forms.TextBox RevisedFinalStateTB;
        private System.Windows.Forms.TextBox RevisedInitialStateTB;
        private System.Windows.Forms.TextBox FinalStateTB;
        private System.Windows.Forms.TextBox InitialStateTB;
        private System.Windows.Forms.Label RevisedFinalStateLB;
        private System.Windows.Forms.Label RevisedInitialStateLB;
        private System.Windows.Forms.Label FinalStateLB;
        private System.Windows.Forms.Label InitialStateLB;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private Opc.Ua.Client.Controls.HeaderBranding clientHeaderBranding1;
    }
}
