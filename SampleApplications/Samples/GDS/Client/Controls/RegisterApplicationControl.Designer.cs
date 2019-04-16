/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

namespace Opc.Ua.Gds.Client
{
    partial class RegisterApplicationControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.RegistrationPanel = new System.Windows.Forms.Panel();
            this.RegistrationDetailsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.HttpsCertificatePrivateKeyPathButton = new System.Windows.Forms.Button();
            this.HttpsCertificatePublicKeyPathButton = new System.Windows.Forms.Button();
            this.HttpsCertificatePrivateKeyPathTextBox = new System.Windows.Forms.TextBox();
            this.HttpsCertificatePublicKeyPathTextBox = new System.Windows.Forms.TextBox();
            this.HttpsCertificatePrivateKeyPathLabel = new System.Windows.Forms.Label();
            this.HttpsCertificatePublicKeyPathLabel = new System.Windows.Forms.Label();
            this.HttpsIssuerListStorePathButton = new System.Windows.Forms.Button();
            this.HttpsIssuerListStorePathTextBox = new System.Windows.Forms.TextBox();
            this.HttpsIssuerListStorePathLabel = new System.Windows.Forms.Label();
            this.HttpsTrustListStorePathButton = new System.Windows.Forms.Button();
            this.HttpsTrustListStorePathTextBox = new System.Windows.Forms.TextBox();
            this.HttpsTrustListStorePathLabel = new System.Windows.Forms.Label();
            this.DiscoveryUrlsButton = new System.Windows.Forms.Button();
            this.DiscoveryUrlsTextBox = new System.Windows.Forms.Label();
            this.DiscoveryUrlsLabel = new System.Windows.Forms.Label();
            this.ServerCapabilitiesTextBox = new System.Windows.Forms.Label();
            this.ServerCapabilitiesButton = new System.Windows.Forms.Button();
            this.ServerCapabilitiesLabel = new System.Windows.Forms.Label();
            this.ApplicationIdTextBox = new System.Windows.Forms.Label();
            this.ApplicationIdLabel = new System.Windows.Forms.Label();
            this.IssuerListStorePathButton = new System.Windows.Forms.Button();
            this.CertificateSubjectNameTextBox = new System.Windows.Forms.TextBox();
            this.CertificateSubjectNameLabel = new System.Windows.Forms.Label();
            this.IssuerListStorePathTextBox = new System.Windows.Forms.TextBox();
            this.IssuerListStorePathLabel = new System.Windows.Forms.Label();
            this.TrustListStorePathTextBox = new System.Windows.Forms.TextBox();
            this.TrustListStorePathLabel = new System.Windows.Forms.Label();
            this.ProductUriTextBox = new System.Windows.Forms.TextBox();
            this.ProductUriLabel = new System.Windows.Forms.Label();
            this.ApplicationUriTextBox = new System.Windows.Forms.TextBox();
            this.ApplicationUriLabel = new System.Windows.Forms.Label();
            this.ApplicationNameTextBox = new System.Windows.Forms.TextBox();
            this.ApplicationNameLabel = new System.Windows.Forms.Label();
            this.CertificatePrivateKeyPathTextBox = new System.Windows.Forms.TextBox();
            this.CertificatePrivateKeyPathLabel = new System.Windows.Forms.Label();
            this.CertificatePublicKeyPathTextBox = new System.Windows.Forms.TextBox();
            this.CertificatePublicKeyPathLabel = new System.Windows.Forms.Label();
            this.CertificateStorePathTextBox = new System.Windows.Forms.TextBox();
            this.CertificateStorePathLabel = new System.Windows.Forms.Label();
            this.ConfigurationFileTextBox = new System.Windows.Forms.TextBox();
            this.ConfigurationFileLabel = new System.Windows.Forms.Label();
            this.RegistrationTypeLabel = new System.Windows.Forms.Label();
            this.RegistrationTypeComboBox = new System.Windows.Forms.ComboBox();
            this.CertificateStorePathButton = new System.Windows.Forms.Button();
            this.ConfigurationFileButton = new System.Windows.Forms.Button();
            this.CertificatePrivateKeyPathButton = new System.Windows.Forms.Button();
            this.CertificatePublicKeyPathButton = new System.Windows.Forms.Button();
            this.TrustListStorePathButton = new System.Windows.Forms.Button();
            this.RegistrationButtonsPanel = new System.Windows.Forms.Panel();
            this.PickServerButton = new System.Windows.Forms.Button();
            this.ClearButton = new System.Windows.Forms.Button();
            this.OpenConfigurationButton = new System.Windows.Forms.Button();
            this.LoadButton = new System.Windows.Forms.Button();
            this.SaveButton = new System.Windows.Forms.Button();
            this.UnregisterApplicationButton = new System.Windows.Forms.Button();
            this.RegisterApplicationButton = new System.Windows.Forms.Button();
            this.ApplyChangesButton = new System.Windows.Forms.Button();
            this.ToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.DomainsLabel = new System.Windows.Forms.Label();
            this.DomainsTextBox = new System.Windows.Forms.TextBox();
            this.RegistrationPanel.SuspendLayout();
            this.RegistrationDetailsPanel.SuspendLayout();
            this.RegistrationButtonsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // RegistrationPanel
            // 
            this.RegistrationPanel.Controls.Add(this.RegistrationDetailsPanel);
            this.RegistrationPanel.Controls.Add(this.RegistrationButtonsPanel);
            this.RegistrationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationPanel.Location = new System.Drawing.Point(0, 0);
            this.RegistrationPanel.Margin = new System.Windows.Forms.Padding(4);
            this.RegistrationPanel.Name = "RegistrationPanel";
            this.RegistrationPanel.Size = new System.Drawing.Size(1172, 633);
            this.RegistrationPanel.TabIndex = 50;
            // 
            // RegistrationDetailsPanel
            // 
            this.RegistrationDetailsPanel.ColumnCount = 3;
            this.RegistrationDetailsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.RegistrationDetailsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.RegistrationDetailsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.RegistrationDetailsPanel.Controls.Add(this.DomainsTextBox, 1, 22);
            this.RegistrationDetailsPanel.Controls.Add(this.DomainsLabel, 0, 22);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePrivateKeyPathButton, 2, 19);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePublicKeyPathButton, 2, 18);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePrivateKeyPathTextBox, 1, 19);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePublicKeyPathTextBox, 1, 18);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePrivateKeyPathLabel, 0, 19);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsCertificatePublicKeyPathLabel, 0, 18);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsIssuerListStorePathButton, 2, 21);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsIssuerListStorePathTextBox, 1, 21);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsIssuerListStorePathLabel, 0, 21);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsTrustListStorePathButton, 2, 20);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsTrustListStorePathTextBox, 1, 20);
            this.RegistrationDetailsPanel.Controls.Add(this.HttpsTrustListStorePathLabel, 0, 20);
            this.RegistrationDetailsPanel.Controls.Add(this.DiscoveryUrlsButton, 2, 5);
            this.RegistrationDetailsPanel.Controls.Add(this.DiscoveryUrlsTextBox, 1, 5);
            this.RegistrationDetailsPanel.Controls.Add(this.DiscoveryUrlsLabel, 0, 5);
            this.RegistrationDetailsPanel.Controls.Add(this.ServerCapabilitiesTextBox, 1, 6);
            this.RegistrationDetailsPanel.Controls.Add(this.ServerCapabilitiesButton, 2, 6);
            this.RegistrationDetailsPanel.Controls.Add(this.ServerCapabilitiesLabel, 0, 6);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationIdTextBox, 1, 1);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationIdLabel, 0, 1);
            this.RegistrationDetailsPanel.Controls.Add(this.IssuerListStorePathButton, 2, 15);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificateSubjectNameTextBox, 1, 10);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificateSubjectNameLabel, 0, 10);
            this.RegistrationDetailsPanel.Controls.Add(this.IssuerListStorePathTextBox, 1, 15);
            this.RegistrationDetailsPanel.Controls.Add(this.IssuerListStorePathLabel, 0, 15);
            this.RegistrationDetailsPanel.Controls.Add(this.TrustListStorePathTextBox, 1, 14);
            this.RegistrationDetailsPanel.Controls.Add(this.TrustListStorePathLabel, 0, 14);
            this.RegistrationDetailsPanel.Controls.Add(this.ProductUriTextBox, 1, 4);
            this.RegistrationDetailsPanel.Controls.Add(this.ProductUriLabel, 0, 4);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationUriTextBox, 1, 3);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationUriLabel, 0, 3);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationNameTextBox, 1, 2);
            this.RegistrationDetailsPanel.Controls.Add(this.ApplicationNameLabel, 0, 2);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePrivateKeyPathTextBox, 1, 12);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePrivateKeyPathLabel, 0, 12);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePublicKeyPathTextBox, 1, 11);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePublicKeyPathLabel, 0, 11);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificateStorePathTextBox, 1, 9);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificateStorePathLabel, 0, 9);
            this.RegistrationDetailsPanel.Controls.Add(this.ConfigurationFileTextBox, 1, 7);
            this.RegistrationDetailsPanel.Controls.Add(this.ConfigurationFileLabel, 0, 7);
            this.RegistrationDetailsPanel.Controls.Add(this.RegistrationTypeLabel, 0, 0);
            this.RegistrationDetailsPanel.Controls.Add(this.RegistrationTypeComboBox, 1, 0);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificateStorePathButton, 2, 9);
            this.RegistrationDetailsPanel.Controls.Add(this.ConfigurationFileButton, 2, 7);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePrivateKeyPathButton, 2, 12);
            this.RegistrationDetailsPanel.Controls.Add(this.CertificatePublicKeyPathButton, 2, 11);
            this.RegistrationDetailsPanel.Controls.Add(this.TrustListStorePathButton, 2, 14);
            this.RegistrationDetailsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationDetailsPanel.Location = new System.Drawing.Point(0, 0);
            this.RegistrationDetailsPanel.Margin = new System.Windows.Forms.Padding(4);
            this.RegistrationDetailsPanel.Name = "RegistrationDetailsPanel";
            this.RegistrationDetailsPanel.Padding = new System.Windows.Forms.Padding(4, 0, 4, 4);
            this.RegistrationDetailsPanel.RowCount = 24;
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.RegistrationDetailsPanel.Size = new System.Drawing.Size(1172, 594);
            this.RegistrationDetailsPanel.TabIndex = 0;
            // 
            // HttpsCertificatePrivateKeyPathButton
            // 
            this.HttpsCertificatePrivateKeyPathButton.Location = new System.Drawing.Point(1136, 423);
            this.HttpsCertificatePrivateKeyPathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.HttpsCertificatePrivateKeyPathButton.Name = "HttpsCertificatePrivateKeyPathButton";
            this.HttpsCertificatePrivateKeyPathButton.Size = new System.Drawing.Size(32, 25);
            this.HttpsCertificatePrivateKeyPathButton.TabIndex = 58;
            this.HttpsCertificatePrivateKeyPathButton.Text = "...";
            this.HttpsCertificatePrivateKeyPathButton.UseVisualStyleBackColor = true;
            this.HttpsCertificatePrivateKeyPathButton.Click += new System.EventHandler(this.HttpsCertificatePrivateKeyPathButton_Click);
            // 
            // HttpsCertificatePublicKeyPathButton
            // 
            this.HttpsCertificatePublicKeyPathButton.Location = new System.Drawing.Point(1136, 394);
            this.HttpsCertificatePublicKeyPathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.HttpsCertificatePublicKeyPathButton.Name = "HttpsCertificatePublicKeyPathButton";
            this.HttpsCertificatePublicKeyPathButton.Size = new System.Drawing.Size(32, 25);
            this.HttpsCertificatePublicKeyPathButton.TabIndex = 57;
            this.HttpsCertificatePublicKeyPathButton.Text = "...";
            this.HttpsCertificatePublicKeyPathButton.UseVisualStyleBackColor = true;
            this.HttpsCertificatePublicKeyPathButton.Click += new System.EventHandler(this.HttpsCertificatePublicKeyPathButton_Click);
            // 
            // HttpsCertificatePrivateKeyPathTextBox
            // 
            this.HttpsCertificatePrivateKeyPathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsCertificatePrivateKeyPathTextBox.Location = new System.Drawing.Point(243, 423);
            this.HttpsCertificatePrivateKeyPathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsCertificatePrivateKeyPathTextBox.Name = "HttpsCertificatePrivateKeyPathTextBox";
            this.HttpsCertificatePrivateKeyPathTextBox.Size = new System.Drawing.Size(890, 22);
            this.HttpsCertificatePrivateKeyPathTextBox.TabIndex = 56;
            this.HttpsCertificatePrivateKeyPathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // HttpsCertificatePublicKeyPathTextBox
            // 
            this.HttpsCertificatePublicKeyPathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsCertificatePublicKeyPathTextBox.Location = new System.Drawing.Point(243, 394);
            this.HttpsCertificatePublicKeyPathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsCertificatePublicKeyPathTextBox.Name = "HttpsCertificatePublicKeyPathTextBox";
            this.HttpsCertificatePublicKeyPathTextBox.Size = new System.Drawing.Size(890, 22);
            this.HttpsCertificatePublicKeyPathTextBox.TabIndex = 55;
            this.HttpsCertificatePublicKeyPathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // HttpsCertificatePrivateKeyPathLabel
            // 
            this.HttpsCertificatePrivateKeyPathLabel.AllowDrop = true;
            this.HttpsCertificatePrivateKeyPathLabel.AutoSize = true;
            this.HttpsCertificatePrivateKeyPathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsCertificatePrivateKeyPathLabel.Location = new System.Drawing.Point(7, 423);
            this.HttpsCertificatePrivateKeyPathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsCertificatePrivateKeyPathLabel.Name = "HttpsCertificatePrivateKeyPathLabel";
            this.HttpsCertificatePrivateKeyPathLabel.Size = new System.Drawing.Size(230, 25);
            this.HttpsCertificatePrivateKeyPathLabel.TabIndex = 53;
            this.HttpsCertificatePrivateKeyPathLabel.Text = "HTTPS Certificate Private Key Path";
            this.HttpsCertificatePrivateKeyPathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // HttpsCertificatePublicKeyPathLabel
            // 
            this.HttpsCertificatePublicKeyPathLabel.AllowDrop = true;
            this.HttpsCertificatePublicKeyPathLabel.AutoSize = true;
            this.HttpsCertificatePublicKeyPathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsCertificatePublicKeyPathLabel.Location = new System.Drawing.Point(7, 394);
            this.HttpsCertificatePublicKeyPathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsCertificatePublicKeyPathLabel.Name = "HttpsCertificatePublicKeyPathLabel";
            this.HttpsCertificatePublicKeyPathLabel.Size = new System.Drawing.Size(230, 25);
            this.HttpsCertificatePublicKeyPathLabel.TabIndex = 52;
            this.HttpsCertificatePublicKeyPathLabel.Text = "HTTPS Certificate Public Key Path";
            this.HttpsCertificatePublicKeyPathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // HttpsIssuerListStorePathButton
            // 
            this.HttpsIssuerListStorePathButton.Location = new System.Drawing.Point(1136, 481);
            this.HttpsIssuerListStorePathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.HttpsIssuerListStorePathButton.Name = "HttpsIssuerListStorePathButton";
            this.HttpsIssuerListStorePathButton.Size = new System.Drawing.Size(32, 25);
            this.HttpsIssuerListStorePathButton.TabIndex = 50;
            this.HttpsIssuerListStorePathButton.Text = "...";
            this.HttpsIssuerListStorePathButton.UseVisualStyleBackColor = true;
            this.HttpsIssuerListStorePathButton.Click += new System.EventHandler(this.HttpsIssuerListStorePathButton_Click);
            // 
            // HttpsIssuerListStorePathTextBox
            // 
            this.HttpsIssuerListStorePathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsIssuerListStorePathTextBox.Location = new System.Drawing.Point(243, 481);
            this.HttpsIssuerListStorePathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsIssuerListStorePathTextBox.Name = "HttpsIssuerListStorePathTextBox";
            this.HttpsIssuerListStorePathTextBox.Size = new System.Drawing.Size(890, 22);
            this.HttpsIssuerListStorePathTextBox.TabIndex = 49;
            this.HttpsIssuerListStorePathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // HttpsIssuerListStorePathLabel
            // 
            this.HttpsIssuerListStorePathLabel.AllowDrop = true;
            this.HttpsIssuerListStorePathLabel.AutoSize = true;
            this.HttpsIssuerListStorePathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsIssuerListStorePathLabel.Location = new System.Drawing.Point(7, 481);
            this.HttpsIssuerListStorePathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsIssuerListStorePathLabel.Name = "HttpsIssuerListStorePathLabel";
            this.HttpsIssuerListStorePathLabel.Size = new System.Drawing.Size(230, 25);
            this.HttpsIssuerListStorePathLabel.TabIndex = 48;
            this.HttpsIssuerListStorePathLabel.Text = "HTTPS Issuer List Store Path";
            this.HttpsIssuerListStorePathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // HttpsTrustListStorePathButton
            // 
            this.HttpsTrustListStorePathButton.Location = new System.Drawing.Point(1136, 452);
            this.HttpsTrustListStorePathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.HttpsTrustListStorePathButton.Name = "HttpsTrustListStorePathButton";
            this.HttpsTrustListStorePathButton.Size = new System.Drawing.Size(32, 25);
            this.HttpsTrustListStorePathButton.TabIndex = 47;
            this.HttpsTrustListStorePathButton.Text = "...";
            this.HttpsTrustListStorePathButton.UseVisualStyleBackColor = true;
            this.HttpsTrustListStorePathButton.Click += new System.EventHandler(this.HttpsTrustListStorePathButton_Click);
            // 
            // HttpsTrustListStorePathTextBox
            // 
            this.HttpsTrustListStorePathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsTrustListStorePathTextBox.Location = new System.Drawing.Point(243, 452);
            this.HttpsTrustListStorePathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsTrustListStorePathTextBox.Name = "HttpsTrustListStorePathTextBox";
            this.HttpsTrustListStorePathTextBox.Size = new System.Drawing.Size(890, 22);
            this.HttpsTrustListStorePathTextBox.TabIndex = 46;
            this.HttpsTrustListStorePathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // HttpsTrustListStorePathLabel
            // 
            this.HttpsTrustListStorePathLabel.AllowDrop = true;
            this.HttpsTrustListStorePathLabel.AutoSize = true;
            this.HttpsTrustListStorePathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HttpsTrustListStorePathLabel.Location = new System.Drawing.Point(7, 452);
            this.HttpsTrustListStorePathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.HttpsTrustListStorePathLabel.Name = "HttpsTrustListStorePathLabel";
            this.HttpsTrustListStorePathLabel.Size = new System.Drawing.Size(230, 25);
            this.HttpsTrustListStorePathLabel.TabIndex = 45;
            this.HttpsTrustListStorePathLabel.Text = "HTTPS Trust List Store Path";
            this.HttpsTrustListStorePathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DiscoveryUrlsButton
            // 
            this.DiscoveryUrlsButton.Location = new System.Drawing.Point(1136, 136);
            this.DiscoveryUrlsButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.DiscoveryUrlsButton.Name = "DiscoveryUrlsButton";
            this.DiscoveryUrlsButton.Size = new System.Drawing.Size(32, 25);
            this.DiscoveryUrlsButton.TabIndex = 44;
            this.DiscoveryUrlsButton.Text = "...";
            this.DiscoveryUrlsButton.UseVisualStyleBackColor = true;
            this.DiscoveryUrlsButton.Click += new System.EventHandler(this.DiscoveryUrlsButton_Click);
            // 
            // DiscoveryUrlsTextBox
            // 
            this.DiscoveryUrlsTextBox.AllowDrop = true;
            this.DiscoveryUrlsTextBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.DiscoveryUrlsTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryUrlsTextBox.Location = new System.Drawing.Point(243, 136);
            this.DiscoveryUrlsTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.DiscoveryUrlsTextBox.Name = "DiscoveryUrlsTextBox";
            this.DiscoveryUrlsTextBox.Size = new System.Drawing.Size(890, 25);
            this.DiscoveryUrlsTextBox.TabIndex = 43;
            this.DiscoveryUrlsTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.DiscoveryUrlsTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // DiscoveryUrlsLabel
            // 
            this.DiscoveryUrlsLabel.AllowDrop = true;
            this.DiscoveryUrlsLabel.AutoSize = true;
            this.DiscoveryUrlsLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryUrlsLabel.Location = new System.Drawing.Point(7, 136);
            this.DiscoveryUrlsLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.DiscoveryUrlsLabel.Name = "DiscoveryUrlsLabel";
            this.DiscoveryUrlsLabel.Size = new System.Drawing.Size(230, 25);
            this.DiscoveryUrlsLabel.TabIndex = 42;
            this.DiscoveryUrlsLabel.Text = "Discovery URLs";
            this.DiscoveryUrlsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerCapabilitiesTextBox
            // 
            this.ServerCapabilitiesTextBox.AllowDrop = true;
            this.ServerCapabilitiesTextBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.ServerCapabilitiesTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerCapabilitiesTextBox.Location = new System.Drawing.Point(243, 165);
            this.ServerCapabilitiesTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ServerCapabilitiesTextBox.Name = "ServerCapabilitiesTextBox";
            this.ServerCapabilitiesTextBox.Size = new System.Drawing.Size(890, 25);
            this.ServerCapabilitiesTextBox.TabIndex = 38;
            this.ServerCapabilitiesTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.ServerCapabilitiesTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // ServerCapabilitiesButton
            // 
            this.ServerCapabilitiesButton.Location = new System.Drawing.Point(1136, 165);
            this.ServerCapabilitiesButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.ServerCapabilitiesButton.Name = "ServerCapabilitiesButton";
            this.ServerCapabilitiesButton.Size = new System.Drawing.Size(32, 25);
            this.ServerCapabilitiesButton.TabIndex = 37;
            this.ServerCapabilitiesButton.Text = "...";
            this.ServerCapabilitiesButton.UseVisualStyleBackColor = true;
            this.ServerCapabilitiesButton.Click += new System.EventHandler(this.ServerCapabilitiesButton_Click);
            // 
            // ServerCapabilitiesLabel
            // 
            this.ServerCapabilitiesLabel.AllowDrop = true;
            this.ServerCapabilitiesLabel.AutoSize = true;
            this.ServerCapabilitiesLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerCapabilitiesLabel.Location = new System.Drawing.Point(7, 165);
            this.ServerCapabilitiesLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ServerCapabilitiesLabel.Name = "ServerCapabilitiesLabel";
            this.ServerCapabilitiesLabel.Size = new System.Drawing.Size(230, 25);
            this.ServerCapabilitiesLabel.TabIndex = 36;
            this.ServerCapabilitiesLabel.Text = "Server Capabilities";
            this.ServerCapabilitiesLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationIdTextBox
            // 
            this.ApplicationIdTextBox.AllowDrop = true;
            this.ApplicationIdTextBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.ApplicationIdTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationIdTextBox.Location = new System.Drawing.Point(243, 29);
            this.ApplicationIdTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationIdTextBox.Name = "ApplicationIdTextBox";
            this.ApplicationIdTextBox.Size = new System.Drawing.Size(890, 25);
            this.ApplicationIdTextBox.TabIndex = 34;
            this.ApplicationIdTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationIdLabel
            // 
            this.ApplicationIdLabel.AllowDrop = true;
            this.ApplicationIdLabel.AutoSize = true;
            this.ApplicationIdLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationIdLabel.Location = new System.Drawing.Point(7, 29);
            this.ApplicationIdLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationIdLabel.Name = "ApplicationIdLabel";
            this.ApplicationIdLabel.Size = new System.Drawing.Size(230, 25);
            this.ApplicationIdLabel.TabIndex = 33;
            this.ApplicationIdLabel.Text = "Application ID";
            this.ApplicationIdLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IssuerListStorePathButton
            // 
            this.IssuerListStorePathButton.Location = new System.Drawing.Point(1136, 365);
            this.IssuerListStorePathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.IssuerListStorePathButton.Name = "IssuerListStorePathButton";
            this.IssuerListStorePathButton.Size = new System.Drawing.Size(32, 25);
            this.IssuerListStorePathButton.TabIndex = 31;
            this.IssuerListStorePathButton.Text = "...";
            this.IssuerListStorePathButton.UseVisualStyleBackColor = true;
            this.IssuerListStorePathButton.Click += new System.EventHandler(this.IssuerListStorePathButton_Click);
            // 
            // CertificateSubjectNameTextBox
            // 
            this.CertificateSubjectNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateSubjectNameTextBox.Location = new System.Drawing.Point(243, 252);
            this.CertificateSubjectNameTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificateSubjectNameTextBox.Name = "CertificateSubjectNameTextBox";
            this.CertificateSubjectNameTextBox.Size = new System.Drawing.Size(890, 22);
            this.CertificateSubjectNameTextBox.TabIndex = 18;
            this.CertificateSubjectNameTextBox.TextChanged += new System.EventHandler(this.CertificateLocation_TextChanged);
            // 
            // CertificateSubjectNameLabel
            // 
            this.CertificateSubjectNameLabel.AllowDrop = true;
            this.CertificateSubjectNameLabel.AutoSize = true;
            this.CertificateSubjectNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateSubjectNameLabel.Location = new System.Drawing.Point(7, 252);
            this.CertificateSubjectNameLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificateSubjectNameLabel.Name = "CertificateSubjectNameLabel";
            this.CertificateSubjectNameLabel.Size = new System.Drawing.Size(230, 22);
            this.CertificateSubjectNameLabel.TabIndex = 17;
            this.CertificateSubjectNameLabel.Text = "Certificate Subject Name";
            this.CertificateSubjectNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IssuerListStorePathTextBox
            // 
            this.IssuerListStorePathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IssuerListStorePathTextBox.Location = new System.Drawing.Point(243, 365);
            this.IssuerListStorePathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.IssuerListStorePathTextBox.Name = "IssuerListStorePathTextBox";
            this.IssuerListStorePathTextBox.Size = new System.Drawing.Size(890, 22);
            this.IssuerListStorePathTextBox.TabIndex = 30;
            this.IssuerListStorePathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // IssuerListStorePathLabel
            // 
            this.IssuerListStorePathLabel.AllowDrop = true;
            this.IssuerListStorePathLabel.AutoSize = true;
            this.IssuerListStorePathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IssuerListStorePathLabel.Location = new System.Drawing.Point(7, 365);
            this.IssuerListStorePathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.IssuerListStorePathLabel.Name = "IssuerListStorePathLabel";
            this.IssuerListStorePathLabel.Size = new System.Drawing.Size(230, 25);
            this.IssuerListStorePathLabel.TabIndex = 29;
            this.IssuerListStorePathLabel.Text = "Issuer List Store Path";
            this.IssuerListStorePathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TrustListStorePathTextBox
            // 
            this.TrustListStorePathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TrustListStorePathTextBox.Location = new System.Drawing.Point(243, 336);
            this.TrustListStorePathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.TrustListStorePathTextBox.Name = "TrustListStorePathTextBox";
            this.TrustListStorePathTextBox.Size = new System.Drawing.Size(890, 22);
            this.TrustListStorePathTextBox.TabIndex = 28;
            this.TrustListStorePathTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // TrustListStorePathLabel
            // 
            this.TrustListStorePathLabel.AllowDrop = true;
            this.TrustListStorePathLabel.AutoSize = true;
            this.TrustListStorePathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TrustListStorePathLabel.Location = new System.Drawing.Point(7, 336);
            this.TrustListStorePathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.TrustListStorePathLabel.Name = "TrustListStorePathLabel";
            this.TrustListStorePathLabel.Size = new System.Drawing.Size(230, 25);
            this.TrustListStorePathLabel.TabIndex = 27;
            this.TrustListStorePathLabel.Text = "Trust List Store Path";
            this.TrustListStorePathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriTextBox
            // 
            this.ProductUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriTextBox.Location = new System.Drawing.Point(243, 110);
            this.ProductUriTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ProductUriTextBox.Name = "ProductUriTextBox";
            this.ProductUriTextBox.Size = new System.Drawing.Size(890, 22);
            this.ProductUriTextBox.TabIndex = 13;
            this.ProductUriTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // ProductUriLabel
            // 
            this.ProductUriLabel.AllowDrop = true;
            this.ProductUriLabel.AutoSize = true;
            this.ProductUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriLabel.Location = new System.Drawing.Point(7, 110);
            this.ProductUriLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ProductUriLabel.Name = "ProductUriLabel";
            this.ProductUriLabel.Size = new System.Drawing.Size(230, 22);
            this.ProductUriLabel.TabIndex = 12;
            this.ProductUriLabel.Text = "Product URI";
            this.ProductUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriTextBox
            // 
            this.ApplicationUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriTextBox.Location = new System.Drawing.Point(243, 84);
            this.ApplicationUriTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationUriTextBox.Name = "ApplicationUriTextBox";
            this.ApplicationUriTextBox.Size = new System.Drawing.Size(890, 22);
            this.ApplicationUriTextBox.TabIndex = 11;
            this.ApplicationUriTextBox.TextChanged += new System.EventHandler(this.ApplicationUriTextBox_TextChanged);
            // 
            // ApplicationUriLabel
            // 
            this.ApplicationUriLabel.AllowDrop = true;
            this.ApplicationUriLabel.AutoSize = true;
            this.ApplicationUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriLabel.Location = new System.Drawing.Point(7, 84);
            this.ApplicationUriLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationUriLabel.Name = "ApplicationUriLabel";
            this.ApplicationUriLabel.Size = new System.Drawing.Size(230, 22);
            this.ApplicationUriLabel.TabIndex = 10;
            this.ApplicationUriLabel.Text = "Application URI";
            this.ApplicationUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameTextBox
            // 
            this.ApplicationNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameTextBox.Location = new System.Drawing.Point(243, 58);
            this.ApplicationNameTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationNameTextBox.Name = "ApplicationNameTextBox";
            this.ApplicationNameTextBox.Size = new System.Drawing.Size(890, 22);
            this.ApplicationNameTextBox.TabIndex = 9;
            this.ApplicationNameTextBox.TextChanged += new System.EventHandler(this.GenericField_TextChanged);
            // 
            // ApplicationNameLabel
            // 
            this.ApplicationNameLabel.AllowDrop = true;
            this.ApplicationNameLabel.AutoSize = true;
            this.ApplicationNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameLabel.Location = new System.Drawing.Point(7, 58);
            this.ApplicationNameLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ApplicationNameLabel.Name = "ApplicationNameLabel";
            this.ApplicationNameLabel.Size = new System.Drawing.Size(230, 22);
            this.ApplicationNameLabel.TabIndex = 8;
            this.ApplicationNameLabel.Text = "Application Name";
            this.ApplicationNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CertificatePrivateKeyPathTextBox
            // 
            this.CertificatePrivateKeyPathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatePrivateKeyPathTextBox.Location = new System.Drawing.Point(243, 307);
            this.CertificatePrivateKeyPathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificatePrivateKeyPathTextBox.Name = "CertificatePrivateKeyPathTextBox";
            this.CertificatePrivateKeyPathTextBox.Size = new System.Drawing.Size(890, 22);
            this.CertificatePrivateKeyPathTextBox.TabIndex = 23;
            this.CertificatePrivateKeyPathTextBox.TextChanged += new System.EventHandler(this.CertificateLocation_TextChanged);
            // 
            // CertificatePrivateKeyPathLabel
            // 
            this.CertificatePrivateKeyPathLabel.AllowDrop = true;
            this.CertificatePrivateKeyPathLabel.AutoSize = true;
            this.CertificatePrivateKeyPathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatePrivateKeyPathLabel.Location = new System.Drawing.Point(7, 307);
            this.CertificatePrivateKeyPathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificatePrivateKeyPathLabel.Name = "CertificatePrivateKeyPathLabel";
            this.CertificatePrivateKeyPathLabel.Size = new System.Drawing.Size(230, 25);
            this.CertificatePrivateKeyPathLabel.TabIndex = 22;
            this.CertificatePrivateKeyPathLabel.Text = "Certificate Private Key Path";
            this.CertificatePrivateKeyPathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CertificatePublicKeyPathTextBox
            // 
            this.CertificatePublicKeyPathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatePublicKeyPathTextBox.Location = new System.Drawing.Point(243, 278);
            this.CertificatePublicKeyPathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificatePublicKeyPathTextBox.Name = "CertificatePublicKeyPathTextBox";
            this.CertificatePublicKeyPathTextBox.Size = new System.Drawing.Size(890, 22);
            this.CertificatePublicKeyPathTextBox.TabIndex = 20;
            this.CertificatePublicKeyPathTextBox.TextChanged += new System.EventHandler(this.CertificateLocation_TextChanged);
            // 
            // CertificatePublicKeyPathLabel
            // 
            this.CertificatePublicKeyPathLabel.AllowDrop = true;
            this.CertificatePublicKeyPathLabel.AutoSize = true;
            this.CertificatePublicKeyPathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatePublicKeyPathLabel.Location = new System.Drawing.Point(7, 278);
            this.CertificatePublicKeyPathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificatePublicKeyPathLabel.Name = "CertificatePublicKeyPathLabel";
            this.CertificatePublicKeyPathLabel.Size = new System.Drawing.Size(230, 25);
            this.CertificatePublicKeyPathLabel.TabIndex = 19;
            this.CertificatePublicKeyPathLabel.Text = "Certificate Public Key Path";
            this.CertificatePublicKeyPathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CertificateStorePathTextBox
            // 
            this.CertificateStorePathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateStorePathTextBox.Location = new System.Drawing.Point(243, 223);
            this.CertificateStorePathTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificateStorePathTextBox.Name = "CertificateStorePathTextBox";
            this.CertificateStorePathTextBox.Size = new System.Drawing.Size(890, 22);
            this.CertificateStorePathTextBox.TabIndex = 15;
            this.CertificateStorePathTextBox.TextChanged += new System.EventHandler(this.CertificateLocation_TextChanged);
            // 
            // CertificateStorePathLabel
            // 
            this.CertificateStorePathLabel.AllowDrop = true;
            this.CertificateStorePathLabel.AutoSize = true;
            this.CertificateStorePathLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateStorePathLabel.Location = new System.Drawing.Point(7, 223);
            this.CertificateStorePathLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.CertificateStorePathLabel.Name = "CertificateStorePathLabel";
            this.CertificateStorePathLabel.Size = new System.Drawing.Size(230, 25);
            this.CertificateStorePathLabel.TabIndex = 14;
            this.CertificateStorePathLabel.Text = "Certificate Store Path";
            this.CertificateStorePathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ConfigurationFileTextBox
            // 
            this.ConfigurationFileTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ConfigurationFileTextBox.Location = new System.Drawing.Point(243, 194);
            this.ConfigurationFileTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ConfigurationFileTextBox.Name = "ConfigurationFileTextBox";
            this.ConfigurationFileTextBox.Size = new System.Drawing.Size(890, 22);
            this.ConfigurationFileTextBox.TabIndex = 3;
            this.ConfigurationFileTextBox.TextChanged += new System.EventHandler(this.ConfigurationFileTextBox_TextChanged);
            // 
            // ConfigurationFileLabel
            // 
            this.ConfigurationFileLabel.AllowDrop = true;
            this.ConfigurationFileLabel.AutoSize = true;
            this.ConfigurationFileLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ConfigurationFileLabel.Location = new System.Drawing.Point(7, 194);
            this.ConfigurationFileLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.ConfigurationFileLabel.Name = "ConfigurationFileLabel";
            this.ConfigurationFileLabel.Size = new System.Drawing.Size(230, 25);
            this.ConfigurationFileLabel.TabIndex = 2;
            this.ConfigurationFileLabel.Text = "Configuration File";
            this.ConfigurationFileLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // RegistrationTypeLabel
            // 
            this.RegistrationTypeLabel.AllowDrop = true;
            this.RegistrationTypeLabel.AutoSize = true;
            this.RegistrationTypeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationTypeLabel.Location = new System.Drawing.Point(7, 2);
            this.RegistrationTypeLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.RegistrationTypeLabel.Name = "RegistrationTypeLabel";
            this.RegistrationTypeLabel.Size = new System.Drawing.Size(230, 23);
            this.RegistrationTypeLabel.TabIndex = 0;
            this.RegistrationTypeLabel.Text = "Registration Type";
            this.RegistrationTypeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // RegistrationTypeComboBox
            // 
            this.RegistrationTypeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.RegistrationTypeComboBox.FormattingEnabled = true;
            this.RegistrationTypeComboBox.Location = new System.Drawing.Point(243, 2);
            this.RegistrationTypeComboBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 1);
            this.RegistrationTypeComboBox.Name = "RegistrationTypeComboBox";
            this.RegistrationTypeComboBox.Size = new System.Drawing.Size(244, 24);
            this.RegistrationTypeComboBox.TabIndex = 1;
            this.RegistrationTypeComboBox.SelectedIndexChanged += new System.EventHandler(this.RegistrationTypeComboBox_SelectedIndexChanged);
            // 
            // CertificateStorePathButton
            // 
            this.CertificateStorePathButton.Location = new System.Drawing.Point(1136, 223);
            this.CertificateStorePathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.CertificateStorePathButton.Name = "CertificateStorePathButton";
            this.CertificateStorePathButton.Size = new System.Drawing.Size(32, 25);
            this.CertificateStorePathButton.TabIndex = 16;
            this.CertificateStorePathButton.Text = "...";
            this.CertificateStorePathButton.UseVisualStyleBackColor = true;
            this.CertificateStorePathButton.Click += new System.EventHandler(this.CertificateStorePathButton_Click);
            // 
            // ConfigurationFileButton
            // 
            this.ConfigurationFileButton.Location = new System.Drawing.Point(1136, 194);
            this.ConfigurationFileButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.ConfigurationFileButton.Name = "ConfigurationFileButton";
            this.ConfigurationFileButton.Size = new System.Drawing.Size(32, 25);
            this.ConfigurationFileButton.TabIndex = 4;
            this.ConfigurationFileButton.Text = "...";
            this.ConfigurationFileButton.UseVisualStyleBackColor = true;
            this.ConfigurationFileButton.Click += new System.EventHandler(this.ConfigurationFileButton_Click);
            // 
            // CertificatePrivateKeyPathButton
            // 
            this.CertificatePrivateKeyPathButton.Location = new System.Drawing.Point(1136, 307);
            this.CertificatePrivateKeyPathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.CertificatePrivateKeyPathButton.Name = "CertificatePrivateKeyPathButton";
            this.CertificatePrivateKeyPathButton.Size = new System.Drawing.Size(32, 25);
            this.CertificatePrivateKeyPathButton.TabIndex = 24;
            this.CertificatePrivateKeyPathButton.Text = "...";
            this.CertificatePrivateKeyPathButton.UseVisualStyleBackColor = true;
            this.CertificatePrivateKeyPathButton.Click += new System.EventHandler(this.CertificatePrivateKeyPathButton_Click);
            // 
            // CertificatePublicKeyPathButton
            // 
            this.CertificatePublicKeyPathButton.Location = new System.Drawing.Point(1136, 278);
            this.CertificatePublicKeyPathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.CertificatePublicKeyPathButton.Name = "CertificatePublicKeyPathButton";
            this.CertificatePublicKeyPathButton.Size = new System.Drawing.Size(32, 25);
            this.CertificatePublicKeyPathButton.TabIndex = 21;
            this.CertificatePublicKeyPathButton.Text = "...";
            this.CertificatePublicKeyPathButton.UseVisualStyleBackColor = true;
            this.CertificatePublicKeyPathButton.Click += new System.EventHandler(this.CertificatePublicKeyPathButton_Click);
            // 
            // TrustListStorePathButton
            // 
            this.TrustListStorePathButton.Location = new System.Drawing.Point(1136, 336);
            this.TrustListStorePathButton.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.TrustListStorePathButton.Name = "TrustListStorePathButton";
            this.TrustListStorePathButton.Size = new System.Drawing.Size(32, 25);
            this.TrustListStorePathButton.TabIndex = 32;
            this.TrustListStorePathButton.Text = "...";
            this.TrustListStorePathButton.UseVisualStyleBackColor = true;
            this.TrustListStorePathButton.Click += new System.EventHandler(this.TrustListStorePathButton_Click);
            // 
            // RegistrationButtonsPanel
            // 
            this.RegistrationButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButtonsPanel.Controls.Add(this.PickServerButton);
            this.RegistrationButtonsPanel.Controls.Add(this.ClearButton);
            this.RegistrationButtonsPanel.Controls.Add(this.OpenConfigurationButton);
            this.RegistrationButtonsPanel.Controls.Add(this.LoadButton);
            this.RegistrationButtonsPanel.Controls.Add(this.SaveButton);
            this.RegistrationButtonsPanel.Controls.Add(this.UnregisterApplicationButton);
            this.RegistrationButtonsPanel.Controls.Add(this.RegisterApplicationButton);
            this.RegistrationButtonsPanel.Controls.Add(this.ApplyChangesButton);
            this.RegistrationButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RegistrationButtonsPanel.Location = new System.Drawing.Point(0, 594);
            this.RegistrationButtonsPanel.Margin = new System.Windows.Forms.Padding(4);
            this.RegistrationButtonsPanel.Name = "RegistrationButtonsPanel";
            this.RegistrationButtonsPanel.Size = new System.Drawing.Size(1172, 39);
            this.RegistrationButtonsPanel.TabIndex = 13;
            // 
            // PickServerButton
            // 
            this.PickServerButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.PickServerButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.PickServerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PickServerButton.ForeColor = System.Drawing.Color.White;
            this.PickServerButton.Location = new System.Drawing.Point(1029, 0);
            this.PickServerButton.Margin = new System.Windows.Forms.Padding(4);
            this.PickServerButton.Name = "PickServerButton";
            this.PickServerButton.Size = new System.Drawing.Size(147, 39);
            this.PickServerButton.TabIndex = 7;
            this.PickServerButton.Text = "Pick Server";
            this.ToolTip.SetToolTip(this.PickServerButton, "Clears all fields");
            this.PickServerButton.UseVisualStyleBackColor = false;
            this.PickServerButton.Click += new System.EventHandler(this.PickServerButton_Click);
            // 
            // ClearButton
            // 
            this.ClearButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ClearButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ClearButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ClearButton.ForeColor = System.Drawing.Color.White;
            this.ClearButton.Location = new System.Drawing.Point(882, 0);
            this.ClearButton.Margin = new System.Windows.Forms.Padding(4);
            this.ClearButton.Name = "ClearButton";
            this.ClearButton.Size = new System.Drawing.Size(147, 39);
            this.ClearButton.TabIndex = 6;
            this.ClearButton.Text = "Clear";
            this.ToolTip.SetToolTip(this.ClearButton, "Clears all fields");
            this.ClearButton.UseVisualStyleBackColor = false;
            this.ClearButton.Click += new System.EventHandler(this.ClearButton_Click);
            this.ClearButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ClearButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // OpenConfigurationButton
            // 
            this.OpenConfigurationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.OpenConfigurationButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.OpenConfigurationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.OpenConfigurationButton.ForeColor = System.Drawing.Color.White;
            this.OpenConfigurationButton.Location = new System.Drawing.Point(735, 0);
            this.OpenConfigurationButton.Margin = new System.Windows.Forms.Padding(4);
            this.OpenConfigurationButton.Name = "OpenConfigurationButton";
            this.OpenConfigurationButton.Size = new System.Drawing.Size(147, 39);
            this.OpenConfigurationButton.TabIndex = 5;
            this.OpenConfigurationButton.Text = "Open Config";
            this.ToolTip.SetToolTip(this.OpenConfigurationButton, "Launches an external editor to view the contents of the configuration file.");
            this.OpenConfigurationButton.UseVisualStyleBackColor = false;
            this.OpenConfigurationButton.Click += new System.EventHandler(this.OpenConfigurationButton_Click);
            this.OpenConfigurationButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.OpenConfigurationButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // LoadButton
            // 
            this.LoadButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.LoadButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.LoadButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.LoadButton.ForeColor = System.Drawing.Color.White;
            this.LoadButton.Location = new System.Drawing.Point(588, 0);
            this.LoadButton.Margin = new System.Windows.Forms.Padding(4);
            this.LoadButton.Name = "LoadButton";
            this.LoadButton.Size = new System.Drawing.Size(147, 39);
            this.LoadButton.TabIndex = 4;
            this.LoadButton.Text = "Load";
            this.ToolTip.SetToolTip(this.LoadButton, "Loads a previously saved registration file.");
            this.LoadButton.UseVisualStyleBackColor = false;
            this.LoadButton.Click += new System.EventHandler(this.LoadButton_Click);
            this.LoadButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.LoadButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // SaveButton
            // 
            this.SaveButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.SaveButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.SaveButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SaveButton.ForeColor = System.Drawing.Color.White;
            this.SaveButton.Location = new System.Drawing.Point(441, 0);
            this.SaveButton.Margin = new System.Windows.Forms.Padding(4);
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Size = new System.Drawing.Size(147, 39);
            this.SaveButton.TabIndex = 3;
            this.SaveButton.Text = "Save";
            this.ToolTip.SetToolTip(this.SaveButton, "Saves the registration information in a form that can be copied to other machines" +
        " and reused.");
            this.SaveButton.UseVisualStyleBackColor = false;
            this.SaveButton.Click += new System.EventHandler(this.SaveButton_Click);
            this.SaveButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.SaveButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // UnregisterApplicationButton
            // 
            this.UnregisterApplicationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.UnregisterApplicationButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.UnregisterApplicationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.UnregisterApplicationButton.ForeColor = System.Drawing.Color.White;
            this.UnregisterApplicationButton.Location = new System.Drawing.Point(294, 0);
            this.UnregisterApplicationButton.Margin = new System.Windows.Forms.Padding(4);
            this.UnregisterApplicationButton.Name = "UnregisterApplicationButton";
            this.UnregisterApplicationButton.Size = new System.Drawing.Size(147, 39);
            this.UnregisterApplicationButton.TabIndex = 1;
            this.UnregisterApplicationButton.Text = "Unregister";
            this.ToolTip.SetToolTip(this.UnregisterApplicationButton, "Unregisters the Application and revokes its Certificate.");
            this.UnregisterApplicationButton.UseVisualStyleBackColor = false;
            this.UnregisterApplicationButton.Click += new System.EventHandler(this.UnregisterApplicationButton_Click);
            this.UnregisterApplicationButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.UnregisterApplicationButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // RegisterApplicationButton
            // 
            this.RegisterApplicationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegisterApplicationButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.RegisterApplicationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RegisterApplicationButton.ForeColor = System.Drawing.Color.White;
            this.RegisterApplicationButton.Location = new System.Drawing.Point(147, 0);
            this.RegisterApplicationButton.Margin = new System.Windows.Forms.Padding(4);
            this.RegisterApplicationButton.Name = "RegisterApplicationButton";
            this.RegisterApplicationButton.Size = new System.Drawing.Size(147, 39);
            this.RegisterApplicationButton.TabIndex = 0;
            this.RegisterApplicationButton.Text = "Register";
            this.ToolTip.SetToolTip(this.RegisterApplicationButton, "Registers the Application with the GDS. Updates any existing record.");
            this.RegisterApplicationButton.UseVisualStyleBackColor = false;
            this.RegisterApplicationButton.Click += new System.EventHandler(this.RegisterApplicationButton_Click);
            this.RegisterApplicationButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.RegisterApplicationButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // ApplyChangesButton
            // 
            this.ApplyChangesButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ApplyChangesButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ApplyChangesButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApplyChangesButton.ForeColor = System.Drawing.Color.White;
            this.ApplyChangesButton.Location = new System.Drawing.Point(0, 0);
            this.ApplyChangesButton.Margin = new System.Windows.Forms.Padding(4);
            this.ApplyChangesButton.Name = "ApplyChangesButton";
            this.ApplyChangesButton.Size = new System.Drawing.Size(147, 39);
            this.ApplyChangesButton.TabIndex = 2;
            this.ApplyChangesButton.Text = "Apply Changes";
            this.ToolTip.SetToolTip(this.ApplyChangesButton, "Saves any changes to the fields in local memory.");
            this.ApplyChangesButton.UseVisualStyleBackColor = false;
            this.ApplyChangesButton.Click += new System.EventHandler(this.ApplyChangesButton_Click);
            this.ApplyChangesButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ApplyChangesButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // DomainsLabel
            // 
            this.DomainsLabel.AllowDrop = true;
            this.DomainsLabel.AutoSize = true;
            this.DomainsLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DomainsLabel.Location = new System.Drawing.Point(7, 510);
            this.DomainsLabel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.DomainsLabel.Name = "DomainsLabel";
            this.DomainsLabel.Size = new System.Drawing.Size(230, 22);
            this.DomainsLabel.TabIndex = 62;
            this.DomainsLabel.Text = "Domains";
            this.DomainsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DomainsTextBox
            // 
            this.DomainsTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DomainsTextBox.Location = new System.Drawing.Point(243, 510);
            this.DomainsTextBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.DomainsTextBox.Name = "DomainsTextBox";
            this.DomainsTextBox.Size = new System.Drawing.Size(890, 22);
            this.DomainsTextBox.TabIndex = 63;
            // 
            // RegisterApplicationControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RegistrationPanel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "RegisterApplicationControl";
            this.Size = new System.Drawing.Size(1172, 633);
            this.RegistrationPanel.ResumeLayout(false);
            this.RegistrationDetailsPanel.ResumeLayout(false);
            this.RegistrationDetailsPanel.PerformLayout();
            this.RegistrationButtonsPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel RegistrationPanel;
        private System.Windows.Forms.TableLayoutPanel RegistrationDetailsPanel;
        private System.Windows.Forms.Button ServerCapabilitiesButton;
        private System.Windows.Forms.Label ServerCapabilitiesLabel;
        private System.Windows.Forms.Label ApplicationIdTextBox;
        private System.Windows.Forms.Label ApplicationIdLabel;
        private System.Windows.Forms.Button IssuerListStorePathButton;
        private System.Windows.Forms.TextBox CertificateSubjectNameTextBox;
        private System.Windows.Forms.Label CertificateSubjectNameLabel;
        private System.Windows.Forms.TextBox IssuerListStorePathTextBox;
        private System.Windows.Forms.Label IssuerListStorePathLabel;
        private System.Windows.Forms.TextBox TrustListStorePathTextBox;
        private System.Windows.Forms.Label TrustListStorePathLabel;
        private System.Windows.Forms.TextBox ProductUriTextBox;
        private System.Windows.Forms.Label ProductUriLabel;
        private System.Windows.Forms.TextBox ApplicationUriTextBox;
        private System.Windows.Forms.Label ApplicationUriLabel;
        private System.Windows.Forms.TextBox ApplicationNameTextBox;
        private System.Windows.Forms.Label ApplicationNameLabel;
        private System.Windows.Forms.TextBox CertificatePrivateKeyPathTextBox;
        private System.Windows.Forms.Label CertificatePrivateKeyPathLabel;
        private System.Windows.Forms.TextBox CertificatePublicKeyPathTextBox;
        private System.Windows.Forms.Label CertificatePublicKeyPathLabel;
        private System.Windows.Forms.TextBox CertificateStorePathTextBox;
        private System.Windows.Forms.Label CertificateStorePathLabel;
        private System.Windows.Forms.TextBox ConfigurationFileTextBox;
        private System.Windows.Forms.Label ConfigurationFileLabel;
        private System.Windows.Forms.Label RegistrationTypeLabel;
        private System.Windows.Forms.ComboBox RegistrationTypeComboBox;
        private System.Windows.Forms.Button CertificateStorePathButton;
        private System.Windows.Forms.Button ConfigurationFileButton;
        private System.Windows.Forms.Button CertificatePrivateKeyPathButton;
        private System.Windows.Forms.Button CertificatePublicKeyPathButton;
        private System.Windows.Forms.Button TrustListStorePathButton;
        private System.Windows.Forms.Panel RegistrationButtonsPanel;
        private System.Windows.Forms.Button UnregisterApplicationButton;
        private System.Windows.Forms.Button RegisterApplicationButton;
        private System.Windows.Forms.Label ServerCapabilitiesTextBox;
        private System.Windows.Forms.Button DiscoveryUrlsButton;
        private System.Windows.Forms.Label DiscoveryUrlsTextBox;
        private System.Windows.Forms.Label DiscoveryUrlsLabel;
        private System.Windows.Forms.Button ApplyChangesButton;
        private System.Windows.Forms.Button LoadButton;
        private System.Windows.Forms.Button SaveButton;
        private System.Windows.Forms.Button OpenConfigurationButton;
        private System.Windows.Forms.ToolTip ToolTip;
        private System.Windows.Forms.Button HttpsIssuerListStorePathButton;
        private System.Windows.Forms.TextBox HttpsIssuerListStorePathTextBox;
        private System.Windows.Forms.Label HttpsIssuerListStorePathLabel;
        private System.Windows.Forms.Button HttpsTrustListStorePathButton;
        private System.Windows.Forms.TextBox HttpsTrustListStorePathTextBox;
        private System.Windows.Forms.Label HttpsTrustListStorePathLabel;
        private System.Windows.Forms.Button HttpsCertificatePrivateKeyPathButton;
        private System.Windows.Forms.Button HttpsCertificatePublicKeyPathButton;
        private System.Windows.Forms.TextBox HttpsCertificatePrivateKeyPathTextBox;
        private System.Windows.Forms.TextBox HttpsCertificatePublicKeyPathTextBox;
        private System.Windows.Forms.Label HttpsCertificatePrivateKeyPathLabel;
        private System.Windows.Forms.Label HttpsCertificatePublicKeyPathLabel;
        private System.Windows.Forms.Button ClearButton;
        private System.Windows.Forms.Button PickServerButton;
        private System.Windows.Forms.TextBox DomainsTextBox;
        private System.Windows.Forms.Label DomainsLabel;
    }
}
