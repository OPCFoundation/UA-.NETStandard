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
    partial class ViewCertificateDlg
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
            this.ExportBTN = new System.Windows.Forms.Button();
            this.DetailsBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.ApplicationNameLB = new System.Windows.Forms.Label();
            this.ApplicationNameTB = new System.Windows.Forms.TextBox();
            this.ApplicationUriLB = new System.Windows.Forms.Label();
            this.ApplicationUriTB = new System.Windows.Forms.TextBox();
            this.SubjectNameLB = new System.Windows.Forms.Label();
            this.SubjectNameTB = new System.Windows.Forms.TextBox();
            this.DomainsLB = new System.Windows.Forms.Label();
            this.DomainsTB = new System.Windows.Forms.TextBox();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ValidToTB = new System.Windows.Forms.TextBox();
            this.ValidToLB = new System.Windows.Forms.Label();
            this.ValidFromTB = new System.Windows.Forms.TextBox();
            this.ValidFromLB = new System.Windows.Forms.Label();
            this.ThumbprintTB = new System.Windows.Forms.TextBox();
            this.ThumbprintLB = new System.Windows.Forms.Label();
            this.IssuerNameTB = new System.Windows.Forms.TextBox();
            this.IssuerNameLB = new System.Windows.Forms.Label();
            this.OrganizationTB = new System.Windows.Forms.TextBox();
            this.OrganizationLB = new System.Windows.Forms.Label();
            this.CertificateStoreCTRL = new Opc.Ua.Client.Controls.CertificateStoreCtrl();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.ExportBTN);
            this.ButtonsPN.Controls.Add(this.DetailsBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 291);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(708, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // ExportBTN
            // 
            this.ExportBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.ExportBTN.Location = new System.Drawing.Point(166, 4);
            this.ExportBTN.Name = "ExportBTN";
            this.ExportBTN.Size = new System.Drawing.Size(75, 23);
            this.ExportBTN.TabIndex = 3;
            this.ExportBTN.Text = "Export...";
            this.ExportBTN.UseVisualStyleBackColor = true;
            this.ExportBTN.Click += new System.EventHandler(this.ExportBTN_Click);
            // 
            // DetailsBTN
            // 
            this.DetailsBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.DetailsBTN.Location = new System.Drawing.Point(85, 4);
            this.DetailsBTN.Name = "DetailsBTN";
            this.DetailsBTN.Size = new System.Drawing.Size(75, 23);
            this.DetailsBTN.TabIndex = 2;
            this.DetailsBTN.Text = "Details...";
            this.DetailsBTN.UseVisualStyleBackColor = true;
            this.DetailsBTN.Click += new System.EventHandler(this.DetailsBTN_Click);
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
            this.CancelBTN.Location = new System.Drawing.Point(629, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // ApplicationNameLB
            // 
            this.ApplicationNameLB.AutoSize = true;
            this.ApplicationNameLB.Location = new System.Drawing.Point(5, 63);
            this.ApplicationNameLB.Name = "ApplicationNameLB";
            this.ApplicationNameLB.Size = new System.Drawing.Size(90, 13);
            this.ApplicationNameLB.TabIndex = 1;
            this.ApplicationNameLB.Text = "Application Name";
            this.ApplicationNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameTB
            // 
            this.ApplicationNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplicationNameTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ApplicationNameTB.Location = new System.Drawing.Point(96, 60);
            this.ApplicationNameTB.Name = "ApplicationNameTB";
            this.ApplicationNameTB.ReadOnly = true;
            this.ApplicationNameTB.Size = new System.Drawing.Size(608, 20);
            this.ApplicationNameTB.TabIndex = 2;
            // 
            // ApplicationUriLB
            // 
            this.ApplicationUriLB.AutoSize = true;
            this.ApplicationUriLB.Location = new System.Drawing.Point(5, 115);
            this.ApplicationUriLB.Name = "ApplicationUriLB";
            this.ApplicationUriLB.Size = new System.Drawing.Size(81, 13);
            this.ApplicationUriLB.TabIndex = 5;
            this.ApplicationUriLB.Text = "Application URI";
            this.ApplicationUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriTB
            // 
            this.ApplicationUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplicationUriTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ApplicationUriTB.Location = new System.Drawing.Point(96, 112);
            this.ApplicationUriTB.Name = "ApplicationUriTB";
            this.ApplicationUriTB.ReadOnly = true;
            this.ApplicationUriTB.Size = new System.Drawing.Size(608, 20);
            this.ApplicationUriTB.TabIndex = 6;
            // 
            // SubjectNameLB
            // 
            this.SubjectNameLB.AutoSize = true;
            this.SubjectNameLB.Location = new System.Drawing.Point(5, 167);
            this.SubjectNameLB.Name = "SubjectNameLB";
            this.SubjectNameLB.Size = new System.Drawing.Size(74, 13);
            this.SubjectNameLB.TabIndex = 9;
            this.SubjectNameLB.Text = "Subject Name";
            this.SubjectNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SubjectNameTB
            // 
            this.SubjectNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SubjectNameTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.SubjectNameTB.Location = new System.Drawing.Point(96, 164);
            this.SubjectNameTB.Name = "SubjectNameTB";
            this.SubjectNameTB.ReadOnly = true;
            this.SubjectNameTB.Size = new System.Drawing.Size(608, 20);
            this.SubjectNameTB.TabIndex = 10;
            // 
            // DomainsLB
            // 
            this.DomainsLB.AutoSize = true;
            this.DomainsLB.Location = new System.Drawing.Point(5, 141);
            this.DomainsLB.Name = "DomainsLB";
            this.DomainsLB.Size = new System.Drawing.Size(48, 13);
            this.DomainsLB.TabIndex = 7;
            this.DomainsLB.Text = "Domains";
            this.DomainsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DomainsTB
            // 
            this.DomainsTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DomainsTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DomainsTB.Location = new System.Drawing.Point(96, 138);
            this.DomainsTB.Name = "DomainsTB";
            this.DomainsTB.ReadOnly = true;
            this.DomainsTB.Size = new System.Drawing.Size(608, 20);
            this.DomainsTB.TabIndex = 8;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.ValidToTB);
            this.MainPN.Controls.Add(this.ValidToLB);
            this.MainPN.Controls.Add(this.ValidFromTB);
            this.MainPN.Controls.Add(this.ValidFromLB);
            this.MainPN.Controls.Add(this.ThumbprintTB);
            this.MainPN.Controls.Add(this.ThumbprintLB);
            this.MainPN.Controls.Add(this.IssuerNameTB);
            this.MainPN.Controls.Add(this.IssuerNameLB);
            this.MainPN.Controls.Add(this.OrganizationTB);
            this.MainPN.Controls.Add(this.OrganizationLB);
            this.MainPN.Controls.Add(this.CertificateStoreCTRL);
            this.MainPN.Controls.Add(this.DomainsTB);
            this.MainPN.Controls.Add(this.DomainsLB);
            this.MainPN.Controls.Add(this.SubjectNameTB);
            this.MainPN.Controls.Add(this.SubjectNameLB);
            this.MainPN.Controls.Add(this.ApplicationUriTB);
            this.MainPN.Controls.Add(this.ApplicationUriLB);
            this.MainPN.Controls.Add(this.ApplicationNameTB);
            this.MainPN.Controls.Add(this.ApplicationNameLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(708, 291);
            this.MainPN.TabIndex = 1;
            // 
            // ValidToTB
            // 
            this.ValidToTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ValidToTB.Location = new System.Drawing.Point(96, 242);
            this.ValidToTB.Name = "ValidToTB";
            this.ValidToTB.ReadOnly = true;
            this.ValidToTB.Size = new System.Drawing.Size(156, 20);
            this.ValidToTB.TabIndex = 16;
            // 
            // ValidToLB
            // 
            this.ValidToLB.AutoSize = true;
            this.ValidToLB.Location = new System.Drawing.Point(5, 245);
            this.ValidToLB.Name = "ValidToLB";
            this.ValidToLB.Size = new System.Drawing.Size(46, 13);
            this.ValidToLB.TabIndex = 15;
            this.ValidToLB.Text = "Valid To";
            this.ValidToLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ValidFromTB
            // 
            this.ValidFromTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ValidFromTB.Location = new System.Drawing.Point(96, 216);
            this.ValidFromTB.Name = "ValidFromTB";
            this.ValidFromTB.ReadOnly = true;
            this.ValidFromTB.Size = new System.Drawing.Size(156, 20);
            this.ValidFromTB.TabIndex = 14;
            // 
            // ValidFromLB
            // 
            this.ValidFromLB.AutoSize = true;
            this.ValidFromLB.Location = new System.Drawing.Point(5, 219);
            this.ValidFromLB.Name = "ValidFromLB";
            this.ValidFromLB.Size = new System.Drawing.Size(56, 13);
            this.ValidFromLB.TabIndex = 13;
            this.ValidFromLB.Text = "Valid From";
            this.ValidFromLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ThumbprintTB
            // 
            this.ThumbprintTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ThumbprintTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ThumbprintTB.Location = new System.Drawing.Point(96, 268);
            this.ThumbprintTB.Name = "ThumbprintTB";
            this.ThumbprintTB.ReadOnly = true;
            this.ThumbprintTB.Size = new System.Drawing.Size(609, 20);
            this.ThumbprintTB.TabIndex = 18;
            // 
            // ThumbprintLB
            // 
            this.ThumbprintLB.AutoSize = true;
            this.ThumbprintLB.Location = new System.Drawing.Point(5, 271);
            this.ThumbprintLB.Name = "ThumbprintLB";
            this.ThumbprintLB.Size = new System.Drawing.Size(60, 13);
            this.ThumbprintLB.TabIndex = 17;
            this.ThumbprintLB.Text = "Thumbprint";
            this.ThumbprintLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IssuerNameTB
            // 
            this.IssuerNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IssuerNameTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.IssuerNameTB.Location = new System.Drawing.Point(96, 190);
            this.IssuerNameTB.Name = "IssuerNameTB";
            this.IssuerNameTB.ReadOnly = true;
            this.IssuerNameTB.Size = new System.Drawing.Size(608, 20);
            this.IssuerNameTB.TabIndex = 12;
            // 
            // IssuerNameLB
            // 
            this.IssuerNameLB.AutoSize = true;
            this.IssuerNameLB.Location = new System.Drawing.Point(5, 193);
            this.IssuerNameLB.Name = "IssuerNameLB";
            this.IssuerNameLB.Size = new System.Drawing.Size(66, 13);
            this.IssuerNameLB.TabIndex = 11;
            this.IssuerNameLB.Text = "Issuer Name";
            this.IssuerNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // OrganizationTB
            // 
            this.OrganizationTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.OrganizationTB.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.OrganizationTB.Location = new System.Drawing.Point(96, 86);
            this.OrganizationTB.Name = "OrganizationTB";
            this.OrganizationTB.ReadOnly = true;
            this.OrganizationTB.Size = new System.Drawing.Size(608, 20);
            this.OrganizationTB.TabIndex = 4;
            // 
            // OrganizationLB
            // 
            this.OrganizationLB.AutoSize = true;
            this.OrganizationLB.Location = new System.Drawing.Point(5, 89);
            this.OrganizationLB.Name = "OrganizationLB";
            this.OrganizationLB.Size = new System.Drawing.Size(66, 13);
            this.OrganizationLB.TabIndex = 3;
            this.OrganizationLB.Text = "Organization";
            this.OrganizationLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CertificateStoreCTRL
            // 
            this.CertificateStoreCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificateStoreCTRL.LabelWidth = 91;
            this.CertificateStoreCTRL.Location = new System.Drawing.Point(4, 6);
            this.CertificateStoreCTRL.MinimumSize = new System.Drawing.Size(300, 51);
            this.CertificateStoreCTRL.Name = "CertificateStoreCTRL";
            this.CertificateStoreCTRL.Size = new System.Drawing.Size(699, 51);
            this.CertificateStoreCTRL.StorePath = "X:\\OPC\\Source\\UA311\\Source\\Utilities\\CertificateGenerator";
            this.CertificateStoreCTRL.TabIndex = 0;
            // 
            // ViewCertificateDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(708, 322);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "ViewCertificateDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "View Certificate";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Label ApplicationNameLB;
        private System.Windows.Forms.TextBox ApplicationNameTB;
        private System.Windows.Forms.Label ApplicationUriLB;
        private System.Windows.Forms.TextBox ApplicationUriTB;
        private System.Windows.Forms.Label SubjectNameLB;
        private System.Windows.Forms.TextBox SubjectNameTB;
        private System.Windows.Forms.Label DomainsLB;
        private System.Windows.Forms.TextBox DomainsTB;
        private System.Windows.Forms.Panel MainPN;
        private CertificateStoreCtrl CertificateStoreCTRL;
        private System.Windows.Forms.TextBox OrganizationTB;
        private System.Windows.Forms.Label OrganizationLB;
        private System.Windows.Forms.Label ValidFromLB;
        private System.Windows.Forms.TextBox ThumbprintTB;
        private System.Windows.Forms.Label ThumbprintLB;
        private System.Windows.Forms.TextBox IssuerNameTB;
        private System.Windows.Forms.Label IssuerNameLB;
        private System.Windows.Forms.TextBox ValidToTB;
        private System.Windows.Forms.Label ValidToLB;
        private System.Windows.Forms.TextBox ValidFromTB;
        private System.Windows.Forms.Button DetailsBTN;
        private System.Windows.Forms.Button ExportBTN;
    }
}
