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

namespace Opc.Ua.Gds.Client.Controls
{
    partial class SelectPushServerDialog
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
            this.ButtonPanel = new System.Windows.Forms.Panel();
            this.label1 = new System.Windows.Forms.Label();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.UserNameCredentialsRB = new System.Windows.Forms.RadioButton();
            this.IdentityProviderRB = new System.Windows.Forms.RadioButton();
            this.ClientCredentialsRB = new System.Windows.Forms.RadioButton();
            this.CloseButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.ServersGroupBox = new System.Windows.Forms.GroupBox();
            this.ServersListBox = new System.Windows.Forms.ListBox();
            this.SelectedServerGroupBox = new System.Windows.Forms.GroupBox();
            this.ServerUrlTextBox = new System.Windows.Forms.TextBox();
            this.ButtonPanel.SuspendLayout();
            this.flowLayoutPanel1.SuspendLayout();
            this.MainPanel.SuspendLayout();
            this.ServersGroupBox.SuspendLayout();
            this.SelectedServerGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonPanel
            // 
            this.ButtonPanel.Controls.Add(this.label1);
            this.ButtonPanel.Controls.Add(this.flowLayoutPanel1);
            this.ButtonPanel.Controls.Add(this.CloseButton);
            this.ButtonPanel.Controls.Add(this.OkButton);
            this.ButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonPanel.Location = new System.Drawing.Point(0, 192);
            this.ButtonPanel.Name = "ButtonPanel";
            this.ButtonPanel.Size = new System.Drawing.Size(632, 30);
            this.ButtonPanel.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(83, 8);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(131, 13);
            this.label1.TabIndex = 1;
            this.label1.Text = "Admin Credentials Source:";
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.UserNameCredentialsRB);
            this.flowLayoutPanel1.Controls.Add(this.IdentityProviderRB);
            this.flowLayoutPanel1.Controls.Add(this.ClientCredentialsRB);
            this.flowLayoutPanel1.Location = new System.Drawing.Point(212, 4);
            this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(2);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(301, 24);
            this.flowLayoutPanel1.TabIndex = 2;
            // 
            // UserNameCredentialsRB
            // 
            this.UserNameCredentialsRB.AutoSize = true;
            this.UserNameCredentialsRB.Location = new System.Drawing.Point(2, 2);
            this.UserNameCredentialsRB.Margin = new System.Windows.Forms.Padding(2);
            this.UserNameCredentialsRB.Name = "UserNameCredentialsRB";
            this.UserNameCredentialsRB.Size = new System.Drawing.Size(78, 17);
            this.UserNameCredentialsRB.TabIndex = 0;
            this.UserNameCredentialsRB.TabStop = true;
            this.UserNameCredentialsRB.Text = "User Name";
            this.UserNameCredentialsRB.UseVisualStyleBackColor = true;
            // 
            // IdentityProviderRB
            // 
            this.IdentityProviderRB.AutoSize = true;
            this.IdentityProviderRB.Location = new System.Drawing.Point(84, 2);
            this.IdentityProviderRB.Margin = new System.Windows.Forms.Padding(2);
            this.IdentityProviderRB.Name = "IdentityProviderRB";
            this.IdentityProviderRB.Size = new System.Drawing.Size(101, 17);
            this.IdentityProviderRB.TabIndex = 2;
            this.IdentityProviderRB.TabStop = true;
            this.IdentityProviderRB.Text = "Identity Provider";
            this.IdentityProviderRB.UseVisualStyleBackColor = true;
            // 
            // ClientCredentialsRB
            // 
            this.ClientCredentialsRB.AutoSize = true;
            this.ClientCredentialsRB.Location = new System.Drawing.Point(189, 2);
            this.ClientCredentialsRB.Margin = new System.Windows.Forms.Padding(2);
            this.ClientCredentialsRB.Name = "ClientCredentialsRB";
            this.ClientCredentialsRB.Size = new System.Drawing.Size(106, 17);
            this.ClientCredentialsRB.TabIndex = 3;
            this.ClientCredentialsRB.TabStop = true;
            this.ClientCredentialsRB.Text = "Client Credentials";
            this.ClientCredentialsRB.UseVisualStyleBackColor = true;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseButton.Location = new System.Drawing.Point(554, 3);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 24);
            this.CloseButton.TabIndex = 1;
            this.CloseButton.Text = "Cancel";
            this.CloseButton.UseVisualStyleBackColor = true;
            // 
            // OkButton
            // 
            this.OkButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkButton.Location = new System.Drawing.Point(3, 3);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(75, 24);
            this.OkButton.TabIndex = 0;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            this.OkButton.Click += new System.EventHandler(this.OkButton_Click);
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.ServersGroupBox);
            this.MainPanel.Controls.Add(this.SelectedServerGroupBox);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Padding = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.MainPanel.Size = new System.Drawing.Size(632, 192);
            this.MainPanel.TabIndex = 1;
            // 
            // ServersGroupBox
            // 
            this.ServersGroupBox.Controls.Add(this.ServersListBox);
            this.ServersGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersGroupBox.Location = new System.Drawing.Point(3, 47);
            this.ServersGroupBox.Name = "ServersGroupBox";
            this.ServersGroupBox.Size = new System.Drawing.Size(626, 145);
            this.ServersGroupBox.TabIndex = 3;
            this.ServersGroupBox.TabStop = false;
            this.ServersGroupBox.Text = "Available Servers";
            // 
            // ServersListBox
            // 
            this.ServersListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersListBox.FormattingEnabled = true;
            this.ServersListBox.Items.AddRange(new object[] {
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "0"});
            this.ServersListBox.Location = new System.Drawing.Point(3, 16);
            this.ServersListBox.Name = "ServersListBox";
            this.ServersListBox.Size = new System.Drawing.Size(620, 126);
            this.ServersListBox.TabIndex = 0;
            this.ServersListBox.SelectedIndexChanged += new System.EventHandler(this.ServersListBox_SelectedIndexChanged);
            // 
            // SelectedServerGroupBox
            // 
            this.SelectedServerGroupBox.Controls.Add(this.ServerUrlTextBox);
            this.SelectedServerGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.SelectedServerGroupBox.Location = new System.Drawing.Point(3, 3);
            this.SelectedServerGroupBox.Name = "SelectedServerGroupBox";
            this.SelectedServerGroupBox.Size = new System.Drawing.Size(626, 44);
            this.SelectedServerGroupBox.TabIndex = 4;
            this.SelectedServerGroupBox.TabStop = false;
            this.SelectedServerGroupBox.Text = "Selected Server";
            // 
            // ServerUrlTextBox
            // 
            this.ServerUrlTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerUrlTextBox.Location = new System.Drawing.Point(3, 16);
            this.ServerUrlTextBox.Name = "ServerUrlTextBox";
            this.ServerUrlTextBox.Size = new System.Drawing.Size(620, 20);
            this.ServerUrlTextBox.TabIndex = 3;
            this.ServerUrlTextBox.TextChanged += new System.EventHandler(this.ServerUrlTextBox_TextChanged);
            // 
            // SelectPushServerDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(632, 222);
            this.Controls.Add(this.MainPanel);
            this.Controls.Add(this.ButtonPanel);
            this.MaximumSize = new System.Drawing.Size(800, 267);
            this.MinimizeBox = false;
            this.Name = "SelectPushServerDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Available Servers";
            this.Load += new System.EventHandler(this.SelectPushServerDialog_Load);
            this.ButtonPanel.ResumeLayout(false);
            this.ButtonPanel.PerformLayout();
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.MainPanel.ResumeLayout(false);
            this.ServersGroupBox.ResumeLayout(false);
            this.SelectedServerGroupBox.ResumeLayout(false);
            this.SelectedServerGroupBox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonPanel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.Panel MainPanel;
        private System.Windows.Forms.ListBox ServersListBox;
        private System.Windows.Forms.GroupBox ServersGroupBox;
        private System.Windows.Forms.GroupBox SelectedServerGroupBox;
        private System.Windows.Forms.TextBox ServerUrlTextBox;
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
        private System.Windows.Forms.RadioButton UserNameCredentialsRB;
        private System.Windows.Forms.RadioButton IdentityProviderRB;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.RadioButton ClientCredentialsRB;
    }
}
