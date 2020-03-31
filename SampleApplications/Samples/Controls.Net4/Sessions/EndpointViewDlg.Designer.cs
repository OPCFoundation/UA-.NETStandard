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
    partial class EndpointViewDlg
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
            this.ServerCertificateTB = new System.Windows.Forms.TextBox();
            this.ServerCertificateLB = new System.Windows.Forms.Label();
            this.ServerUriTB = new System.Windows.Forms.TextBox();
            this.ServerUriLB = new System.Windows.Forms.Label();
            this.ServerNameTB = new System.Windows.Forms.TextBox();
            this.ServerNameLB = new System.Windows.Forms.Label();
            this.EndpointTB = new System.Windows.Forms.TextBox();
            this.EndpointLB = new System.Windows.Forms.Label();
            this.SecurityPolicyUriTB = new System.Windows.Forms.TextBox();
            this.UserIdentityTypeTB = new System.Windows.Forms.TextBox();
            this.SecurityModeTB = new System.Windows.Forms.TextBox();
            this.SecurityPolicyUriLB = new System.Windows.Forms.Label();
            this.UserIdentityTypeLB = new System.Windows.Forms.Label();
            this.SecurityModeLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 169);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(387, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(308, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ServerCertificateTB);
            this.MainPN.Controls.Add(this.ServerCertificateLB);
            this.MainPN.Controls.Add(this.ServerUriTB);
            this.MainPN.Controls.Add(this.ServerUriLB);
            this.MainPN.Controls.Add(this.ServerNameTB);
            this.MainPN.Controls.Add(this.ServerNameLB);
            this.MainPN.Controls.Add(this.EndpointTB);
            this.MainPN.Controls.Add(this.EndpointLB);
            this.MainPN.Controls.Add(this.SecurityPolicyUriTB);
            this.MainPN.Controls.Add(this.UserIdentityTypeTB);
            this.MainPN.Controls.Add(this.SecurityModeTB);
            this.MainPN.Controls.Add(this.SecurityPolicyUriLB);
            this.MainPN.Controls.Add(this.UserIdentityTypeLB);
            this.MainPN.Controls.Add(this.SecurityModeLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(387, 169);
            this.MainPN.TabIndex = 1;
            // 
            // ServerCertificateTB
            // 
            this.ServerCertificateTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerCertificateTB.Location = new System.Drawing.Point(122, 76);
            this.ServerCertificateTB.Name = "ServerCertificateTB";
            this.ServerCertificateTB.Size = new System.Drawing.Size(261, 20);
            this.ServerCertificateTB.TabIndex = 19;
            // 
            // ServerCertificateLB
            // 
            this.ServerCertificateLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerCertificateLB.AutoSize = true;
            this.ServerCertificateLB.Location = new System.Drawing.Point(4, 80);
            this.ServerCertificateLB.Name = "ServerCertificateLB";
            this.ServerCertificateLB.Size = new System.Drawing.Size(88, 13);
            this.ServerCertificateLB.TabIndex = 18;
            this.ServerCertificateLB.Text = "Server Certificate";
            this.ServerCertificateLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerUriTB
            // 
            this.ServerUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerUriTB.Location = new System.Drawing.Point(122, 52);
            this.ServerUriTB.Name = "ServerUriTB";
            this.ServerUriTB.Size = new System.Drawing.Size(261, 20);
            this.ServerUriTB.TabIndex = 17;
            // 
            // ServerUriLB
            // 
            this.ServerUriLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerUriLB.AutoSize = true;
            this.ServerUriLB.Location = new System.Drawing.Point(4, 56);
            this.ServerUriLB.Name = "ServerUriLB";
            this.ServerUriLB.Size = new System.Drawing.Size(60, 13);
            this.ServerUriLB.TabIndex = 16;
            this.ServerUriLB.Text = "Server URI";
            this.ServerUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerNameTB
            // 
            this.ServerNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerNameTB.Location = new System.Drawing.Point(122, 28);
            this.ServerNameTB.Name = "ServerNameTB";
            this.ServerNameTB.Size = new System.Drawing.Size(261, 20);
            this.ServerNameTB.TabIndex = 15;
            // 
            // ServerNameLB
            // 
            this.ServerNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerNameLB.AutoSize = true;
            this.ServerNameLB.Location = new System.Drawing.Point(4, 32);
            this.ServerNameLB.Name = "ServerNameLB";
            this.ServerNameLB.Size = new System.Drawing.Size(69, 13);
            this.ServerNameLB.TabIndex = 14;
            this.ServerNameLB.Text = "Server Name";
            this.ServerNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndpointTB
            // 
            this.EndpointTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EndpointTB.Location = new System.Drawing.Point(122, 4);
            this.EndpointTB.Name = "EndpointTB";
            this.EndpointTB.Size = new System.Drawing.Size(261, 20);
            this.EndpointTB.TabIndex = 1;
            // 
            // EndpointLB
            // 
            this.EndpointLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.EndpointLB.AutoSize = true;
            this.EndpointLB.Location = new System.Drawing.Point(4, 8);
            this.EndpointLB.Name = "EndpointLB";
            this.EndpointLB.Size = new System.Drawing.Size(49, 13);
            this.EndpointLB.TabIndex = 0;
            this.EndpointLB.Text = "Endpoint";
            this.EndpointLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityPolicyUriTB
            // 
            this.SecurityPolicyUriTB.Location = new System.Drawing.Point(122, 124);
            this.SecurityPolicyUriTB.Name = "SecurityPolicyUriTB";
            this.SecurityPolicyUriTB.Size = new System.Drawing.Size(131, 20);
            this.SecurityPolicyUriTB.TabIndex = 7;
            // 
            // UserIdentityTypeTB
            // 
            this.UserIdentityTypeTB.Location = new System.Drawing.Point(122, 148);
            this.UserIdentityTypeTB.Name = "UserIdentityTypeTB";
            this.UserIdentityTypeTB.Size = new System.Drawing.Size(131, 20);
            this.UserIdentityTypeTB.TabIndex = 5;
            // 
            // SecurityModeTB
            // 
            this.SecurityModeTB.Location = new System.Drawing.Point(122, 100);
            this.SecurityModeTB.Name = "SecurityModeTB";
            this.SecurityModeTB.Size = new System.Drawing.Size(131, 20);
            this.SecurityModeTB.TabIndex = 3;
            // 
            // SecurityPolicyUriLB
            // 
            this.SecurityPolicyUriLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SecurityPolicyUriLB.AutoSize = true;
            this.SecurityPolicyUriLB.Location = new System.Drawing.Point(4, 128);
            this.SecurityPolicyUriLB.Name = "SecurityPolicyUriLB";
            this.SecurityPolicyUriLB.Size = new System.Drawing.Size(77, 13);
            this.SecurityPolicyUriLB.TabIndex = 6;
            this.SecurityPolicyUriLB.Text = "Algorithm Suite";
            this.SecurityPolicyUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserIdentityTypeLB
            // 
            this.UserIdentityTypeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UserIdentityTypeLB.AutoSize = true;
            this.UserIdentityTypeLB.Location = new System.Drawing.Point(4, 152);
            this.UserIdentityTypeLB.Name = "UserIdentityTypeLB";
            this.UserIdentityTypeLB.Size = new System.Drawing.Size(93, 13);
            this.UserIdentityTypeLB.TabIndex = 4;
            this.UserIdentityTypeLB.Text = "User Identity Type";
            this.UserIdentityTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityModeLB
            // 
            this.SecurityModeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SecurityModeLB.AutoSize = true;
            this.SecurityModeLB.Location = new System.Drawing.Point(4, 104);
            this.SecurityModeLB.Name = "SecurityModeLB";
            this.SecurityModeLB.Size = new System.Drawing.Size(75, 13);
            this.SecurityModeLB.TabIndex = 2;
            this.SecurityModeLB.Text = "Security Mode";
            this.SecurityModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndpointViewDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(387, 200);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "EndpointViewDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Endpoint Description";
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
        private System.Windows.Forms.Label UserIdentityTypeLB;
        private System.Windows.Forms.Label SecurityModeLB;
        private System.Windows.Forms.Label SecurityPolicyUriLB;
        private System.Windows.Forms.TextBox SecurityPolicyUriTB;
        private System.Windows.Forms.TextBox UserIdentityTypeTB;
        private System.Windows.Forms.TextBox SecurityModeTB;
        private System.Windows.Forms.TextBox EndpointTB;
        private System.Windows.Forms.Label EndpointLB;
        private System.Windows.Forms.TextBox ServerCertificateTB;
        private System.Windows.Forms.Label ServerCertificateLB;
        private System.Windows.Forms.TextBox ServerUriTB;
        private System.Windows.Forms.Label ServerUriLB;
        private System.Windows.Forms.TextBox ServerNameTB;
        private System.Windows.Forms.Label ServerNameLB;
    }
}
