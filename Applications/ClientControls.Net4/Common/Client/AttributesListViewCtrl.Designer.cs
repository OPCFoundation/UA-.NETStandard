/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
    partial class AttributesListViewCtrl
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
            this.AttributesMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.Attributes_ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.AttributesLV = new System.Windows.Forms.ListView();
            this.NameCH = new System.Windows.Forms.ColumnHeader();
            this.DataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.ValueCH = new System.Windows.Forms.ColumnHeader();
            this.AttributesMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // AttributesMenu
            // 
            this.AttributesMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Attributes_ViewMI});
            this.AttributesMenu.Name = "BrowseMenu";
            this.AttributesMenu.Size = new System.Drawing.Size(100, 26);
            // 
            // Attributes_ViewMI
            // 
            this.Attributes_ViewMI.Name = "Attributes_ViewMI";
            this.Attributes_ViewMI.Size = new System.Drawing.Size(99, 22);
            this.Attributes_ViewMI.Text = "View";
            this.Attributes_ViewMI.Click += new System.EventHandler(this.AttributesLV_DoubleClick);
            // 
            // AttributesLV
            // 
            this.AttributesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.NameCH,
            this.DataTypeCH,
            this.ValueCH});
            this.AttributesLV.ContextMenuStrip = this.AttributesMenu;
            this.AttributesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AttributesLV.FullRowSelect = true;
            this.AttributesLV.Location = new System.Drawing.Point(0, 0);
            this.AttributesLV.Name = "AttributesLV";
            this.AttributesLV.Size = new System.Drawing.Size(1003, 569);
            this.AttributesLV.TabIndex = 2;
            this.AttributesLV.UseCompatibleStateImageBehavior = false;
            this.AttributesLV.View = System.Windows.Forms.View.Details;
            this.AttributesLV.DoubleClick += new System.EventHandler(this.AttributesLV_DoubleClick);
            // 
            // NameCH
            // 
            this.NameCH.Text = "Name";
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
            // AttributesListViewCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.AttributesLV);
            this.Name = "AttributesListViewCtrl";
            this.Size = new System.Drawing.Size(1003, 569);
            this.AttributesMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip AttributesMenu;
        private System.Windows.Forms.ToolStripMenuItem Attributes_ViewMI;
        private System.Windows.Forms.ListView AttributesLV;
        private System.Windows.Forms.ColumnHeader NameCH;
        private System.Windows.Forms.ColumnHeader DataTypeCH;
        private System.Windows.Forms.ColumnHeader ValueCH;
    }
}
