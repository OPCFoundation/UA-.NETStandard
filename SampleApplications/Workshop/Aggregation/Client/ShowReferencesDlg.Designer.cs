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

namespace AggregationClient
{
    partial class ShowReferencesDlg
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
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ReferencesLV = new System.Windows.Forms.ListView();
            this.ReferenceTypeCH = new System.Windows.Forms.ColumnHeader();
            this.TargetNameCH = new System.Windows.Forms.ColumnHeader();
            this.NodeClassCH = new System.Windows.Forms.ColumnHeader();
            this.TypeDefinitionCH = new System.Windows.Forms.ColumnHeader();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(725, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 5;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 4;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 388);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(803, 30);
            this.BottomPN.TabIndex = 9;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ReferencesLV);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(803, 388);
            this.MainPN.TabIndex = 10;
            // 
            // ReferencesLV
            // 
            this.ReferencesLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.ReferenceTypeCH,
            this.TargetNameCH,
            this.NodeClassCH,
            this.TypeDefinitionCH});
            this.ReferencesLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ReferencesLV.Location = new System.Drawing.Point(0, 0);
            this.ReferencesLV.Name = "ReferencesLV";
            this.ReferencesLV.Size = new System.Drawing.Size(803, 388);
            this.ReferencesLV.TabIndex = 1;
            this.ReferencesLV.UseCompatibleStateImageBehavior = false;
            this.ReferencesLV.View = System.Windows.Forms.View.Details;
            // 
            // ReferenceTypeCH
            // 
            this.ReferenceTypeCH.Text = "Reference Type";
            this.ReferenceTypeCH.Width = 106;
            // 
            // TargetNameCH
            // 
            this.TargetNameCH.Text = "Target Name";
            this.TargetNameCH.Width = 77;
            // 
            // NodeClassCH
            // 
            this.NodeClassCH.Text = "Node Class";
            this.NodeClassCH.Width = 81;
            // 
            // TypeDefinitionCH
            // 
            this.TypeDefinitionCH.Text = "Type Definition";
            this.TypeDefinitionCH.Width = 125;
            // 
            // ShowReferencesDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(803, 418);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.MaximumSize = new System.Drawing.Size(1200, 1200);
            this.MinimumSize = new System.Drawing.Size(400, 91);
            this.Name = "ShowReferencesDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Show References";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ListView ReferencesLV;
        private System.Windows.Forms.ColumnHeader ReferenceTypeCH;
        private System.Windows.Forms.ColumnHeader TargetNameCH;
        private System.Windows.Forms.ColumnHeader NodeClassCH;
        private System.Windows.Forms.ColumnHeader TypeDefinitionCH;
    }
}
