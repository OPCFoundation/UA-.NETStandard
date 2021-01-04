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
    partial class UserNamePasswordDlg
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
            this.MainPN = new System.Windows.Forms.TableLayoutPanel();
            this.PasswordLB = new System.Windows.Forms.Label();
            this.UserNameLB = new System.Windows.Forms.Label();
            this.UserNameTB = new System.Windows.Forms.TextBox();
            this.PasswordTB = new System.Windows.Forms.TextBox();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 53);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(247, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(168, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.ColumnCount = 2;
            this.MainPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPN.Controls.Add(this.PasswordLB, 0, 1);
            this.MainPN.Controls.Add(this.UserNameLB, 0, 0);
            this.MainPN.Controls.Add(this.UserNameTB, 1, 0);
            this.MainPN.Controls.Add(this.PasswordTB, 1, 1);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.RowCount = 3;
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.Size = new System.Drawing.Size(247, 53);
            this.MainPN.TabIndex = 1;
            // 
            // PasswordLB
            // 
            this.PasswordLB.AutoSize = true;
            this.PasswordLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PasswordLB.Location = new System.Drawing.Point(3, 26);
            this.PasswordLB.Name = "PasswordLB";
            this.PasswordLB.Size = new System.Drawing.Size(60, 26);
            this.PasswordLB.TabIndex = 2;
            this.PasswordLB.Text = "Password";
            this.PasswordLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserNameLB
            // 
            this.UserNameLB.AutoSize = true;
            this.UserNameLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UserNameLB.Location = new System.Drawing.Point(3, 0);
            this.UserNameLB.Name = "UserNameLB";
            this.UserNameLB.Size = new System.Drawing.Size(60, 26);
            this.UserNameLB.TabIndex = 0;
            this.UserNameLB.Text = "User Name";
            this.UserNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserNameTB
            // 
            this.UserNameTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UserNameTB.Location = new System.Drawing.Point(69, 3);
            this.UserNameTB.Name = "UserNameTB";
            this.UserNameTB.Size = new System.Drawing.Size(175, 20);
            this.UserNameTB.TabIndex = 1;
            // 
            // PasswordTB
            // 
            this.PasswordTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PasswordTB.Location = new System.Drawing.Point(69, 29);
            this.PasswordTB.Name = "PasswordTB";
            this.PasswordTB.PasswordChar = '*';
            this.PasswordTB.Size = new System.Drawing.Size(175, 20);
            this.PasswordTB.TabIndex = 3;
            // 
            // UserNamePasswordDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(247, 84);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "UserNamePasswordDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Enter User Name";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.TableLayoutPanel MainPN;
        private System.Windows.Forms.Label PasswordLB;
        private System.Windows.Forms.Label UserNameLB;
        private System.Windows.Forms.TextBox UserNameTB;
        private System.Windows.Forms.TextBox PasswordTB;
    }
}
