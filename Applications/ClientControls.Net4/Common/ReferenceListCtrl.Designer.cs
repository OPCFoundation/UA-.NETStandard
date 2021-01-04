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
    partial class ReferenceListCtrl
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
            this.ReferencesDV = new System.Windows.Forms.DataGridView();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.ImageCH = new System.Windows.Forms.DataGridViewImageColumn();
            this.TargetNameCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ReferenceTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IsForwardCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.NodeClassCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.TargetTypeCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.ReferencesDV)).BeginInit();
            this.SuspendLayout();
            // 
            // ReferencesDV
            // 
            this.ReferencesDV.AllowUserToAddRows = false;
            this.ReferencesDV.AllowUserToDeleteRows = false;
            this.ReferencesDV.AllowUserToOrderColumns = true;
            this.ReferencesDV.AllowUserToResizeRows = false;
            this.ReferencesDV.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.ReferencesDV.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ReferencesDV.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.None;
            this.ReferencesDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ReferencesDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ImageCH,
            this.TargetNameCH,
            this.ReferenceTypeCH,
            this.IsForwardCH,
            this.NodeClassCH,
            this.TargetTypeCH});
            this.ReferencesDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReferencesDV.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ReferencesDV.Location = new System.Drawing.Point(0, 0);
            this.ReferencesDV.Margin = new System.Windows.Forms.Padding(0);
            this.ReferencesDV.Name = "ReferencesDV";
            this.ReferencesDV.ReadOnly = true;
            this.ReferencesDV.RowHeadersVisible = false;
            this.ReferencesDV.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.ReferencesDV.RowTemplate.Height = 20;
            this.ReferencesDV.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ReferencesDV.Size = new System.Drawing.Size(890, 430);
            this.ReferencesDV.TabIndex = 1;
            // 
            // ImageList
            // 
            this.ImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth24Bit;
            this.ImageList.ImageSize = new System.Drawing.Size(16, 16);
            this.ImageList.TransparentColor = System.Drawing.Color.White;
            // 
            // ImageCH
            // 
            this.ImageCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.ImageCH.DataPropertyName = "Image";
            this.ImageCH.HeaderText = "";
            this.ImageCH.Name = "ImageCH";
            this.ImageCH.ReadOnly = true;
            this.ImageCH.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.ImageCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Automatic;
            this.ImageCH.Width = 19;
            // 
            // TargetNameCH
            // 
            this.TargetNameCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.TargetNameCH.DataPropertyName = "TargetName";
            this.TargetNameCH.HeaderText = "Target Name";
            this.TargetNameCH.MinimumWidth = 20;
            this.TargetNameCH.Name = "TargetNameCH";
            this.TargetNameCH.ReadOnly = true;
            this.TargetNameCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.TargetNameCH.Width = 94;
            // 
            // ReferenceTypeCH
            // 
            this.ReferenceTypeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.ReferenceTypeCH.DataPropertyName = "ReferenceType";
            this.ReferenceTypeCH.HeaderText = "Reference Type";
            this.ReferenceTypeCH.MinimumWidth = 20;
            this.ReferenceTypeCH.Name = "ReferenceTypeCH";
            this.ReferenceTypeCH.ReadOnly = true;
            this.ReferenceTypeCH.Width = 109;
            // 
            // IsForwardCH
            // 
            this.IsForwardCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.IsForwardCH.DataPropertyName = "IsForward";
            this.IsForwardCH.HeaderText = "Is Forward";
            this.IsForwardCH.MinimumWidth = 20;
            this.IsForwardCH.Name = "IsForwardCH";
            this.IsForwardCH.ReadOnly = true;
            this.IsForwardCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.IsForwardCH.Width = 81;
            // 
            // NodeClassCH
            // 
            this.NodeClassCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.NodeClassCH.DataPropertyName = "NodeClass";
            this.NodeClassCH.HeaderText = "NodeClass";
            this.NodeClassCH.MinimumWidth = 20;
            this.NodeClassCH.Name = "NodeClassCH";
            this.NodeClassCH.ReadOnly = true;
            this.NodeClassCH.Width = 83;
            // 
            // TargetTypeCH
            // 
            this.TargetTypeCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.TargetTypeCH.DataPropertyName = "TargetType";
            this.TargetTypeCH.HeaderText = "Target Type";
            this.TargetTypeCH.MinimumWidth = 20;
            this.TargetTypeCH.Name = "TargetTypeCH";
            this.TargetTypeCH.ReadOnly = true;
            this.TargetTypeCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            // 
            // ReferenceListCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ReferencesDV);
            this.Name = "ReferenceListCtrl";
            this.Size = new System.Drawing.Size(890, 430);
            ((System.ComponentModel.ISupportInitialize)(this.ReferencesDV)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView ReferencesDV;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.DataGridViewImageColumn ImageCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn TargetNameCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ReferenceTypeCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn IsForwardCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn NodeClassCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn TargetTypeCH;

    }
}
