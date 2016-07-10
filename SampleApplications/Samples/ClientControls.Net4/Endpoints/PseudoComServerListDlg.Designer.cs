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

namespace Opc.Ua.Client.Controls
{
    partial class PseudoComServerListDlg
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
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ServersCTRL = new Opc.Ua.Client.Controls.PseudoComServerListCtrl();
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.FileMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_ExportMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_ImportMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_Seperator1 = new System.Windows.Forms.ToolStripSeparator();
            this.File_ExitMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.View_RefreshMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_AboutMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.MenuBar.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 235);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(816, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(737, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ServersCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 24);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2);
            this.MainPN.Size = new System.Drawing.Size(816, 211);
            this.MainPN.TabIndex = 1;
            // 
            // ServersCTRL
            // 
            this.ServersCTRL.Cursor = System.Windows.Forms.Cursors.Default;
            this.ServersCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersCTRL.Instructions = null;
            this.ServersCTRL.Location = new System.Drawing.Point(2, 2);
            this.ServersCTRL.Name = "ServersCTRL";
            this.ServersCTRL.Size = new System.Drawing.Size(812, 207);
            this.ServersCTRL.TabIndex = 0;
            this.ServersCTRL.ItemsSelected += new Opc.Ua.Client.Controls.ListItemActionEventHandler(this.ServersCTRL_ItemsSelected);
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMI,
            this.ViewMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(816, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // FileMI
            // 
            this.FileMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.File_ExportMI,
            this.File_ImportMI,
            this.File_Seperator1,
            this.File_ExitMI});
            this.FileMI.Name = "FileMI";
            this.FileMI.Size = new System.Drawing.Size(37, 20);
            this.FileMI.Text = "File";
            // 
            // File_ExportMI
            // 
            this.File_ExportMI.Name = "File_ExportMI";
            this.File_ExportMI.Size = new System.Drawing.Size(119, 22);
            this.File_ExportMI.Text = "Export...";
            this.File_ExportMI.Click += new System.EventHandler(this.File_ExportMI_Click);
            // 
            // File_ImportMI
            // 
            this.File_ImportMI.Name = "File_ImportMI";
            this.File_ImportMI.Size = new System.Drawing.Size(119, 22);
            this.File_ImportMI.Text = "Import...";
            this.File_ImportMI.Click += new System.EventHandler(this.File_ImportMI_Click);
            // 
            // File_Seperator1
            // 
            this.File_Seperator1.Name = "File_Seperator1";
            this.File_Seperator1.Size = new System.Drawing.Size(116, 6);
            // 
            // File_ExitMI
            // 
            this.File_ExitMI.Name = "File_ExitMI";
            this.File_ExitMI.Size = new System.Drawing.Size(119, 22);
            this.File_ExitMI.Text = "Exit";
            this.File_ExitMI.Click += new System.EventHandler(this.File_ExitMI_Click);
            // 
            // ViewMI
            // 
            this.ViewMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.View_RefreshMI});
            this.ViewMI.Name = "ViewMI";
            this.ViewMI.Size = new System.Drawing.Size(44, 20);
            this.ViewMI.Text = "View";
            // 
            // View_RefreshMI
            // 
            this.View_RefreshMI.Name = "View_RefreshMI";
            this.View_RefreshMI.Size = new System.Drawing.Size(113, 22);
            this.View_RefreshMI.Text = "Refresh";
            this.View_RefreshMI.Click += new System.EventHandler(this.View_RefreshMI_Click);
            // 
            // HelpMI
            // 
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_AboutMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(44, 20);
            this.HelpMI.Text = "Help";
            // 
            // Help_AboutMI
            // 
            this.Help_AboutMI.Name = "Help_AboutMI";
            this.Help_AboutMI.Size = new System.Drawing.Size(116, 22);
            this.Help_AboutMI.Text = "About...";
            this.Help_AboutMI.Click += new System.EventHandler(this.Help_AboutMI_Click);
            // 
            // PseudoComServerListDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(816, 266);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.MenuBar);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MainMenuStrip = this.MenuBar;
            this.MaximizeBox = false;
            this.Name = "PseudoComServerListDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configure COM Pseudo-Servers";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private PseudoComServerListCtrl ServersCTRL;
        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.ToolStripMenuItem FileMI;
        private System.Windows.Forms.ToolStripMenuItem File_ExportMI;
        private System.Windows.Forms.ToolStripMenuItem File_ImportMI;
        private System.Windows.Forms.ToolStripSeparator File_Seperator1;
        private System.Windows.Forms.ToolStripMenuItem File_ExitMI;
        private System.Windows.Forms.ToolStripMenuItem ViewMI;
        private System.Windows.Forms.ToolStripMenuItem View_RefreshMI;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_AboutMI;
    }
}
