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
    partial class CallRequestDlg
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
            this.ButtonPN = new System.Windows.Forms.FlowLayoutPanel();
            this.CloseBTN = new System.Windows.Forms.Button();
            this.CallBTN = new System.Windows.Forms.Button();
            this.BackBTN = new System.Windows.Forms.Button();
            this.CallRequestCTRL = new Opc.Ua.Client.Controls.CallRequestListViewCtrl();
            this.ButtonPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonPN
            // 
            this.ButtonPN.Controls.Add(this.CloseBTN);
            this.ButtonPN.Controls.Add(this.CallBTN);
            this.ButtonPN.Controls.Add(this.BackBTN);
            this.ButtonPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonPN.Location = new System.Drawing.Point(0, 233);
            this.ButtonPN.Name = "ButtonPN";
            this.ButtonPN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.ButtonPN.Size = new System.Drawing.Size(784, 29);
            this.ButtonPN.TabIndex = 1;
            // 
            // CloseBTN
            // 
            this.CloseBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseBTN.Location = new System.Drawing.Point(706, 3);
            this.CloseBTN.Name = "CloseBTN";
            this.CloseBTN.Size = new System.Drawing.Size(75, 23);
            this.CloseBTN.TabIndex = 0;
            this.CloseBTN.Text = "Close";
            this.CloseBTN.UseVisualStyleBackColor = true;
            this.CloseBTN.Click += new System.EventHandler(this.CloseBTN_Click);
            // 
            // CallBTN
            // 
            this.CallBTN.Location = new System.Drawing.Point(625, 3);
            this.CallBTN.Name = "CallBTN";
            this.CallBTN.Size = new System.Drawing.Size(75, 23);
            this.CallBTN.TabIndex = 1;
            this.CallBTN.Text = "Call";
            this.CallBTN.UseVisualStyleBackColor = true;
            this.CallBTN.Click += new System.EventHandler(this.CallBTN_Click);
            // 
            // BackBTN
            // 
            this.BackBTN.Location = new System.Drawing.Point(544, 3);
            this.BackBTN.Name = "BackBTN";
            this.BackBTN.Size = new System.Drawing.Size(75, 23);
            this.BackBTN.TabIndex = 2;
            this.BackBTN.Text = "Back";
            this.BackBTN.UseVisualStyleBackColor = true;
            this.BackBTN.Visible = false;
            this.BackBTN.Click += new System.EventHandler(this.BackBTN_Click);
            // 
            // CallRequestCTRL
            // 
            this.CallRequestCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CallRequestCTRL.Location = new System.Drawing.Point(0, 0);
            this.CallRequestCTRL.Name = "CallRequestCTRL";
            this.CallRequestCTRL.Size = new System.Drawing.Size(784, 233);
            this.CallRequestCTRL.TabIndex = 0;
            // 
            // CallRequestDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 262);
            this.Controls.Add(this.CallRequestCTRL);
            this.Controls.Add(this.ButtonPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CallRequestDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Call Method";
            this.ButtonPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private CallRequestListViewCtrl CallRequestCTRL;
        private System.Windows.Forms.FlowLayoutPanel ButtonPN;
        private System.Windows.Forms.Button CloseBTN;
        private System.Windows.Forms.Button CallBTN;
        private System.Windows.Forms.Button BackBTN;

    }
}
