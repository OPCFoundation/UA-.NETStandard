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
    partial class EventFilterListViewCtrl
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
            this.MoveUpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MoveDownMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FilterDV = new System.Windows.Forms.DataGridView();
            this.RightPN = new System.Windows.Forms.Panel();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.BrowseCTRL = new Opc.Ua.Client.Controls.BrowseNodeCtrl();
            this.Icon = new System.Windows.Forms.DataGridViewImageColumn();
            this.BrowsePathCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SelectFieldCH = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.DisplayInListCH = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.FilterEnabledCH = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.FilterOperatorCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.FilterValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PopupMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.FilterDV)).BeginInit();
            this.RightPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.MoveUpMI,
            this.MoveDownMI,
            this.DeleteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(139, 70);
            // 
            // MoveUpMI
            // 
            this.MoveUpMI.Name = "MoveUpMI";
            this.MoveUpMI.Size = new System.Drawing.Size(138, 22);
            this.MoveUpMI.Text = "Move Up";
            this.MoveUpMI.Click += new System.EventHandler(this.MoveUpMI_Click);
            // 
            // MoveDownMI
            // 
            this.MoveDownMI.Name = "MoveDownMI";
            this.MoveDownMI.Size = new System.Drawing.Size(138, 22);
            this.MoveDownMI.Text = "Move Down";
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(138, 22);
            this.DeleteMI.Text = "Delete";
            // 
            // FilterDV
            // 
            this.FilterDV.AllowUserToAddRows = false;
            this.FilterDV.AllowUserToDeleteRows = false;
            this.FilterDV.AllowUserToResizeRows = false;
            this.FilterDV.BackgroundColor = System.Drawing.SystemColors.Window;
            this.FilterDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.FilterDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Icon,
            this.BrowsePathCH,
            this.SelectFieldCH,
            this.DisplayInListCH,
            this.FilterEnabledCH,
            this.FilterOperatorCH,
            this.FilterValueCH});
            this.FilterDV.ContextMenuStrip = this.PopupMenu;
            this.FilterDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.FilterDV.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.FilterDV.Location = new System.Drawing.Point(0, 0);
            this.FilterDV.Name = "FilterDV";
            this.FilterDV.RowHeadersVisible = false;
            this.FilterDV.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.FilterDV.Size = new System.Drawing.Size(754, 346);
            this.FilterDV.TabIndex = 0;
            this.FilterDV.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.FilterDV_CellDoubleClick);
            this.FilterDV.ColumnHeaderMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.FilterDV_ColumnHeaderMouseClick);
            this.FilterDV.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.FilterDV_CellContentClick);
            // 
            // RightPN
            // 
            this.RightPN.Controls.Add(this.FilterDV);
            this.RightPN.Controls.Add(this.BrowseCTRL);
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
            // BrowseCTRL
            // 
            this.BrowseCTRL.AttributesListCollapsed = false;
            this.BrowseCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseCTRL.Location = new System.Drawing.Point(0, 0);
            this.BrowseCTRL.Name = "BrowseCTRL";
            this.BrowseCTRL.Size = new System.Drawing.Size(754, 346);
            this.BrowseCTRL.SplitterDistance = 387;
            this.BrowseCTRL.TabIndex = 1;
            this.BrowseCTRL.View = null;
            // 
            // Icon
            // 
            this.Icon.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.Icon.DataPropertyName = "Icon";
            this.Icon.HeaderText = "";
            this.Icon.Name = "Icon";
            this.Icon.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.Icon.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.Icon.Width = 19;
            // 
            // BrowsePathCH
            // 
            this.BrowsePathCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.BrowsePathCH.DataPropertyName = "BrowsePath";
            this.BrowsePathCH.HeaderText = "Browse Path";
            this.BrowsePathCH.Name = "BrowsePathCH";
            this.BrowsePathCH.ReadOnly = true;
            this.BrowsePathCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.BrowsePathCH.Width = 92;
            // 
            // SelectFieldCH
            // 
            this.SelectFieldCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.SelectFieldCH.DataPropertyName = "SelectField";
            this.SelectFieldCH.FalseValue = "SelectField";
            this.SelectFieldCH.HeaderText = "Select";
            this.SelectFieldCH.Name = "SelectFieldCH";
            this.SelectFieldCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.SelectFieldCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.SelectFieldCH.TrueValue = "SelectField";
            this.SelectFieldCH.Width = 62;
            // 
            // DisplayInListCH
            // 
            this.DisplayInListCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.DisplayInListCH.DataPropertyName = "DisplayInList";
            this.DisplayInListCH.FalseValue = "DisplayInList";
            this.DisplayInListCH.HeaderText = "Display";
            this.DisplayInListCH.Name = "DisplayInListCH";
            this.DisplayInListCH.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.DisplayInListCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.DisplayInListCH.TrueValue = "DisplayInList";
            this.DisplayInListCH.Width = 66;
            // 
            // FilterEnabledCH
            // 
            this.FilterEnabledCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.FilterEnabledCH.DataPropertyName = "FilterEnabled";
            this.FilterEnabledCH.FalseValue = "FilterEnabled";
            this.FilterEnabledCH.HeaderText = "Filter";
            this.FilterEnabledCH.Name = "FilterEnabledCH";
            this.FilterEnabledCH.Resizable = System.Windows.Forms.DataGridViewTriState.False;
            this.FilterEnabledCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.FilterEnabledCH.TrueValue = "FilterEnabled";
            this.FilterEnabledCH.Width = 54;
            // 
            // FilterOperatorCH
            // 
            this.FilterOperatorCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.FilterOperatorCH.DataPropertyName = "FilterOperator";
            this.FilterOperatorCH.HeaderText = "Filter Operator";
            this.FilterOperatorCH.Name = "FilterOperatorCH";
            this.FilterOperatorCH.ReadOnly = true;
            this.FilterOperatorCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            this.FilterOperatorCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            this.FilterOperatorCH.Width = 98;
            // 
            // FilterValueCH
            // 
            this.FilterValueCH.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.FilterValueCH.DataPropertyName = "FilterValue";
            this.FilterValueCH.HeaderText = "Filter Value";
            this.FilterValueCH.Name = "FilterValueCH";
            this.FilterValueCH.ReadOnly = true;
            this.FilterValueCH.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.Programmatic;
            // 
            // EventFilterListViewCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RightPN);
            this.Name = "EventFilterListViewCtrl";
            this.Size = new System.Drawing.Size(754, 346);
            this.PopupMenu.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.FilterDV)).EndInit();
            this.RightPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.DataGridView FilterDV;
        private System.Windows.Forms.Panel RightPN;
        private System.Windows.Forms.ToolStripMenuItem MoveDownMI;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.ToolStripMenuItem MoveUpMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private BrowseNodeCtrl BrowseCTRL;
        private System.Windows.Forms.DataGridViewImageColumn Icon;
        private System.Windows.Forms.DataGridViewTextBoxColumn BrowsePathCH;
        private System.Windows.Forms.DataGridViewCheckBoxColumn SelectFieldCH;
        private System.Windows.Forms.DataGridViewCheckBoxColumn DisplayInListCH;
        private System.Windows.Forms.DataGridViewCheckBoxColumn FilterEnabledCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn FilterOperatorCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn FilterValueCH;
    }
}
