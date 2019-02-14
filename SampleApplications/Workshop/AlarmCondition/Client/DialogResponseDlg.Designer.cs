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

namespace Quickstarts.AlarmConditionClient
{
    partial class DialogResponseDlg
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
            this.Response1BTN = new System.Windows.Forms.Button();
            this.Response3BTN = new System.Windows.Forms.Button();
            this.ButtonsPN = new System.Windows.Forms.TableLayoutPanel();
            this.Response2BTN = new System.Windows.Forms.Button();
            this.PromptLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(391, 3);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 21);
            this.CancelBTN.TabIndex = 5;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // OkBTN
            // 
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(3, 3);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 21);
            this.OkBTN.TabIndex = 4;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // Response1BTN
            // 
            this.Response1BTN.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.Response1BTN.DialogResult = System.Windows.Forms.DialogResult.Abort;
            this.Response1BTN.Location = new System.Drawing.Point(102, 3);
            this.Response1BTN.Name = "Response1BTN";
            this.Response1BTN.Size = new System.Drawing.Size(75, 21);
            this.Response1BTN.TabIndex = 6;
            this.Response1BTN.Text = "Response1";
            this.Response1BTN.UseVisualStyleBackColor = true;
            // 
            // Response3BTN
            // 
            this.Response3BTN.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.Response3BTN.DialogResult = System.Windows.Forms.DialogResult.Ignore;
            this.Response3BTN.Location = new System.Drawing.Point(288, 3);
            this.Response3BTN.Name = "Response3BTN";
            this.Response3BTN.Size = new System.Drawing.Size(75, 21);
            this.Response3BTN.TabIndex = 8;
            this.Response3BTN.Text = "Response3";
            this.Response3BTN.UseVisualStyleBackColor = true;
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.ColumnCount = 5;
            this.ButtonsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.ButtonsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.ButtonsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.ButtonsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.ButtonsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.ButtonsPN.Controls.Add(this.OkBTN, 0, 0);
            this.ButtonsPN.Controls.Add(this.CancelBTN, 4, 0);
            this.ButtonsPN.Controls.Add(this.Response3BTN, 3, 0);
            this.ButtonsPN.Controls.Add(this.Response1BTN, 1, 0);
            this.ButtonsPN.Controls.Add(this.Response2BTN, 2, 0);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 42);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.RowCount = 1;
            this.ButtonsPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.ButtonsPN.Size = new System.Drawing.Size(469, 27);
            this.ButtonsPN.TabIndex = 9;
            // 
            // Response2BTN
            // 
            this.Response2BTN.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.Response2BTN.DialogResult = System.Windows.Forms.DialogResult.Retry;
            this.Response2BTN.Location = new System.Drawing.Point(195, 3);
            this.Response2BTN.Name = "Response2BTN";
            this.Response2BTN.Size = new System.Drawing.Size(75, 21);
            this.Response2BTN.TabIndex = 7;
            this.Response2BTN.Text = "Response2";
            this.Response2BTN.UseVisualStyleBackColor = true;
            // 
            // PromptLB
            // 
            this.PromptLB.AutoSize = true;
            this.PromptLB.Location = new System.Drawing.Point(8, 15);
            this.PromptLB.Name = "PromptLB";
            this.PromptLB.Size = new System.Drawing.Size(238, 13);
            this.PromptLB.TabIndex = 10;
            this.PromptLB.Text = "This is a prompt asking to user to make a choice.";
            // 
            // DialogResponseDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(469, 69);
            this.Controls.Add(this.PromptLB);
            this.Controls.Add(this.ButtonsPN);
            this.MaximumSize = new System.Drawing.Size(1024, 600);
            this.MinimumSize = new System.Drawing.Size(400, 91);
            this.Name = "DialogResponseDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Select Dialog Response";
            this.ButtonsPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button Response1BTN;
        private System.Windows.Forms.Button Response3BTN;
        private System.Windows.Forms.TableLayoutPanel ButtonsPN;
        private System.Windows.Forms.Button Response2BTN;
        private System.Windows.Forms.Label PromptLB;
    }
}
