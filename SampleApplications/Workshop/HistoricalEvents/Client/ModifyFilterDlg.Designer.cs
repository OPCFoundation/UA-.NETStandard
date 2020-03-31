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

namespace Quickstarts.HistoricalEvents.Client
{
    partial class ModifyFilterDlg
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
            this.components = new System.ComponentModel.Container();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.EventFieldsLV = new System.Windows.Forms.ListView();
            this.BrowsePathCH = new System.Windows.Forms.ColumnHeader();
            this.DisplayInListCH = new System.Windows.Forms.ColumnHeader();
            this.FilterOperandCH = new System.Windows.Forms.ColumnHeader();
            this.FilterValueCH = new System.Windows.Forms.ColumnHeader();
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.DisplayInListViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteFieldMI = new System.Windows.Forms.ToolStripMenuItem();
            this.FilterOperandMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetFilterValueMI = new System.Windows.Forms.ToolStripMenuItem();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.FilterEnabledMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.FilterEnabledCH = new System.Windows.Forms.ColumnHeader();
            this.PopupMenu.SuspendLayout();
            this.BottomPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(726, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 5;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 4;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // EventFieldsLV
            // 
            this.EventFieldsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.BrowsePathCH,
            this.DisplayInListCH,
            this.FilterEnabledCH,
            this.FilterOperandCH,
            this.FilterValueCH,
            this.DataTypeCH});
            this.EventFieldsLV.ContextMenuStrip = this.PopupMenu;
            this.EventFieldsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventFieldsLV.ForeColor = System.Drawing.SystemColors.WindowText;
            this.EventFieldsLV.FullRowSelect = true;
            this.EventFieldsLV.GridLines = true;
            this.EventFieldsLV.Location = new System.Drawing.Point(0, 0);
            this.EventFieldsLV.Name = "EventFieldsLV";
            this.EventFieldsLV.Size = new System.Drawing.Size(804, 431);
            this.EventFieldsLV.TabIndex = 7;
            this.EventFieldsLV.UseCompatibleStateImageBehavior = false;
            this.EventFieldsLV.View = System.Windows.Forms.View.Details;
            // 
            // BrowsePathCH
            // 
            this.BrowsePathCH.Text = "Field";
            // 
            // DisplayInListCH
            // 
            this.DisplayInListCH.Text = "Display In List";
            this.DisplayInListCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.DisplayInListCH.Width = 129;
            // 
            // FilterOperandCH
            // 
            this.FilterOperandCH.Text = "Filter Operand";
            this.FilterOperandCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.FilterOperandCH.Width = 145;
            // 
            // FilterValueCH
            // 
            this.FilterValueCH.Text = "Filter Value";
            this.FilterValueCH.Width = 224;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.DisplayInListViewMI,
            this.toolStripSeparator1,
            this.FilterEnabledMI,
            this.FilterOperandMI,
            this.SetFilterValueMI,
            this.toolStripSeparator2,
            this.DeleteFieldMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(175, 126);
            this.PopupMenu.Opening += new System.ComponentModel.CancelEventHandler(this.PopupMenu_Opening);
            // 
            // DisplayInListViewMI
            // 
            this.DisplayInListViewMI.Checked = true;
            this.DisplayInListViewMI.CheckOnClick = true;
            this.DisplayInListViewMI.CheckState = System.Windows.Forms.CheckState.Indeterminate;
            this.DisplayInListViewMI.Name = "DisplayInListViewMI";
            this.DisplayInListViewMI.Size = new System.Drawing.Size(174, 22);
            this.DisplayInListViewMI.Text = "Display In List View";
            this.DisplayInListViewMI.CheckStateChanged += new System.EventHandler(this.DisplayInListViewMI_CheckedChanged);
            // 
            // DeleteFieldMI
            // 
            this.DeleteFieldMI.Name = "DeleteFieldMI";
            this.DeleteFieldMI.Size = new System.Drawing.Size(174, 22);
            this.DeleteFieldMI.Text = "Delete Field";
            this.DeleteFieldMI.Click += new System.EventHandler(this.DeleteFieldMI_Click);
            // 
            // FilterOperandMI
            // 
            this.FilterOperandMI.Name = "FilterOperandMI";
            this.FilterOperandMI.Size = new System.Drawing.Size(174, 22);
            this.FilterOperandMI.Text = "Filter Operand";
            this.FilterOperandMI.DropDownOpening += new System.EventHandler(this.FilterOperandMI_DropDownOpening);
            // 
            // SetFilterValueMI
            // 
            this.SetFilterValueMI.Name = "SetFilterValueMI";
            this.SetFilterValueMI.Size = new System.Drawing.Size(174, 22);
            this.SetFilterValueMI.Text = "Set Filter Value...";
            this.SetFilterValueMI.Click += new System.EventHandler(this.SetFilterValueMI_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 431);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(804, 30);
            this.BottomPN.TabIndex = 9;
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(171, 6);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(171, 6);
            // 
            // FilterEnabledMI
            // 
            this.FilterEnabledMI.Checked = true;
            this.FilterEnabledMI.CheckOnClick = true;
            this.FilterEnabledMI.CheckState = System.Windows.Forms.CheckState.Indeterminate;
            this.FilterEnabledMI.Name = "FilterEnabledMI";
            this.FilterEnabledMI.Size = new System.Drawing.Size(174, 22);
            this.FilterEnabledMI.Text = "Filter Enabled";
            this.FilterEnabledMI.CheckStateChanged += new System.EventHandler(this.FilterEnabledMI_CheckedChanged);
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.Text = "Data Type";
            // 
            // FilterEnabledCH
            // 
            this.FilterEnabledCH.Text = "Filter Enabled";
            this.FilterEnabledCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.FilterEnabledCH.Width = 104;
            // 
            // ModifyFilterDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(804, 461);
            this.Controls.Add(this.EventFieldsLV);
            this.Controls.Add(this.BottomPN);
            this.MaximumSize = new System.Drawing.Size(1200, 1200);
            this.MinimumSize = new System.Drawing.Size(400, 91);
            this.Name = "ModifyFilterDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Modify Event Filter";
            this.PopupMenu.ResumeLayout(false);
            this.BottomPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.ListView EventFieldsLV;
        private System.Windows.Forms.ColumnHeader BrowsePathCH;
        private System.Windows.Forms.ColumnHeader DisplayInListCH;
        private System.Windows.Forms.ColumnHeader FilterOperandCH;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem FilterOperandMI;
        private System.Windows.Forms.ColumnHeader FilterValueCH;
        private System.Windows.Forms.ToolStripMenuItem DisplayInListViewMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteFieldMI;
        private System.Windows.Forms.ToolStripMenuItem SetFilterValueMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem FilterEnabledMI;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ColumnHeader DataTypeCH;
        private System.Windows.Forms.ColumnHeader FilterEnabledCH;
    }
}
