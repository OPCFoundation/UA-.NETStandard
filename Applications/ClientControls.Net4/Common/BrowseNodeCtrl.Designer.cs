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

namespace Opc.Ua.Client.Controls
{
    partial class BrowseNodeCtrl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.BrowseTV = new System.Windows.Forms.TreeView();
            this.BrowseMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Browse_RefreshMI = new System.Windows.Forms.ToolStripMenuItem();
            this.AttributesLV = new System.Windows.Forms.ListView();
            this.AttributeCH = new System.Windows.Forms.ColumnHeader();
            this.DataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.AttributesMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Attributes_ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.BrowseMenu.SuspendLayout();
            this.AttributesMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.BrowseTV);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.AttributesLV);
            this.MainPN.Size = new System.Drawing.Size(1003, 569);
            this.MainPN.SplitterDistance = 387;
            this.MainPN.TabIndex = 11;
            // 
            // BrowseTV
            // 
            this.BrowseTV.ContextMenuStrip = this.BrowseMenu;
            this.BrowseTV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseTV.Location = new System.Drawing.Point(0, 0);
            this.BrowseTV.Name = "BrowseTV";
            this.BrowseTV.Size = new System.Drawing.Size(387, 569);
            this.BrowseTV.TabIndex = 0;
            this.BrowseTV.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.BrowseTV_BeforeExpand);
            this.BrowseTV.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.BrowseTV_AfterSelect);
            this.BrowseTV.MouseDown += new System.Windows.Forms.MouseEventHandler(this.BrowseTV_MouseDown);
            // 
            // BrowseMenu
            // 
            this.BrowseMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Browse_RefreshMI});
            this.BrowseMenu.Name = "BrowseMenu";
            this.BrowseMenu.Size = new System.Drawing.Size(114, 26);
            // 
            // Browse_RefreshMI
            // 
            this.Browse_RefreshMI.Name = "Browse_RefreshMI";
            this.Browse_RefreshMI.Size = new System.Drawing.Size(113, 22);
            this.Browse_RefreshMI.Text = "Refresh";
            this.Browse_RefreshMI.Click += new System.EventHandler(this.Browse_RefreshMI_Click);
            // 
            // AttributesLV
            // 
            this.AttributesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.AttributeCH,
            this.DataTypeCH,
            this.ValueCH});
            this.AttributesLV.ContextMenuStrip = this.AttributesMenu;
            this.AttributesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesLV.FullRowSelect = true;
            this.AttributesLV.Location = new System.Drawing.Point(0, 0);
            this.AttributesLV.Name = "AttributesLV";
            this.AttributesLV.Size = new System.Drawing.Size(612, 569);
            this.AttributesLV.TabIndex = 0;
            this.AttributesLV.UseCompatibleStateImageBehavior = false;
            this.AttributesLV.View = System.Windows.Forms.View.Details;
            this.AttributesLV.DoubleClick += new System.EventHandler(this.AttributesLV_DoubleClick);
            // 
            // AttributeCH
            // 
            this.AttributeCH.Text = "Attribute";
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.Text = "Data Type";
            this.DataTypeCH.Width = 100;
            // 
            // ValueCH
            // 
            this.ValueCH.Text = "Value";
            // 
            // AttributesMenu
            // 
            this.AttributesMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Attributes_ViewMI});
            this.AttributesMenu.Name = "BrowseMenu";
            this.AttributesMenu.Size = new System.Drawing.Size(95, 26);
            // 
            // Attributes_ViewMI
            // 
            this.Attributes_ViewMI.Name = "Attributes_ViewMI";
            this.Attributes_ViewMI.Size = new System.Drawing.Size(94, 22);
            this.Attributes_ViewMI.Text = "Edit";
            this.Attributes_ViewMI.Click += new System.EventHandler(this.AttributesLV_DoubleClick);
            // 
            // BrowseNodeCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.MainPN);
            this.Name = "BrowseNodeCtrl";
            this.Size = new System.Drawing.Size(1003, 569);
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.BrowseMenu.ResumeLayout(false);
            this.AttributesMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer MainPN;
        private System.Windows.Forms.TreeView BrowseTV;
        private System.Windows.Forms.ListView AttributesLV;
        private System.Windows.Forms.ColumnHeader AttributeCH;
        private System.Windows.Forms.ColumnHeader DataTypeCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
        private System.Windows.Forms.ContextMenuStrip BrowseMenu;
        private System.Windows.Forms.ToolStripMenuItem Browse_RefreshMI;
        private System.Windows.Forms.ContextMenuStrip AttributesMenu;
        private System.Windows.Forms.ToolStripMenuItem Attributes_ViewMI;
    }
}
