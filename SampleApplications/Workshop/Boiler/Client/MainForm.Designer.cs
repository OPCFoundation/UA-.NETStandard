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

namespace Quickstarts.Boiler.Client
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
            this.MainPN = new System.Windows.Forms.Panel();
            this.OutputPipeFlowTB = new System.Windows.Forms.Label();
            this.OutputPipeFlowLB = new System.Windows.Forms.Label();
            this.DrumLevelSetPointTB = new System.Windows.Forms.Label();
            this.DrumLevelSetPointLB = new System.Windows.Forms.Label();
            this.DrumLevelTB = new System.Windows.Forms.Label();
            this.DrumLevelLB = new System.Windows.Forms.Label();
            this.InputPipeFlowTB = new System.Windows.Forms.Label();
            this.InputPipeFlowLB = new System.Windows.Forms.Label();
            this.BoilerCB = new System.Windows.Forms.ComboBox();
            this.BoilerLB = new System.Windows.Forms.Label();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
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
            this.Server_DiscoverMI.Size = new System.Drawing.Size(133, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            // 
            // Server_ConnectMI
            // 
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(133, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            // 
            // Server_DisconnectMI
            // 
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(133, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            // 
            // HelpMI
            // 
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_ContentsMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(44, 20);
            this.HelpMI.Text = "Help";
            // 
            // Help_ContentsMI
            // 
            this.Help_ContentsMI.Name = "Help_ContentsMI";
            this.Help_ContentsMI.Size = new System.Drawing.Size(122, 22);
            this.Help_ContentsMI.Text = "Contents";
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.OutputPipeFlowTB);
            this.MainPN.Controls.Add(this.OutputPipeFlowLB);
            this.MainPN.Controls.Add(this.DrumLevelSetPointTB);
            this.MainPN.Controls.Add(this.DrumLevelSetPointLB);
            this.MainPN.Controls.Add(this.DrumLevelTB);
            this.MainPN.Controls.Add(this.DrumLevelLB);
            this.MainPN.Controls.Add(this.InputPipeFlowTB);
            this.MainPN.Controls.Add(this.InputPipeFlowLB);
            this.MainPN.Controls.Add(this.BoilerCB);
            this.MainPN.Controls.Add(this.BoilerLB);
            this.MainPN.Location = new System.Drawing.Point(0, 48);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 476);
            this.MainPN.TabIndex = 3;
            // 
            // OutputPipeFlowTB
            // 
            this.OutputPipeFlowTB.AutoSize = true;
            this.OutputPipeFlowTB.Location = new System.Drawing.Point(125, 70);
            this.OutputPipeFlowTB.Name = "OutputPipeFlowTB";
            this.OutputPipeFlowTB.Size = new System.Drawing.Size(13, 13);
            this.OutputPipeFlowTB.TabIndex = 10;
            this.OutputPipeFlowTB.Text = "0";
            // 
            // OutputPipeFlowLB
            // 
            this.OutputPipeFlowLB.AutoSize = true;
            this.OutputPipeFlowLB.Location = new System.Drawing.Point(12, 70);
            this.OutputPipeFlowLB.Name = "OutputPipeFlowLB";
            this.OutputPipeFlowLB.Size = new System.Drawing.Size(88, 13);
            this.OutputPipeFlowLB.TabIndex = 9;
            this.OutputPipeFlowLB.Text = "Output Pipe Flow";
            // 
            // DrumLevelSetPointTB
            // 
            this.DrumLevelSetPointTB.AutoSize = true;
            this.DrumLevelSetPointTB.Location = new System.Drawing.Point(125, 110);
            this.DrumLevelSetPointTB.Name = "DrumLevelSetPointTB";
            this.DrumLevelSetPointTB.Size = new System.Drawing.Size(13, 13);
            this.DrumLevelSetPointTB.TabIndex = 8;
            this.DrumLevelSetPointTB.Text = "0";
            // 
            // DrumLevelSetPointLB
            // 
            this.DrumLevelSetPointLB.AutoSize = true;
            this.DrumLevelSetPointLB.Location = new System.Drawing.Point(12, 110);
            this.DrumLevelSetPointLB.Name = "DrumLevelSetPointLB";
            this.DrumLevelSetPointLB.Size = new System.Drawing.Size(107, 13);
            this.DrumLevelSetPointLB.TabIndex = 7;
            this.DrumLevelSetPointLB.Text = "Drum Level Set Point";
            // 
            // DrumLevelTB
            // 
            this.DrumLevelTB.AutoSize = true;
            this.DrumLevelTB.Location = new System.Drawing.Point(125, 90);
            this.DrumLevelTB.Name = "DrumLevelTB";
            this.DrumLevelTB.Size = new System.Drawing.Size(13, 13);
            this.DrumLevelTB.TabIndex = 6;
            this.DrumLevelTB.Text = "0";
            // 
            // DrumLevelLB
            // 
            this.DrumLevelLB.AutoSize = true;
            this.DrumLevelLB.Location = new System.Drawing.Point(12, 90);
            this.DrumLevelLB.Name = "DrumLevelLB";
            this.DrumLevelLB.Size = new System.Drawing.Size(61, 13);
            this.DrumLevelLB.TabIndex = 5;
            this.DrumLevelLB.Text = "Drum Level";
            // 
            // InputPipeFlowTB
            // 
            this.InputPipeFlowTB.AutoSize = true;
            this.InputPipeFlowTB.Location = new System.Drawing.Point(125, 50);
            this.InputPipeFlowTB.Name = "InputPipeFlowTB";
            this.InputPipeFlowTB.Size = new System.Drawing.Size(13, 13);
            this.InputPipeFlowTB.TabIndex = 4;
            this.InputPipeFlowTB.Text = "0";
            // 
            // InputPipeFlowLB
            // 
            this.InputPipeFlowLB.AutoSize = true;
            this.InputPipeFlowLB.Location = new System.Drawing.Point(12, 50);
            this.InputPipeFlowLB.Name = "InputPipeFlowLB";
            this.InputPipeFlowLB.Size = new System.Drawing.Size(80, 13);
            this.InputPipeFlowLB.TabIndex = 3;
            this.InputPipeFlowLB.Text = "Input Pipe Flow";
            // 
            // BoilerCB
            // 
            this.BoilerCB.FormattingEnabled = true;
            this.BoilerCB.Location = new System.Drawing.Point(51, 13);
            this.BoilerCB.Name = "BoilerCB";
            this.BoilerCB.Size = new System.Drawing.Size(164, 21);
            this.BoilerCB.TabIndex = 2;
            this.BoilerCB.SelectedIndexChanged += new System.EventHandler(this.BoilerCB_SelectedIndexChanged);
            // 
            // BoilerLB
            // 
            this.BoilerLB.AutoSize = true;
            this.BoilerLB.Location = new System.Drawing.Point(12, 16);
            this.BoilerLB.Name = "BoilerLB";
            this.BoilerLB.Size = new System.Drawing.Size(33, 13);
            this.BoilerLB.TabIndex = 1;
            this.BoilerLB.Text = "Boiler";
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 24);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.Size = new System.Drawing.Size(884, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 4;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectComplete);
            this.ConnectServerCTRL.ReconnectStarting += new System.EventHandler(this.Server_ReconnectStarting);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectComplete);
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 524);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart Boiler Client";
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
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Label OutputPipeFlowTB;
        private System.Windows.Forms.Label OutputPipeFlowLB;
        private System.Windows.Forms.Label DrumLevelSetPointTB;
        private System.Windows.Forms.Label DrumLevelSetPointLB;
        private System.Windows.Forms.Label DrumLevelTB;
        private System.Windows.Forms.Label DrumLevelLB;
        private System.Windows.Forms.Label InputPipeFlowTB;
        private System.Windows.Forms.Label InputPipeFlowLB;
        private System.Windows.Forms.ComboBox BoilerCB;
        private System.Windows.Forms.Label BoilerLB;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
        private System.Windows.Forms.StatusStrip StatusBar;
    }
}
