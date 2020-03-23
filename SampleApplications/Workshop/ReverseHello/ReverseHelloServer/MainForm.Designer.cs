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

namespace ReverseHelloTestServer
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
            this.ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Connect_DotNetTestClientMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.ServerDiagnosticsCTRL = new Opc.Ua.Server.Controls.ServerDiagnosticsCtrl();
            this.Disconnect_DotNetTestClientMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MenuBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ConnectMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(884, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // ConnectMI
            // 
            this.ConnectMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Connect_DotNetTestClientMI,
            this.Disconnect_DotNetTestClientMI});
            this.ConnectMI.Name = "ConnectMI";
            this.ConnectMI.Size = new System.Drawing.Size(64, 20);
            this.ConnectMI.Text = "Connect";
            // 
            // Connect_DotNetTestClientMI
            // 
            this.Connect_DotNetTestClientMI.Name = "Connect_DotNetTestClientMI";
            this.Connect_DotNetTestClientMI.Size = new System.Drawing.Size(248, 22);
            this.Connect_DotNetTestClientMI.Text = "Connect to .NET Test Client";
            this.Connect_DotNetTestClientMI.Click += new System.EventHandler(this.Connect_DotNetTestClient_Click);
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 524);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(884, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // ServerDiagnosticsCTRL
            // 
            this.ServerDiagnosticsCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerDiagnosticsCTRL.Location = new System.Drawing.Point(0, 24);
            this.ServerDiagnosticsCTRL.Name = "ServerDiagnosticsCTRL";
            this.ServerDiagnosticsCTRL.Size = new System.Drawing.Size(884, 500);
            this.ServerDiagnosticsCTRL.TabIndex = 3;
            // 
            // Disconnect_DotNetTestClientMI
            // 
            this.Disconnect_DotNetTestClientMI.Enabled = false;
            this.Disconnect_DotNetTestClientMI.Name = "Disconnect_DotNetTestClientMI";
            this.Disconnect_DotNetTestClientMI.Size = new System.Drawing.Size(248, 22);
            this.Disconnect_DotNetTestClientMI.Text = "Disconnect from .NET Test Client";
            this.Disconnect_DotNetTestClientMI.Click += new System.EventHandler(this.Disconnect_DotNetTestClient_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.ServerDiagnosticsCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "ReverseHello Test Server";
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Connect_DotNetTestClientMI;
        private Opc.Ua.Server.Controls.ServerDiagnosticsCtrl ServerDiagnosticsCTRL;
        private System.Windows.Forms.ToolStripMenuItem Disconnect_DotNetTestClientMI;
    }
}
