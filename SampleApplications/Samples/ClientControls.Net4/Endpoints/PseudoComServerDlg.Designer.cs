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
    partial class PseudoComServerDlg
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
            this.ProgIdTB = new System.Windows.Forms.TextBox();
            this.ProgIdLB = new System.Windows.Forms.Label();
            this.SpecificationCB = new System.Windows.Forms.ComboBox();
            this.ClsidBTN = new System.Windows.Forms.Button();
            this.ClsidTB = new System.Windows.Forms.TextBox();
            this.SpecificationLB = new System.Windows.Forms.Label();
            this.ClsidLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 79);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(491, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(412, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ProgIdTB);
            this.MainPN.Controls.Add(this.ProgIdLB);
            this.MainPN.Controls.Add(this.SpecificationCB);
            this.MainPN.Controls.Add(this.ClsidBTN);
            this.MainPN.Controls.Add(this.ClsidTB);
            this.MainPN.Controls.Add(this.SpecificationLB);
            this.MainPN.Controls.Add(this.ClsidLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(491, 110);
            this.MainPN.TabIndex = 1;
            // 
            // ProgIdTB
            // 
            this.ProgIdTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProgIdTB.Location = new System.Drawing.Point(52, 58);
            this.ProgIdTB.Name = "ProgIdTB";
            this.ProgIdTB.Size = new System.Drawing.Size(433, 20);
            this.ProgIdTB.TabIndex = 6;
            // 
            // ProgIdLB
            // 
            this.ProgIdLB.AutoSize = true;
            this.ProgIdLB.Location = new System.Drawing.Point(4, 61);
            this.ProgIdLB.Name = "ProgIdLB";
            this.ProgIdLB.Size = new System.Drawing.Size(43, 13);
            this.ProgIdLB.TabIndex = 5;
            this.ProgIdLB.Text = "Prog ID";
            this.ProgIdLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SpecificationCB
            // 
            this.SpecificationCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SpecificationCB.FormattingEnabled = true;
            this.SpecificationCB.Location = new System.Drawing.Point(52, 5);
            this.SpecificationCB.Name = "SpecificationCB";
            this.SpecificationCB.Size = new System.Drawing.Size(178, 21);
            this.SpecificationCB.TabIndex = 1;
            this.SpecificationCB.SelectedIndexChanged += new System.EventHandler(this.SpecificationCB_SelectedIndexChanged);
            // 
            // ClsidBTN
            // 
            this.ClsidBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ClsidBTN.Location = new System.Drawing.Point(461, 31);
            this.ClsidBTN.Name = "ClsidBTN";
            this.ClsidBTN.Size = new System.Drawing.Size(25, 22);
            this.ClsidBTN.TabIndex = 4;
            this.ClsidBTN.Text = "...";
            this.ClsidBTN.UseVisualStyleBackColor = true;
            // 
            // ClsidTB
            // 
            this.ClsidTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ClsidTB.Location = new System.Drawing.Point(52, 32);
            this.ClsidTB.Name = "ClsidTB";
            this.ClsidTB.Size = new System.Drawing.Size(405, 20);
            this.ClsidTB.TabIndex = 3;
            // 
            // SpecificationLB
            // 
            this.SpecificationLB.AutoSize = true;
            this.SpecificationLB.Location = new System.Drawing.Point(4, 9);
            this.SpecificationLB.Name = "SpecificationLB";
            this.SpecificationLB.Size = new System.Drawing.Size(46, 13);
            this.SpecificationLB.TabIndex = 0;
            this.SpecificationLB.Text = "Protocol";
            this.SpecificationLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ClsidLB
            // 
            this.ClsidLB.AutoSize = true;
            this.ClsidLB.Location = new System.Drawing.Point(4, 36);
            this.ClsidLB.Name = "ClsidLB";
            this.ClsidLB.Size = new System.Drawing.Size(38, 13);
            this.ClsidLB.TabIndex = 2;
            this.ClsidLB.Text = "CLSID";
            this.ClsidLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PseudoComServerDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(491, 110);
            this.Controls.Add(this.ButtonsPN);
            this.Controls.Add(this.MainPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "PseudoComServerDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "COM Server Configuration";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label SpecificationLB;
        private System.Windows.Forms.Label ClsidLB;
        private System.Windows.Forms.Button ClsidBTN;
        private System.Windows.Forms.TextBox ClsidTB;
        private System.Windows.Forms.ComboBox SpecificationCB;
        private System.Windows.Forms.TextBox ProgIdTB;
        private System.Windows.Forms.Label ProgIdLB;
    }
}
