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
    partial class CertificateListDlg
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
            this.FilterBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.StoreGB = new System.Windows.Forms.GroupBox();
            this.CertificateStoreCTRL = new Opc.Ua.Client.Controls.CertificateStoreCtrl();
            this.FiltersGB = new System.Windows.Forms.GroupBox();
            this.PrivateKeyCK = new System.Windows.Forms.CheckBox();
            this.PrivateKeyLB = new System.Windows.Forms.Label();
            this.IssuerNameTB = new System.Windows.Forms.TextBox();
            this.DomainTB = new System.Windows.Forms.TextBox();
            this.SubjectNameTB = new System.Windows.Forms.TextBox();
            this.IssuedCK = new System.Windows.Forms.CheckBox();
            this.SelfSignedCK = new System.Windows.Forms.CheckBox();
            this.CertificateAuthorityCK = new System.Windows.Forms.CheckBox();
            this.ApplicationCK = new System.Windows.Forms.CheckBox();
            this.CertificateTypeLB = new System.Windows.Forms.Label();
            this.IssuerNameLB = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SubjectNameLB = new System.Windows.Forms.Label();
            this.CertificatesCTRL = new Opc.Ua.Client.Controls.CertificateListCtrl();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.StoreGB.SuspendLayout();
            this.FiltersGB.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 625);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(927, 31);
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
            this.CancelBTN.Location = new System.Drawing.Point(848, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // FilterBTN
            // 
            this.FilterBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.FilterBTN.Location = new System.Drawing.Point(834, 112);
            this.FilterBTN.Name = "FilterBTN";
            this.FilterBTN.Size = new System.Drawing.Size(75, 23);
            this.FilterBTN.TabIndex = 13;
            this.FilterBTN.Text = "Filter";
            this.FilterBTN.UseVisualStyleBackColor = true;
            this.FilterBTN.Click += new System.EventHandler(this.FilterBTN_Click);
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.StoreGB);
            this.MainPN.Controls.Add(this.FiltersGB);
            this.MainPN.Controls.Add(this.CertificatesCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.MainPN.Size = new System.Drawing.Size(927, 625);
            this.MainPN.TabIndex = 1;
            // 
            // StoreGB
            // 
            this.StoreGB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StoreGB.Controls.Add(this.CertificateStoreCTRL);
            this.StoreGB.Location = new System.Drawing.Point(6, 6);
            this.StoreGB.Name = "StoreGB";
            this.StoreGB.Size = new System.Drawing.Size(915, 73);
            this.StoreGB.TabIndex = 0;
            this.StoreGB.TabStop = false;
            this.StoreGB.Text = "Location";
            // 
            // CertificateStoreCTRL
            // 
            this.CertificateStoreCTRL.LabelWidth = 90;
            this.CertificateStoreCTRL.Location = new System.Drawing.Point(10, 19);
            this.CertificateStoreCTRL.MinimumSize = new System.Drawing.Size(300, 51);
            this.CertificateStoreCTRL.Name = "CertificateStoreCTRL";
            this.CertificateStoreCTRL.ReadOnly = true;
            this.CertificateStoreCTRL.Size = new System.Drawing.Size(899, 51);
            this.CertificateStoreCTRL.StorePath = "X:\\OPC\\Source\\UA311\\Source\\Utilities\\CertificateGenerator";
            this.CertificateStoreCTRL.TabIndex = 0;
            this.CertificateStoreCTRL.StoreChanged += new System.EventHandler(this.CertificateStoreCTRL_StoreChanged);
            // 
            // FiltersGB
            // 
            this.FiltersGB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.FiltersGB.Controls.Add(this.FilterBTN);
            this.FiltersGB.Controls.Add(this.PrivateKeyCK);
            this.FiltersGB.Controls.Add(this.PrivateKeyLB);
            this.FiltersGB.Controls.Add(this.IssuerNameTB);
            this.FiltersGB.Controls.Add(this.DomainTB);
            this.FiltersGB.Controls.Add(this.SubjectNameTB);
            this.FiltersGB.Controls.Add(this.IssuedCK);
            this.FiltersGB.Controls.Add(this.SelfSignedCK);
            this.FiltersGB.Controls.Add(this.CertificateAuthorityCK);
            this.FiltersGB.Controls.Add(this.ApplicationCK);
            this.FiltersGB.Controls.Add(this.CertificateTypeLB);
            this.FiltersGB.Controls.Add(this.IssuerNameLB);
            this.FiltersGB.Controls.Add(this.label2);
            this.FiltersGB.Controls.Add(this.SubjectNameLB);
            this.FiltersGB.Location = new System.Drawing.Point(6, 77);
            this.FiltersGB.Name = "FiltersGB";
            this.FiltersGB.Size = new System.Drawing.Size(915, 141);
            this.FiltersGB.TabIndex = 1;
            this.FiltersGB.TabStop = false;
            this.FiltersGB.Text = "Filters";
            // 
            // PrivateKeyCK
            // 
            this.PrivateKeyCK.AutoSize = true;
            this.PrivateKeyCK.Location = new System.Drawing.Point(100, 117);
            this.PrivateKeyCK.Name = "PrivateKeyCK";
            this.PrivateKeyCK.Size = new System.Drawing.Size(15, 14);
            this.PrivateKeyCK.TabIndex = 12;
            this.PrivateKeyCK.UseVisualStyleBackColor = true;
            // 
            // PrivateKeyLB
            // 
            this.PrivateKeyLB.AutoSize = true;
            this.PrivateKeyLB.Location = new System.Drawing.Point(7, 117);
            this.PrivateKeyLB.Name = "PrivateKeyLB";
            this.PrivateKeyLB.Size = new System.Drawing.Size(83, 13);
            this.PrivateKeyLB.TabIndex = 11;
            this.PrivateKeyLB.Text = "Has Private Key";
            // 
            // IssuerNameTB
            // 
            this.IssuerNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.IssuerNameTB.Location = new System.Drawing.Point(100, 66);
            this.IssuerNameTB.Name = "IssuerNameTB";
            this.IssuerNameTB.Size = new System.Drawing.Size(809, 20);
            this.IssuerNameTB.TabIndex = 5;
            // 
            // DomainTB
            // 
            this.DomainTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DomainTB.Location = new System.Drawing.Point(100, 42);
            this.DomainTB.Name = "DomainTB";
            this.DomainTB.Size = new System.Drawing.Size(809, 20);
            this.DomainTB.TabIndex = 3;
            // 
            // SubjectNameTB
            // 
            this.SubjectNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SubjectNameTB.Location = new System.Drawing.Point(100, 18);
            this.SubjectNameTB.Name = "SubjectNameTB";
            this.SubjectNameTB.Size = new System.Drawing.Size(809, 20);
            this.SubjectNameTB.TabIndex = 1;
            // 
            // IssuedCK
            // 
            this.IssuedCK.AutoSize = true;
            this.IssuedCK.Location = new System.Drawing.Point(391, 92);
            this.IssuedCK.Name = "IssuedCK";
            this.IssuedCK.Size = new System.Drawing.Size(88, 17);
            this.IssuedCK.TabIndex = 10;
            this.IssuedCK.Text = "Issued by CA";
            this.IssuedCK.UseVisualStyleBackColor = true;
            // 
            // SelfSignedCK
            // 
            this.SelfSignedCK.AutoSize = true;
            this.SelfSignedCK.Location = new System.Drawing.Point(307, 92);
            this.SelfSignedCK.Name = "SelfSignedCK";
            this.SelfSignedCK.Size = new System.Drawing.Size(78, 17);
            this.SelfSignedCK.TabIndex = 9;
            this.SelfSignedCK.Text = "Self-signed";
            this.SelfSignedCK.UseVisualStyleBackColor = true;
            // 
            // CertificateAuthorityCK
            // 
            this.CertificateAuthorityCK.AutoSize = true;
            this.CertificateAuthorityCK.Location = new System.Drawing.Point(184, 92);
            this.CertificateAuthorityCK.Name = "CertificateAuthorityCK";
            this.CertificateAuthorityCK.Size = new System.Drawing.Size(117, 17);
            this.CertificateAuthorityCK.TabIndex = 8;
            this.CertificateAuthorityCK.Text = "Certificate Authority";
            this.CertificateAuthorityCK.UseVisualStyleBackColor = true;
            // 
            // ApplicationCK
            // 
            this.ApplicationCK.AutoSize = true;
            this.ApplicationCK.Location = new System.Drawing.Point(100, 92);
            this.ApplicationCK.Name = "ApplicationCK";
            this.ApplicationCK.Size = new System.Drawing.Size(78, 17);
            this.ApplicationCK.TabIndex = 7;
            this.ApplicationCK.Text = "Application";
            this.ApplicationCK.UseVisualStyleBackColor = true;
            // 
            // CertificateTypeLB
            // 
            this.CertificateTypeLB.AutoSize = true;
            this.CertificateTypeLB.Location = new System.Drawing.Point(7, 93);
            this.CertificateTypeLB.Name = "CertificateTypeLB";
            this.CertificateTypeLB.Size = new System.Drawing.Size(81, 13);
            this.CertificateTypeLB.TabIndex = 6;
            this.CertificateTypeLB.Text = "Certificate Type";
            // 
            // IssuerNameLB
            // 
            this.IssuerNameLB.AutoSize = true;
            this.IssuerNameLB.Location = new System.Drawing.Point(7, 69);
            this.IssuerNameLB.Name = "IssuerNameLB";
            this.IssuerNameLB.Size = new System.Drawing.Size(66, 13);
            this.IssuerNameLB.TabIndex = 4;
            this.IssuerNameLB.Text = "Issuer Name";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 45);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(43, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Domain";
            // 
            // SubjectNameLB
            // 
            this.SubjectNameLB.AutoSize = true;
            this.SubjectNameLB.Location = new System.Drawing.Point(7, 21);
            this.SubjectNameLB.Name = "SubjectNameLB";
            this.SubjectNameLB.Size = new System.Drawing.Size(74, 13);
            this.SubjectNameLB.TabIndex = 0;
            this.SubjectNameLB.Text = "Subject Name";
            // 
            // CertificatesCTRL
            // 
            this.CertificatesCTRL.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificatesCTRL.Instructions = null;
            this.CertificatesCTRL.Location = new System.Drawing.Point(6, 224);
            this.CertificatesCTRL.Name = "CertificatesCTRL";
            this.CertificatesCTRL.Size = new System.Drawing.Size(915, 398);
            this.CertificatesCTRL.TabIndex = 2;
            this.CertificatesCTRL.ItemsSelected += new Opc.Ua.Client.Controls.ListItemActionEventHandler(this.CertificatesCTRL_ItemsSelected);
            // 
            // CertificateListDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(927, 656);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "CertificateListDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Manage Certificates in Certificate Store";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.StoreGB.ResumeLayout(false);
            this.FiltersGB.ResumeLayout(false);
            this.FiltersGB.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private CertificateListCtrl CertificatesCTRL;
        private System.Windows.Forms.Label SubjectNameLB;
        private System.Windows.Forms.GroupBox FiltersGB;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label CertificateTypeLB;
        private System.Windows.Forms.Label IssuerNameLB;
        private System.Windows.Forms.TextBox IssuerNameTB;
        private System.Windows.Forms.TextBox DomainTB;
        private System.Windows.Forms.TextBox SubjectNameTB;
        private System.Windows.Forms.CheckBox IssuedCK;
        private System.Windows.Forms.CheckBox SelfSignedCK;
        private System.Windows.Forms.CheckBox CertificateAuthorityCK;
        private System.Windows.Forms.CheckBox ApplicationCK;
        private System.Windows.Forms.Button FilterBTN;
        private System.Windows.Forms.CheckBox PrivateKeyCK;
        private System.Windows.Forms.Label PrivateKeyLB;
        private System.Windows.Forms.GroupBox StoreGB;
        private CertificateStoreCtrl CertificateStoreCTRL;
    }
}
