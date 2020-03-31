/* Copyright (c) 1996-2019, OPC Foundation. All rights reserved.

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
    partial class MainForm
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
            this.BottomStatusStrip = new System.Windows.Forms.StatusStrip();
            this.GdsServerStatusIcon = new System.Windows.Forms.ToolStripStatusLabel();
            this.GdsServerStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.GdsServerStatusTime = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerStatusIcon = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerStatusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.ServerStatusTime = new System.Windows.Forms.ToolStripStatusLabel();
            this.LeftPanel = new System.Windows.Forms.Panel();
            this.SelectGdsButton = new System.Windows.Forms.Button();
            this.DiscoveryButton = new System.Windows.Forms.Button();
            this.ConfigurationButton = new System.Windows.Forms.Button();
            this.SelectServerButton = new System.Windows.Forms.Button();
            this.HttpsTrustListButton = new System.Windows.Forms.Button();
            this.TrustListButton = new System.Windows.Forms.Button();
            this.HttpsCertificateButton = new System.Windows.Forms.Button();
            this.CertificateButton = new System.Windows.Forms.Button();
            this.ServerStatusButton = new System.Windows.Forms.Button();
            this.RegistrationButton = new System.Windows.Forms.Button();
            this.ServerUrlPanel = new System.Windows.Forms.Panel();
            this.ServerUrlTextBox = new System.Windows.Forms.TextBox();
            this.ConnectButton = new System.Windows.Forms.Button();
            this.DiscnnectButton = new System.Windows.Forms.Button();
            this.DiscoveryPanel = new Opc.Ua.Gds.Client.Controls.DiscoveryControl();
            this.TrustListPanel = new Opc.Ua.Gds.Client.ApplicationTrustListControl();
            this.CertificatePanel = new Opc.Ua.Gds.Client.ApplicationCertificateControl();
            this.RegistrationPanel = new Opc.Ua.Gds.Client.RegisterApplicationControl();
            this.ServerStatusPanel = new Opc.Ua.Gds.Client.Controls.ServerStatusControl();
            this.BottomStatusStrip.SuspendLayout();
            this.LeftPanel.SuspendLayout();
            this.ServerUrlPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // BottomStatusStrip
            // 
            this.BottomStatusStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.BottomStatusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.GdsServerStatusIcon,
            this.GdsServerStatusLabel,
            this.GdsServerStatusTime,
            this.ServerStatusIcon,
            this.ServerStatusLabel,
            this.ServerStatusTime});
            this.BottomStatusStrip.Location = new System.Drawing.Point(0, 485);
            this.BottomStatusStrip.Name = "BottomStatusStrip";
            this.BottomStatusStrip.Size = new System.Drawing.Size(1008, 22);
            this.BottomStatusStrip.TabIndex = 3;
            this.BottomStatusStrip.Text = "BottomStatusStrip";
            // 
            // GdsServerStatusIcon
            // 
            this.GdsServerStatusIcon.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.GdsServerStatusIcon.ForeColor = System.Drawing.Color.Transparent;
            this.GdsServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.nav_plain_green;
            this.GdsServerStatusIcon.Margin = new System.Windows.Forms.Padding(0);
            this.GdsServerStatusIcon.Name = "GdsServerStatusIcon";
            this.GdsServerStatusIcon.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.GdsServerStatusIcon.Size = new System.Drawing.Size(26, 22);
            // 
            // GdsServerStatusLabel
            // 
            this.GdsServerStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.GdsServerStatusLabel.Name = "GdsServerStatusLabel";
            this.GdsServerStatusLabel.Size = new System.Drawing.Size(22, 17);
            this.GdsServerStatusLabel.Text = "---";
            // 
            // GdsServerStatusTime
            // 
            this.GdsServerStatusTime.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.GdsServerStatusTime.Name = "GdsServerStatusTime";
            this.GdsServerStatusTime.Size = new System.Drawing.Size(22, 17);
            this.GdsServerStatusTime.Text = "---";
            // 
            // ServerStatusIcon
            // 
            this.ServerStatusIcon.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.ServerStatusIcon.ForeColor = System.Drawing.Color.Transparent;
            this.ServerStatusIcon.Image = global::Opc.Ua.Gds.Client.Properties.Resources.nav_plain_green;
            this.ServerStatusIcon.Margin = new System.Windows.Forms.Padding(0);
            this.ServerStatusIcon.Name = "ServerStatusIcon";
            this.ServerStatusIcon.Padding = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.ServerStatusIcon.Size = new System.Drawing.Size(26, 22);
            // 
            // ServerStatusLabel
            // 
            this.ServerStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ServerStatusLabel.Name = "ServerStatusLabel";
            this.ServerStatusLabel.Size = new System.Drawing.Size(22, 17);
            this.ServerStatusLabel.Text = "---";
            // 
            // ServerStatusTime
            // 
            this.ServerStatusTime.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ServerStatusTime.Name = "ServerStatusTime";
            this.ServerStatusTime.Size = new System.Drawing.Size(22, 17);
            this.ServerStatusTime.Text = "---";
            // 
            // LeftPanel
            // 
            this.LeftPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.LeftPanel.Controls.Add(this.SelectGdsButton);
            this.LeftPanel.Controls.Add(this.DiscoveryButton);
            this.LeftPanel.Controls.Add(this.ConfigurationButton);
            this.LeftPanel.Controls.Add(this.SelectServerButton);
            this.LeftPanel.Controls.Add(this.HttpsTrustListButton);
            this.LeftPanel.Controls.Add(this.TrustListButton);
            this.LeftPanel.Controls.Add(this.HttpsCertificateButton);
            this.LeftPanel.Controls.Add(this.CertificateButton);
            this.LeftPanel.Controls.Add(this.ServerStatusButton);
            this.LeftPanel.Controls.Add(this.RegistrationButton);
            this.LeftPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.LeftPanel.Location = new System.Drawing.Point(0, 3);
            this.LeftPanel.Margin = new System.Windows.Forms.Padding(4);
            this.LeftPanel.Name = "LeftPanel";
            this.LeftPanel.Size = new System.Drawing.Size(129, 482);
            this.LeftPanel.TabIndex = 5;
            // 
            // SelectGdsButton
            // 
            this.SelectGdsButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.SelectGdsButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.SelectGdsButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SelectGdsButton.ForeColor = System.Drawing.Color.White;
            this.SelectGdsButton.Location = new System.Drawing.Point(0, 288);
            this.SelectGdsButton.Name = "SelectGdsButton";
            this.SelectGdsButton.Size = new System.Drawing.Size(129, 32);
            this.SelectGdsButton.TabIndex = 9;
            this.SelectGdsButton.Text = "Select GDS";
            this.SelectGdsButton.UseVisualStyleBackColor = false;
            this.SelectGdsButton.Click += new System.EventHandler(this.SelectGdsButton_Click);
            // 
            // DiscoveryButton
            // 
            this.DiscoveryButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.DiscoveryButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.DiscoveryButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.DiscoveryButton.ForeColor = System.Drawing.Color.White;
            this.DiscoveryButton.Location = new System.Drawing.Point(0, 256);
            this.DiscoveryButton.Name = "DiscoveryButton";
            this.DiscoveryButton.Size = new System.Drawing.Size(129, 32);
            this.DiscoveryButton.TabIndex = 6;
            this.DiscoveryButton.Text = "Discovery";
            this.DiscoveryButton.UseVisualStyleBackColor = false;
            this.DiscoveryButton.Click += new System.EventHandler(this.DiscoveryButton_Click);
            // 
            // ConfigurationButton
            // 
            this.ConfigurationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ConfigurationButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConfigurationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ConfigurationButton.ForeColor = System.Drawing.Color.White;
            this.ConfigurationButton.Location = new System.Drawing.Point(0, 224);
            this.ConfigurationButton.Name = "ConfigurationButton";
            this.ConfigurationButton.Size = new System.Drawing.Size(129, 32);
            this.ConfigurationButton.TabIndex = 5;
            this.ConfigurationButton.Text = "Configuration";
            this.ConfigurationButton.UseVisualStyleBackColor = false;
            this.ConfigurationButton.Visible = false;
            this.ConfigurationButton.Click += new System.EventHandler(this.ConfigurationButton_Click);
            // 
            // SelectServerButton
            // 
            this.SelectServerButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.SelectServerButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.SelectServerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.SelectServerButton.ForeColor = System.Drawing.Color.White;
            this.SelectServerButton.Location = new System.Drawing.Point(0, 192);
            this.SelectServerButton.Name = "SelectServerButton";
            this.SelectServerButton.Size = new System.Drawing.Size(129, 32);
            this.SelectServerButton.TabIndex = 1;
            this.SelectServerButton.Text = "Select Server";
            this.SelectServerButton.UseVisualStyleBackColor = false;
            this.SelectServerButton.Visible = false;
            this.SelectServerButton.Click += new System.EventHandler(this.SelectServerButton_Click);
            // 
            // HttpsTrustListButton
            // 
            this.HttpsTrustListButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.HttpsTrustListButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.HttpsTrustListButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.HttpsTrustListButton.ForeColor = System.Drawing.Color.White;
            this.HttpsTrustListButton.Location = new System.Drawing.Point(0, 160);
            this.HttpsTrustListButton.Name = "HttpsTrustListButton";
            this.HttpsTrustListButton.Size = new System.Drawing.Size(129, 32);
            this.HttpsTrustListButton.TabIndex = 7;
            this.HttpsTrustListButton.Text = "HTTPS Trust List";
            this.HttpsTrustListButton.UseVisualStyleBackColor = false;
            this.HttpsTrustListButton.Click += new System.EventHandler(this.HttpsTrustListButton_Click);
            // 
            // TrustListButton
            // 
            this.TrustListButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.TrustListButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.TrustListButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TrustListButton.ForeColor = System.Drawing.Color.White;
            this.TrustListButton.Location = new System.Drawing.Point(0, 128);
            this.TrustListButton.Name = "TrustListButton";
            this.TrustListButton.Size = new System.Drawing.Size(129, 32);
            this.TrustListButton.TabIndex = 4;
            this.TrustListButton.Text = "Trust List";
            this.TrustListButton.UseVisualStyleBackColor = false;
            this.TrustListButton.Click += new System.EventHandler(this.TrustListButton_Click);
            // 
            // HttpsCertificateButton
            // 
            this.HttpsCertificateButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.HttpsCertificateButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.HttpsCertificateButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.HttpsCertificateButton.ForeColor = System.Drawing.Color.White;
            this.HttpsCertificateButton.Location = new System.Drawing.Point(0, 96);
            this.HttpsCertificateButton.Margin = new System.Windows.Forms.Padding(4);
            this.HttpsCertificateButton.Name = "HttpsCertificateButton";
            this.HttpsCertificateButton.Size = new System.Drawing.Size(129, 32);
            this.HttpsCertificateButton.TabIndex = 8;
            this.HttpsCertificateButton.Text = "HTTPS Certificate";
            this.HttpsCertificateButton.UseVisualStyleBackColor = false;
            this.HttpsCertificateButton.Click += new System.EventHandler(this.HttpsCertificateButton_ClickAsync);
            // 
            // CertificateButton
            // 
            this.CertificateButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.CertificateButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.CertificateButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.CertificateButton.ForeColor = System.Drawing.Color.White;
            this.CertificateButton.Location = new System.Drawing.Point(0, 64);
            this.CertificateButton.Margin = new System.Windows.Forms.Padding(4);
            this.CertificateButton.Name = "CertificateButton";
            this.CertificateButton.Size = new System.Drawing.Size(129, 32);
            this.CertificateButton.TabIndex = 3;
            this.CertificateButton.Text = "Certificate";
            this.CertificateButton.UseVisualStyleBackColor = false;
            this.CertificateButton.Click += new System.EventHandler(this.CertificateButton_ClickAsync);
            // 
            // ServerStatusButton
            // 
            this.ServerStatusButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ServerStatusButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerStatusButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ServerStatusButton.ForeColor = System.Drawing.Color.White;
            this.ServerStatusButton.Location = new System.Drawing.Point(0, 32);
            this.ServerStatusButton.Margin = new System.Windows.Forms.Padding(4);
            this.ServerStatusButton.Name = "ServerStatusButton";
            this.ServerStatusButton.Size = new System.Drawing.Size(129, 32);
            this.ServerStatusButton.TabIndex = 2;
            this.ServerStatusButton.Text = "Server Status";
            this.ServerStatusButton.UseVisualStyleBackColor = false;
            this.ServerStatusButton.Click += new System.EventHandler(this.ServerStatusButton_Click);
            // 
            // RegistrationButton
            // 
            this.RegistrationButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButton.Dock = System.Windows.Forms.DockStyle.Top;
            this.RegistrationButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RegistrationButton.ForeColor = System.Drawing.Color.White;
            this.RegistrationButton.Location = new System.Drawing.Point(0, 0);
            this.RegistrationButton.Margin = new System.Windows.Forms.Padding(4);
            this.RegistrationButton.Name = "RegistrationButton";
            this.RegistrationButton.Size = new System.Drawing.Size(129, 32);
            this.RegistrationButton.TabIndex = 0;
            this.RegistrationButton.Text = "Registration";
            this.RegistrationButton.UseVisualStyleBackColor = false;
            this.RegistrationButton.Click += new System.EventHandler(this.RegistrationButton_Click);
            // 
            // ServerUrlPanel
            // 
            this.ServerUrlPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.ServerUrlPanel.Controls.Add(this.ServerUrlTextBox);
            this.ServerUrlPanel.Controls.Add(this.ConnectButton);
            this.ServerUrlPanel.Controls.Add(this.DiscnnectButton);
            this.ServerUrlPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerUrlPanel.Location = new System.Drawing.Point(129, 3);
            this.ServerUrlPanel.Margin = new System.Windows.Forms.Padding(4);
            this.ServerUrlPanel.Name = "ServerUrlPanel";
            this.ServerUrlPanel.Padding = new System.Windows.Forms.Padding(0, 0, 3, 0);
            this.ServerUrlPanel.Size = new System.Drawing.Size(879, 32);
            this.ServerUrlPanel.TabIndex = 0;
            // 
            // ServerUrlTextBox
            // 
            this.ServerUrlTextBox.Location = new System.Drawing.Point(6, 6);
            this.ServerUrlTextBox.Margin = new System.Windows.Forms.Padding(4);
            this.ServerUrlTextBox.Name = "ServerUrlTextBox";
            this.ServerUrlTextBox.ReadOnly = true;
            this.ServerUrlTextBox.Size = new System.Drawing.Size(802, 20);
            this.ServerUrlTextBox.TabIndex = 0;
            // 
            // ConnectButton
            // 
            this.ConnectButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.ConnectButton.FlatAppearance.BorderColor = System.Drawing.Color.MidnightBlue;
            this.ConnectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ConnectButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ConnectButton.ForeColor = System.Drawing.Color.White;
            this.ConnectButton.Image = global::Opc.Ua.Gds.Client.Properties.Resources.media_play_green;
            this.ConnectButton.Location = new System.Drawing.Point(812, 0);
            this.ConnectButton.Margin = new System.Windows.Forms.Padding(0);
            this.ConnectButton.Name = "ConnectButton";
            this.ConnectButton.Size = new System.Drawing.Size(32, 32);
            this.ConnectButton.TabIndex = 1;
            this.ConnectButton.UseVisualStyleBackColor = false;
            this.ConnectButton.Click += new System.EventHandler(this.ConnectButton_ClickAsync);
            // 
            // DiscnnectButton
            // 
            this.DiscnnectButton.Dock = System.Windows.Forms.DockStyle.Right;
            this.DiscnnectButton.FlatAppearance.BorderColor = System.Drawing.Color.MidnightBlue;
            this.DiscnnectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.DiscnnectButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.DiscnnectButton.ForeColor = System.Drawing.Color.White;
            this.DiscnnectButton.Image = global::Opc.Ua.Gds.Client.Properties.Resources.media_stop_red;
            this.DiscnnectButton.Location = new System.Drawing.Point(844, 0);
            this.DiscnnectButton.Margin = new System.Windows.Forms.Padding(0);
            this.DiscnnectButton.Name = "DiscnnectButton";
            this.DiscnnectButton.Size = new System.Drawing.Size(32, 32);
            this.DiscnnectButton.TabIndex = 2;
            this.DiscnnectButton.UseVisualStyleBackColor = false;
            this.DiscnnectButton.Click += new System.EventHandler(this.DisconnectButton_Click);
            // 
            // DiscoveryPanel
            // 
            this.DiscoveryPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryPanel.Location = new System.Drawing.Point(129, 35);
            this.DiscoveryPanel.Margin = new System.Windows.Forms.Padding(5);
            this.DiscoveryPanel.Name = "DiscoveryPanel";
            this.DiscoveryPanel.Size = new System.Drawing.Size(879, 450);
            this.DiscoveryPanel.SplitterDistance = 293;
            this.DiscoveryPanel.TabIndex = 17;
            // 
            // TrustListPanel
            // 
            this.TrustListPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TrustListPanel.Location = new System.Drawing.Point(129, 35);
            this.TrustListPanel.Margin = new System.Windows.Forms.Padding(0);
            this.TrustListPanel.Name = "TrustListPanel";
            this.TrustListPanel.Size = new System.Drawing.Size(879, 450);
            this.TrustListPanel.TabIndex = 16;
            // 
            // CertificatePanel
            // 
            this.CertificatePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificatePanel.Location = new System.Drawing.Point(129, 35);
            this.CertificatePanel.Margin = new System.Windows.Forms.Padding(0);
            this.CertificatePanel.Name = "CertificatePanel";
            this.CertificatePanel.Size = new System.Drawing.Size(879, 450);
            this.CertificatePanel.TabIndex = 15;
            // 
            // RegistrationPanel
            // 
            this.RegistrationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationPanel.Location = new System.Drawing.Point(129, 35);
            this.RegistrationPanel.Margin = new System.Windows.Forms.Padding(0);
            this.RegistrationPanel.Name = "RegistrationPanel";
            this.RegistrationPanel.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
            this.RegistrationPanel.Size = new System.Drawing.Size(879, 450);
            this.RegistrationPanel.TabIndex = 1;
            this.RegistrationPanel.SelectServer += new System.EventHandler<Opc.Ua.Gds.Client.SelectServerEventArgs>(this.RegistrationPanel_ServerRequired);
            this.RegistrationPanel.RegisteredApplicationChanged += new System.EventHandler<Opc.Ua.Gds.Client.RegisteredApplicationChangedEventArgs>(this.RegistrationPanel_RegisteredApplicationChangedAsync);
            // 
            // ServerStatusPanel
            // 
            this.ServerStatusPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerStatusPanel.Location = new System.Drawing.Point(129, 35);
            this.ServerStatusPanel.Margin = new System.Windows.Forms.Padding(4);
            this.ServerStatusPanel.Name = "ServerStatusPanel";
            this.ServerStatusPanel.Padding = new System.Windows.Forms.Padding(0, 5, 0, 0);
            this.ServerStatusPanel.Size = new System.Drawing.Size(879, 450);
            this.ServerStatusPanel.TabIndex = 14;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1008, 507);
            this.Controls.Add(this.DiscoveryPanel);
            this.Controls.Add(this.TrustListPanel);
            this.Controls.Add(this.CertificatePanel);
            this.Controls.Add(this.RegistrationPanel);
            this.Controls.Add(this.ServerStatusPanel);
            this.Controls.Add(this.ServerUrlPanel);
            this.Controls.Add(this.LeftPanel);
            this.Controls.Add(this.BottomStatusStrip);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "MainForm";
            this.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
            this.Text = "Global Discovery Client";
            this.BottomStatusStrip.ResumeLayout(false);
            this.BottomStatusStrip.PerformLayout();
            this.LeftPanel.ResumeLayout(false);
            this.ServerUrlPanel.ResumeLayout(false);
            this.ServerUrlPanel.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.StatusStrip BottomStatusStrip;
        private System.Windows.Forms.Panel LeftPanel;
        private System.Windows.Forms.Button TrustListButton;
        private System.Windows.Forms.Button CertificateButton;
        private System.Windows.Forms.Button RegistrationButton;
        private System.Windows.Forms.Button ServerStatusButton;
        private System.Windows.Forms.Button SelectServerButton;
        private System.Windows.Forms.Panel ServerUrlPanel;
        private System.Windows.Forms.Button ConnectButton;
        private System.Windows.Forms.Button DiscnnectButton;
        private System.Windows.Forms.TextBox ServerUrlTextBox;
        private System.Windows.Forms.ToolStripStatusLabel ServerStatusIcon;
        private System.Windows.Forms.ToolStripStatusLabel ServerStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel ServerStatusTime;
        private RegisterApplicationControl RegistrationPanel;
        private Opc.Ua.Gds.Client.Controls.ServerStatusControl ServerStatusPanel;
        private ApplicationCertificateControl CertificatePanel;
        private ApplicationTrustListControl TrustListPanel;
        private System.Windows.Forms.Button ConfigurationButton;
        private System.Windows.Forms.Button DiscoveryButton;
        private Opc.Ua.Gds.Client.Controls.DiscoveryControl DiscoveryPanel;
        private System.Windows.Forms.Button HttpsTrustListButton;
        private System.Windows.Forms.Button HttpsCertificateButton;
        private System.Windows.Forms.Button SelectGdsButton;
        private System.Windows.Forms.ToolStripStatusLabel GdsServerStatusIcon;
        private System.Windows.Forms.ToolStripStatusLabel GdsServerStatusLabel;
        private System.Windows.Forms.ToolStripStatusLabel GdsServerStatusTime;
    }
}
