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
    partial class EditComplexValueDlg
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
            this.MainPN = new System.Windows.Forms.Panel();
            this.ValueCTRL = new Opc.Ua.Client.Controls.Common.EditComplexValueCtrl();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.ButtonsPN = new System.Windows.Forms.FlowLayoutPanel();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BackBTN = new System.Windows.Forms.Button();
            this.SetArraySizeBTN = new System.Windows.Forms.Button();
            this.SetTypeCB = new System.Windows.Forms.ComboBox();
            this.MainPN.SuspendLayout();
            this.BottomPN.SuspendLayout();
            this.ButtonsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainPN
            // 
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ValueCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(658, 221);
            this.MainPN.TabIndex = 1;
            // 
            // ValueCTRL
            // 
            this.ValueCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueCTRL.Location = new System.Drawing.Point(0, 0);
            this.ValueCTRL.Name = "ValueCTRL";
            this.ValueCTRL.Size = new System.Drawing.Size(658, 221);
            this.ValueCTRL.TabIndex = 0;
            this.ValueCTRL.ValueChanged += new System.EventHandler(this.ValueCTRL_ValueChanged);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.ButtonsPN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 221);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(658, 28);
            this.BottomPN.TabIndex = 3;
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.BackBTN);
            this.ButtonsPN.Controls.Add(this.SetArraySizeBTN);
            this.ButtonsPN.Controls.Add(this.SetTypeCB);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Right;
            this.ButtonsPN.Location = new System.Drawing.Point(174, 0);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.ButtonsPN.Size = new System.Drawing.Size(484, 28);
            this.ButtonsPN.TabIndex = 5;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(406, 3);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 7;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.OkBTN.Location = new System.Drawing.Point(325, 3);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 6;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // BackBTN
            // 
            this.BackBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.BackBTN.Location = new System.Drawing.Point(244, 3);
            this.BackBTN.Name = "BackBTN";
            this.BackBTN.Size = new System.Drawing.Size(75, 23);
            this.BackBTN.TabIndex = 5;
            this.BackBTN.Text = "Back";
            this.BackBTN.UseVisualStyleBackColor = true;
            this.BackBTN.Click += new System.EventHandler(this.BackBTN_Click);
            // 
            // SetArraySizeBTN
            // 
            this.SetArraySizeBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.SetArraySizeBTN.Location = new System.Drawing.Point(148, 3);
            this.SetArraySizeBTN.Name = "SetArraySizeBTN";
            this.SetArraySizeBTN.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.SetArraySizeBTN.Size = new System.Drawing.Size(90, 23);
            this.SetArraySizeBTN.TabIndex = 9;
            this.SetArraySizeBTN.Text = "Set Array Size...";
            this.SetArraySizeBTN.UseVisualStyleBackColor = true;
            this.SetArraySizeBTN.Click += new System.EventHandler(this.SetTypeBTN_Click);
            // 
            // SetTypeCB
            // 
            this.SetTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SetTypeCB.FormattingEnabled = true;
            this.SetTypeCB.Location = new System.Drawing.Point(27, 4);
            this.SetTypeCB.Margin = new System.Windows.Forms.Padding(3, 4, 3, 3);
            this.SetTypeCB.Name = "SetTypeCB";
            this.SetTypeCB.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.SetTypeCB.Size = new System.Drawing.Size(115, 21);
            this.SetTypeCB.TabIndex = 10;
            this.SetTypeCB.SelectedIndexChanged += new System.EventHandler(this.SetTypeCB_SelectedIndexChanged);
            // 
            // EditComplexValueDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(658, 249);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditComplexValueDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Value";
            this.MainPN.ResumeLayout(false);
            this.BottomPN.ResumeLayout(false);
            this.ButtonsPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel MainPN;
        private Opc.Ua.Client.Controls.Common.EditComplexValueCtrl ValueCTRL;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.FlowLayoutPanel ButtonsPN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button BackBTN;
        private System.Windows.Forms.Button SetArraySizeBTN;
        private System.Windows.Forms.ComboBox SetTypeCB;
    }
}
