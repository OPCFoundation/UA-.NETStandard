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
    partial class SecuritySettingsDlg
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
            this.UseNativeStackLB = new System.Windows.Forms.Label();
            this.UseNativeStackCK = new System.Windows.Forms.CheckBox();
            this.SecurityModeLB = new System.Windows.Forms.Label();
            this.SecurityPolicyUriLB = new System.Windows.Forms.Label();
            this.SecurityModeCB = new System.Windows.Forms.ComboBox();
            this.SecurityPolicyUriCB = new System.Windows.Forms.ComboBox();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 73);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(258, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 0;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(179, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 1;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.UseNativeStackLB);
            this.MainPN.Controls.Add(this.UseNativeStackCK);
            this.MainPN.Controls.Add(this.SecurityModeLB);
            this.MainPN.Controls.Add(this.SecurityPolicyUriLB);
            this.MainPN.Controls.Add(this.SecurityModeCB);
            this.MainPN.Controls.Add(this.SecurityPolicyUriCB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(258, 73);
            this.MainPN.TabIndex = 1;
            // 
            // UseNativeStackLB
            // 
            this.UseNativeStackLB.AutoSize = true;
            this.UseNativeStackLB.Location = new System.Drawing.Point(4, 59);
            this.UseNativeStackLB.Name = "UseNativeStackLB";
            this.UseNativeStackLB.Size = new System.Drawing.Size(91, 13);
            this.UseNativeStackLB.TabIndex = 5;
            this.UseNativeStackLB.Text = "Use Native Stack";
            this.UseNativeStackLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UseNativeStackCK
            // 
            this.UseNativeStackCK.AutoSize = true;
            this.UseNativeStackCK.Location = new System.Drawing.Point(98, 59);
            this.UseNativeStackCK.Name = "UseNativeStackCK";
            this.UseNativeStackCK.Size = new System.Drawing.Size(15, 14);
            this.UseNativeStackCK.TabIndex = 4;
            this.UseNativeStackCK.UseVisualStyleBackColor = true;
            // 
            // SecurityModeLB
            // 
            this.SecurityModeLB.AutoSize = true;
            this.SecurityModeLB.Location = new System.Drawing.Point(4, 8);
            this.SecurityModeLB.Name = "SecurityModeLB";
            this.SecurityModeLB.Size = new System.Drawing.Size(75, 13);
            this.SecurityModeLB.TabIndex = 0;
            this.SecurityModeLB.Text = "Security Mode";
            this.SecurityModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityPolicyUriLB
            // 
            this.SecurityPolicyUriLB.AutoSize = true;
            this.SecurityPolicyUriLB.Location = new System.Drawing.Point(4, 35);
            this.SecurityPolicyUriLB.Name = "SecurityPolicyUriLB";
            this.SecurityPolicyUriLB.Size = new System.Drawing.Size(76, 13);
            this.SecurityPolicyUriLB.TabIndex = 2;
            this.SecurityPolicyUriLB.Text = "Security Policy";
            this.SecurityPolicyUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityModeCB
            // 
            this.SecurityModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityModeCB.FormattingEnabled = true;
            this.SecurityModeCB.Location = new System.Drawing.Point(98, 5);
            this.SecurityModeCB.Name = "SecurityModeCB";
            this.SecurityModeCB.Size = new System.Drawing.Size(155, 21);
            this.SecurityModeCB.TabIndex = 3;
            // 
            // SecurityPolicyUriCB
            // 
            this.SecurityPolicyUriCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityPolicyUriCB.FormattingEnabled = true;
            this.SecurityPolicyUriCB.Location = new System.Drawing.Point(98, 31);
            this.SecurityPolicyUriCB.Name = "SecurityPolicyUriCB";
            this.SecurityPolicyUriCB.Size = new System.Drawing.Size(155, 21);
            this.SecurityPolicyUriCB.TabIndex = 1;
            // 
            // SecuritySettingsDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(258, 104);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "SecuritySettingsDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Update Security Settings";
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
        private System.Windows.Forms.Label SecurityModeLB;
        private System.Windows.Forms.Label SecurityPolicyUriLB;
        private System.Windows.Forms.Label UseNativeStackLB;
        private System.Windows.Forms.CheckBox UseNativeStackCK;
        private System.Windows.Forms.ComboBox SecurityModeCB;
        private System.Windows.Forms.ComboBox SecurityPolicyUriCB;
    }
}
