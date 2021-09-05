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
    partial class ReadRequestListViewCtrl
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
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ResultsDV = new System.Windows.Forms.DataGridView();
            this.Icon = new System.Windows.Forms.DataGridViewImageColumn();
            this.NodeNameCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.AttributeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IndexRangeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DataEncodingCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DataTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.StatusCodeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SourceTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerTimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RightPN = new System.Windows.Forms.Panel();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.NewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).BeginInit();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewMI,
            this.EditMI,
            this.DeleteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(153, 92);
            this.PopupMenu.Opening += new System.ComponentModel.CancelEventHandler(this.PopupMenu_Opening);
            // 
            // EditMI
            // 
            this.EditMI.Name = "EditMI";
            this.EditMI.Size = new System.Drawing.Size(152, 22);
            this.EditMI.Text = "Edit...";
            this.EditMI.Click += new System.EventHandler(this.EditMI_Click);
            // 
            // ResultsDV
            // 
            this.ResultsDV.AllowUserToAddRows = false;
            this.ResultsDV.AllowUserToDeleteRows = false;
            this.ResultsDV.AllowUserToResizeRows = false;
            this.ResultsDV.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ResultsDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.ResultsDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Icon,
            this.NodeNameCH,
            this.AttributeCH,
            this.IndexRangeCH,
            this.DataEncodingCH,
            this.DataTypeCH,
            this.ValueCH,
            this.StatusCodeCH,
            this.SourceTimestampCH,
            this.ServerTimestampCH});
            this.ResultsDV.ContextMenuStrip = this.PopupMenu;
            this.ResultsDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ResultsDV.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ResultsDV.Location = new System.Drawing.Point(0, 0);
            this.ResultsDV.Name = "ResultsDV";
            this.ResultsDV.RowHeadersVisible = false;
            this.ResultsDV.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ResultsDV.Size = new System.Drawing.Size(754, 346);
            this.ResultsDV.TabIndex = 0;
            this.ResultsDV.DoubleClick += new System.EventHandler(this.EditMI_Click);
            // 
            // Icon
            // 
            this.Icon.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.Icon.DataPropertyName = "Icon";
            this.Icon.HeaderText = "";
            this.Icon.Name = "Icon";
            this.Icon.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.Icon.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.Icon.Width = 19;
            // 
            // NodeNameCH
            // 
            this.NodeNameCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.NodeNameCH.DataPropertyName = "NodeName";
            this.NodeNameCH.HeaderText = "Node";
            this.NodeNameCH.Name = "NodeNameCH";
            this.NodeNameCH.ReadOnly = true;
            this.NodeNameCH.Width = 58;
            // 
            // AttributeCH
            // 
            this.AttributeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.AttributeCH.DataPropertyName = "Attribute";
            this.AttributeCH.HeaderText = "Attribute";
            this.AttributeCH.Name = "AttributeCH";
            this.AttributeCH.ReadOnly = true;
            this.AttributeCH.Width = 71;
            // 
            // IndexRangeCH
            // 
            this.IndexRangeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.IndexRangeCH.DataPropertyName = "IndexRange";
            this.IndexRangeCH.HeaderText = "Index Range";
            this.IndexRangeCH.Name = "IndexRangeCH";
            this.IndexRangeCH.ReadOnly = true;
            this.IndexRangeCH.Width = 93;
            // 
            // DataEncodingCH
            // 
            this.DataEncodingCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.DataEncodingCH.DataPropertyName = "DataEncoding";
            this.DataEncodingCH.HeaderText = "Data Encoding";
            this.DataEncodingCH.Name = "DataEncodingCH";
            this.DataEncodingCH.ReadOnly = true;
            this.DataEncodingCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DataTypeCH.DataPropertyName = "DataType";
            this.DataTypeCH.HeaderText = "Data Type";
            this.DataTypeCH.Name = "DataTypeCH";
            this.DataTypeCH.ReadOnly = true;
            this.DataTypeCH.Width = 82;
            // 
            // ValueCH
            // 
            this.ValueCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ValueCH.DataPropertyName = "Value";
            this.ValueCH.HeaderText = "Value";
            this.ValueCH.Name = "ValueCH";
            this.ValueCH.ReadOnly = true;
            this.ValueCH.Visible = false;
            // 
            // StatusCodeCH
            // 
            this.StatusCodeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.StatusCodeCH.DataPropertyName = "StatusCode";
            this.StatusCodeCH.HeaderText = "StatusCode";
            this.StatusCodeCH.Name = "StatusCodeCH";
            this.StatusCodeCH.ReadOnly = true;
            this.StatusCodeCH.Visible = false;
            // 
            // SourceTimestampCH
            // 
            this.SourceTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.SourceTimestampCH.DataPropertyName = "SourceTimestamp";
            this.SourceTimestampCH.HeaderText = "SourceTimestamp";
            this.SourceTimestampCH.Name = "SourceTimestampCH";
            this.SourceTimestampCH.ReadOnly = true;
            this.SourceTimestampCH.Visible = false;
            // 
            // ServerTimestampCH
            // 
            this.ServerTimestampCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.ServerTimestampCH.DataPropertyName = "ServerTimestamp";
            this.ServerTimestampCH.HeaderText = "ServerTimestamp";
            this.ServerTimestampCH.Name = "ServerTimestampCH";
            this.ServerTimestampCH.ReadOnly = true;
            this.ServerTimestampCH.Visible = false;
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.ResultsDV);
            this.RightPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RightPN.Location = new System.Drawing.Point(0, 0);
            this.RightPN.Name = "RightPN";
            this.RightPN.Size = new System.Drawing.Size(754, 346);
            this.RightPN.TabIndex = 3;
            // 
            // ImageList
            // 
            this.ImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
            this.ImageList.ImageSize = new System.Drawing.Size(16, 16);
            this.ImageList.TransparentColor = System.Drawing.Color.White;
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(152, 22);
            this.DeleteMI.Text = "Delete";
            this.DeleteMI.Click += new System.EventHandler(this.DeleteMI_Click);
            // 
            // NewMI
            // 
            this.NewMI.Name = "NewMI";
            this.NewMI.Size = new System.Drawing.Size(152, 22);
            this.NewMI.Text = "New...";
            this.NewMI.Click += new System.EventHandler(this.NewMI_Click);
            // 
            // ReadRequestListView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Name = "ReadRequestListView";
            this.Size = new System.Drawing.Size(754, 346);
            this.PopupMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ResultsDV)).EndInit();
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.DataGridView ResultsDV;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.ToolStripMenuItem EditMI;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.DataGridViewImageColumn Icon;
        private System.Windows.Forms.DataGridViewTextBoxColumn NodeNameCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn AttributeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn IndexRangeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DataEncodingCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn DataTypeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusCodeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn SourceTimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerTimestampCH;
        private System.Windows.Forms.ToolStripMenuItem NewMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
    }
}
