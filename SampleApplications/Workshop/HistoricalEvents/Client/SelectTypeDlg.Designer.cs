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
    partial class SelectTypeDlg
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
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BrowseTV = new System.Windows.Forms.TreeView();
            this.DeclarationsLV = new System.Windows.Forms.ListView();
            this.BrowsePathCH = new System.Windows.Forms.ColumnHeader();
            this.DataTypeCH = new System.Windows.Forms.ColumnHeader();
            this.DescriptionCH = new System.Windows.Forms.ColumnHeader();
            this.MainPN = new System.Windows.Forms.SplitContainer();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN.Panel1.SuspendLayout();
            this.MainPN.Panel2.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.BottomPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(937, 4);
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
            // BrowseTV
            // 
            this.BrowseTV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BrowseTV.Location = new System.Drawing.Point(0, 0);
            this.BrowseTV.Name = "BrowseTV";
            this.BrowseTV.Size = new System.Drawing.Size(337, 483);
            this.BrowseTV.TabIndex = 6;
            this.BrowseTV.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.BrowseTV_BeforeExpand);
            this.BrowseTV.DoubleClick += new System.EventHandler(this.BrowseTV_DoubleClick);
            this.BrowseTV.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.BrowseTV_AfterSelect);
            // 
            // DeclarationsLV
            // 
            this.DeclarationsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.BrowsePathCH,
            this.DataTypeCH,
            this.DescriptionCH});
            this.DeclarationsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DeclarationsLV.ForeColor = System.Drawing.SystemColors.WindowText;
            this.DeclarationsLV.FullRowSelect = true;
            this.DeclarationsLV.Location = new System.Drawing.Point(0, 0);
            this.DeclarationsLV.Name = "DeclarationsLV";
            this.DeclarationsLV.Size = new System.Drawing.Size(674, 483);
            this.DeclarationsLV.TabIndex = 7;
            this.DeclarationsLV.UseCompatibleStateImageBehavior = false;
            this.DeclarationsLV.View = System.Windows.Forms.View.Details;
            // 
            // BrowsePathCH
            // 
            this.BrowsePathCH.Text = "Path";
            // 
            // DataTypeCH
            // 
            this.DataTypeCH.Text = "Data Type";
            this.DataTypeCH.Width = 129;
            // 
            // DescriptionCH
            // 
            this.DescriptionCH.Text = "Description";
            this.DescriptionCH.Width = 389;
            // 
            // MainPN
            // 
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            // 
            // MainPN.Panel1
            // 
            this.MainPN.Panel1.Controls.Add(this.BrowseTV);
            // 
            // MainPN.Panel2
            // 
            this.MainPN.Panel2.Controls.Add(this.DeclarationsLV);
            this.MainPN.Size = new System.Drawing.Size(1015, 483);
            this.MainPN.SplitterDistance = 337;
            this.MainPN.TabIndex = 8;
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 483);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(1015, 30);
            this.BottomPN.TabIndex = 9;
            // 
            // SelectTypeDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(1015, 513);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.MaximumSize = new System.Drawing.Size(1200, 1200);
            this.MinimumSize = new System.Drawing.Size(400, 91);
            this.Name = "SelectTypeDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Select Type";
            this.MainPN.Panel1.ResumeLayout(false);
            this.MainPN.Panel2.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.BottomPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.TreeView BrowseTV;
        private System.Windows.Forms.ListView DeclarationsLV;
        private System.Windows.Forms.SplitContainer MainPN;
        private System.Windows.Forms.ColumnHeader BrowsePathCH;
        private System.Windows.Forms.ColumnHeader DataTypeCH;
        private System.Windows.Forms.ColumnHeader DescriptionCH;
        private System.Windows.Forms.Panel BottomPN;
    }
}
