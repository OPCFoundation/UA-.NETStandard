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

namespace Opc.Ua.Sample.Controls
{
    partial class SessionOpenDlg
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.UserNameCB = new System.Windows.Forms.ComboBox();
            this.PasswordTB = new System.Windows.Forms.TextBox();
            this.UserIdentityTypeCB = new System.Windows.Forms.ComboBox();
            this.SessionNameTB = new System.Windows.Forms.TextBox();
            this.PasswordLB = new System.Windows.Forms.Label();
            this.UserIdentityTypeLB = new System.Windows.Forms.Label();
            this.UserNameLB = new System.Windows.Forms.Label();
            this.SessionNameLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 101);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(313, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(234, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.UserNameCB);
            this.panel1.Controls.Add(this.PasswordTB);
            this.panel1.Controls.Add(this.UserIdentityTypeCB);
            this.panel1.Controls.Add(this.SessionNameTB);
            this.panel1.Controls.Add(this.PasswordLB);
            this.panel1.Controls.Add(this.UserIdentityTypeLB);
            this.panel1.Controls.Add(this.UserNameLB);
            this.panel1.Controls.Add(this.SessionNameLB);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(313, 101);
            this.panel1.TabIndex = 1;
            // 
            // UserNameCB
            // 
            this.UserNameCB.FormattingEnabled = true;
            this.UserNameCB.Location = new System.Drawing.Point(111, 52);
            this.UserNameCB.Name = "UserNameCB";
            this.UserNameCB.Size = new System.Drawing.Size(197, 21);
            this.UserNameCB.TabIndex = 5;
            // 
            // PasswordTB
            // 
            this.PasswordTB.Location = new System.Drawing.Point(111, 76);
            this.PasswordTB.Name = "PasswordTB";
            this.PasswordTB.PasswordChar = '*';
            this.PasswordTB.Size = new System.Drawing.Size(197, 20);
            this.PasswordTB.TabIndex = 7;
            // 
            // UserIdentityTypeCB
            // 
            this.UserIdentityTypeCB.FormattingEnabled = true;
            this.UserIdentityTypeCB.Location = new System.Drawing.Point(111, 28);
            this.UserIdentityTypeCB.Name = "UserIdentityTypeCB";
            this.UserIdentityTypeCB.Size = new System.Drawing.Size(197, 21);
            this.UserIdentityTypeCB.TabIndex = 3;
            this.UserIdentityTypeCB.SelectedIndexChanged += new System.EventHandler(this.UserIdentityTypeCB_SelectedIndexChanged);
            // 
            // SessionNameTB
            // 
            this.SessionNameTB.Location = new System.Drawing.Point(111, 4);
            this.SessionNameTB.Name = "SessionNameTB";
            this.SessionNameTB.Size = new System.Drawing.Size(197, 20);
            this.SessionNameTB.TabIndex = 1;
            // 
            // PasswordLB
            // 
            this.PasswordLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PasswordLB.AutoSize = true;
            this.PasswordLB.Location = new System.Drawing.Point(4, 80);
            this.PasswordLB.Name = "PasswordLB";
            this.PasswordLB.Size = new System.Drawing.Size(53, 13);
            this.PasswordLB.TabIndex = 6;
            this.PasswordLB.Text = "Password";
            this.PasswordLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserIdentityTypeLB
            // 
            this.UserIdentityTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UserIdentityTypeLB.AutoSize = true;
            this.UserIdentityTypeLB.Location = new System.Drawing.Point(4, 32);
            this.UserIdentityTypeLB.Name = "UserIdentityTypeLB";
            this.UserIdentityTypeLB.Size = new System.Drawing.Size(105, 13);
            this.UserIdentityTypeLB.TabIndex = 2;
            this.UserIdentityTypeLB.Text = "Authentication Mode";
            this.UserIdentityTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserNameLB
            // 
            this.UserNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UserNameLB.AutoSize = true;
            this.UserNameLB.Location = new System.Drawing.Point(4, 56);
            this.UserNameLB.Name = "UserNameLB";
            this.UserNameLB.Size = new System.Drawing.Size(60, 13);
            this.UserNameLB.TabIndex = 4;
            this.UserNameLB.Text = "User Name";
            this.UserNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SessionNameLB
            // 
            this.SessionNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SessionNameLB.AutoSize = true;
            this.SessionNameLB.Location = new System.Drawing.Point(4, 8);
            this.SessionNameLB.Name = "SessionNameLB";
            this.SessionNameLB.Size = new System.Drawing.Size(75, 13);
            this.SessionNameLB.TabIndex = 0;
            this.SessionNameLB.Text = "Session Name";
            this.SessionNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SessionOpenDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(313, 132);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "SessionOpenDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Open Session";
            this.ButtonsPN.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.ComboBox UserIdentityTypeCB;
        private System.Windows.Forms.TextBox SessionNameTB;
        private System.Windows.Forms.Label PasswordLB;
        private System.Windows.Forms.Label UserIdentityTypeLB;
        private System.Windows.Forms.Label UserNameLB;
        private System.Windows.Forms.Label SessionNameLB;
        private System.Windows.Forms.TextBox PasswordTB;
        private System.Windows.Forms.ComboBox UserNameCB;
    }
}
