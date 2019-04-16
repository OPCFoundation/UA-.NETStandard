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

namespace Quickstarts.UserAuthenticationClient
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
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.ServerMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DiscoverMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_ConnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Server_DisconnectMI = new System.Windows.Forms.ToolStripMenuItem();
            this.HelpMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Help_ContentsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.StatusBar = new System.Windows.Forms.StatusStrip();
            this.MainPN = new System.Windows.Forms.Panel();
            this.AccessControlCheckGB = new System.Windows.Forms.GroupBox();
            this.LogFilePathLB = new System.Windows.Forms.Label();
            this.LogFilePathTB = new System.Windows.Forms.TextBox();
            this.ChangeLogFileBTN = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.UserNameTAB = new System.Windows.Forms.TabPage();
            this.PasswordTB = new System.Windows.Forms.TextBox();
            this.UserNameLB = new System.Windows.Forms.Label();
            this.UserNameTokenLB = new System.Windows.Forms.Label();
            this.PasswordLB = new System.Windows.Forms.Label();
            this.UserNameTB = new System.Windows.Forms.TextBox();
            this.UserNameImpersonateBTN = new System.Windows.Forms.Button();
            this.CertificateTAB = new System.Windows.Forms.TabPage();
            this.CertificateBrowseBTN = new System.Windows.Forms.Button();
            this.CertificatePasswordTB = new System.Windows.Forms.TextBox();
            this.CertificateTokenLB = new System.Windows.Forms.Label();
            this.CertificateLB = new System.Windows.Forms.Label();
            this.CertificateImpersonateBTN = new System.Windows.Forms.Button();
            this.CertificatePasswordLB = new System.Windows.Forms.Label();
            this.CertificateTB = new System.Windows.Forms.TextBox();
            this.KerberosTAB = new System.Windows.Forms.TabPage();
            this.KerberosDomainLB = new System.Windows.Forms.Label();
            this.KerberosDomainTB = new System.Windows.Forms.TextBox();
            this.KereberosTokenLB = new System.Windows.Forms.Label();
            this.KerberosPasswordTB = new System.Windows.Forms.TextBox();
            this.KerberosUserNameTB = new System.Windows.Forms.TextBox();
            this.KerberosUserNameLB = new System.Windows.Forms.Label();
            this.KerberosImpersonateBTN = new System.Windows.Forms.Button();
            this.KerberosPasswordLB = new System.Windows.Forms.Label();
            this.AnonymousTAB = new System.Windows.Forms.TabPage();
            this.AnonymousImpersonateBTN = new System.Windows.Forms.Button();
            this.AnonymousTokenLB = new System.Windows.Forms.Label();
            this.PreferredLocalesTB = new System.Windows.Forms.TextBox();
            this.PreferredLocalesLB = new System.Windows.Forms.Label();
            this.ConnectServerCTRL = new Opc.Ua.Client.Controls.ConnectServerCtrl();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.AccessControlCheckGB.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.UserNameTAB.SuspendLayout();
            this.CertificateTAB.SuspendLayout();
            this.KerberosTAB.SuspendLayout();
            this.AnonymousTAB.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ServerMI,
            this.HelpMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(809, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // ServerMI
            // 
            this.ServerMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Server_DiscoverMI,
            this.Server_ConnectMI,
            this.Server_DisconnectMI});
            this.ServerMI.Name = "ServerMI";
            this.ServerMI.Size = new System.Drawing.Size(51, 20);
            this.ServerMI.Text = "Server";
            // 
            // Server_DiscoverMI
            // 
            this.Server_DiscoverMI.Name = "Server_DiscoverMI";
            this.Server_DiscoverMI.Size = new System.Drawing.Size(133, 22);
            this.Server_DiscoverMI.Text = "Discover...";
            this.Server_DiscoverMI.Click += new System.EventHandler(this.Server_DiscoverMI_Click);
            // 
            // Server_ConnectMI
            // 
            this.Server_ConnectMI.Name = "Server_ConnectMI";
            this.Server_ConnectMI.Size = new System.Drawing.Size(133, 22);
            this.Server_ConnectMI.Text = "Connect";
            this.Server_ConnectMI.Click += new System.EventHandler(this.Server_ConnectMI_ClickAsync);
            // 
            // Server_DisconnectMI
            // 
            this.Server_DisconnectMI.Name = "Server_DisconnectMI";
            this.Server_DisconnectMI.Size = new System.Drawing.Size(133, 22);
            this.Server_DisconnectMI.Text = "Disconnect";
            this.Server_DisconnectMI.Click += new System.EventHandler(this.Server_DisconnectMI_Click);
            // 
            // HelpMI
            // 
            this.HelpMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Help_ContentsMI});
            this.HelpMI.Name = "HelpMI";
            this.HelpMI.Size = new System.Drawing.Size(44, 20);
            this.HelpMI.Text = "Help";
            // 
            // Help_ContentsMI
            // 
            this.Help_ContentsMI.Name = "Help_ContentsMI";
            this.Help_ContentsMI.Size = new System.Drawing.Size(122, 22);
            this.Help_ContentsMI.Text = "Contents";
            // 
            // StatusBar
            // 
            this.StatusBar.Location = new System.Drawing.Point(0, 422);
            this.StatusBar.Name = "StatusBar";
            this.StatusBar.Size = new System.Drawing.Size(809, 22);
            this.StatusBar.TabIndex = 2;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.AccessControlCheckGB);
            this.MainPN.Controls.Add(this.tabControl1);
            this.MainPN.Controls.Add(this.PreferredLocalesTB);
            this.MainPN.Controls.Add(this.PreferredLocalesLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 47);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(809, 375);
            this.MainPN.TabIndex = 3;
            // 
            // AccessControlCheckGB
            // 
            this.AccessControlCheckGB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.AccessControlCheckGB.Controls.Add(this.LogFilePathLB);
            this.AccessControlCheckGB.Controls.Add(this.LogFilePathTB);
            this.AccessControlCheckGB.Controls.Add(this.ChangeLogFileBTN);
            this.AccessControlCheckGB.Location = new System.Drawing.Point(17, 306);
            this.AccessControlCheckGB.Name = "AccessControlCheckGB";
            this.AccessControlCheckGB.Size = new System.Drawing.Size(774, 62);
            this.AccessControlCheckGB.TabIndex = 26;
            this.AccessControlCheckGB.TabStop = false;
            this.AccessControlCheckGB.Text = "Access Control Check";
            // 
            // LogFilePathLB
            // 
            this.LogFilePathLB.AutoSize = true;
            this.LogFilePathLB.Location = new System.Drawing.Point(7, 27);
            this.LogFilePathLB.Name = "LogFilePathLB";
            this.LogFilePathLB.Size = new System.Drawing.Size(69, 13);
            this.LogFilePathLB.TabIndex = 24;
            this.LogFilePathLB.Text = "Log File Path";
            // 
            // LogFilePathTB
            // 
            this.LogFilePathTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.LogFilePathTB.Location = new System.Drawing.Point(80, 24);
            this.LogFilePathTB.Name = "LogFilePathTB";
            this.LogFilePathTB.Size = new System.Drawing.Size(615, 20);
            this.LogFilePathTB.TabIndex = 25;
            // 
            // ChangeLogFileBTN
            // 
            this.ChangeLogFileBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ChangeLogFileBTN.Location = new System.Drawing.Point(701, 22);
            this.ChangeLogFileBTN.Name = "ChangeLogFileBTN";
            this.ChangeLogFileBTN.Size = new System.Drawing.Size(67, 23);
            this.ChangeLogFileBTN.TabIndex = 14;
            this.ChangeLogFileBTN.Text = "Change";
            this.ChangeLogFileBTN.UseVisualStyleBackColor = true;
            this.ChangeLogFileBTN.Click += new System.EventHandler(this.ChangeLogFileBTN_Click);
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.UserNameTAB);
            this.tabControl1.Controls.Add(this.CertificateTAB);
            this.tabControl1.Controls.Add(this.KerberosTAB);
            this.tabControl1.Controls.Add(this.AnonymousTAB);
            this.tabControl1.Location = new System.Drawing.Point(22, 20);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(769, 239);
            this.tabControl1.TabIndex = 23;
            // 
            // UserNameTAB
            // 
            this.UserNameTAB.Controls.Add(this.PasswordTB);
            this.UserNameTAB.Controls.Add(this.UserNameLB);
            this.UserNameTAB.Controls.Add(this.UserNameTokenLB);
            this.UserNameTAB.Controls.Add(this.PasswordLB);
            this.UserNameTAB.Controls.Add(this.UserNameTB);
            this.UserNameTAB.Controls.Add(this.UserNameImpersonateBTN);
            this.UserNameTAB.Location = new System.Drawing.Point(4, 22);
            this.UserNameTAB.Name = "UserNameTAB";
            this.UserNameTAB.Padding = new System.Windows.Forms.Padding(3);
            this.UserNameTAB.Size = new System.Drawing.Size(761, 213);
            this.UserNameTAB.TabIndex = 0;
            this.UserNameTAB.Text = "User Name";
            this.UserNameTAB.UseVisualStyleBackColor = true;
            // 
            // PasswordTB
            // 
            this.PasswordTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.PasswordTB.Location = new System.Drawing.Point(76, 32);
            this.PasswordTB.Name = "PasswordTB";
            this.PasswordTB.PasswordChar = '*';
            this.PasswordTB.Size = new System.Drawing.Size(679, 20);
            this.PasswordTB.TabIndex = 6;
            // 
            // UserNameLB
            // 
            this.UserNameLB.AutoSize = true;
            this.UserNameLB.Location = new System.Drawing.Point(5, 9);
            this.UserNameLB.Name = "UserNameLB";
            this.UserNameLB.Size = new System.Drawing.Size(60, 13);
            this.UserNameLB.TabIndex = 1;
            this.UserNameLB.Text = "User Name";
            // 
            // UserNameTokenLB
            // 
            this.UserNameTokenLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UserNameTokenLB.Location = new System.Drawing.Point(8, 96);
            this.UserNameTokenLB.Name = "UserNameTokenLB";
            this.UserNameTokenLB.Size = new System.Drawing.Size(747, 114);
            this.UserNameTokenLB.TabIndex = 19;
            this.UserNameTokenLB.Text = "UserNameTokens";
            // 
            // PasswordLB
            // 
            this.PasswordLB.AutoSize = true;
            this.PasswordLB.Location = new System.Drawing.Point(5, 35);
            this.PasswordLB.Name = "PasswordLB";
            this.PasswordLB.Size = new System.Drawing.Size(53, 13);
            this.PasswordLB.TabIndex = 2;
            this.PasswordLB.Text = "Password";
            // 
            // UserNameTB
            // 
            this.UserNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UserNameTB.Location = new System.Drawing.Point(76, 6);
            this.UserNameTB.Name = "UserNameTB";
            this.UserNameTB.Size = new System.Drawing.Size(679, 20);
            this.UserNameTB.TabIndex = 5;
            // 
            // UserNameImpersonateBTN
            // 
            this.UserNameImpersonateBTN.Location = new System.Drawing.Point(6, 58);
            this.UserNameImpersonateBTN.Name = "UserNameImpersonateBTN";
            this.UserNameImpersonateBTN.Size = new System.Drawing.Size(91, 23);
            this.UserNameImpersonateBTN.TabIndex = 11;
            this.UserNameImpersonateBTN.Text = "Impersonate";
            this.UserNameImpersonateBTN.UseVisualStyleBackColor = true;
            this.UserNameImpersonateBTN.Click += new System.EventHandler(this.UserNameImpersonateBTN_Click);
            // 
            // CertificateTAB
            // 
            this.CertificateTAB.Controls.Add(this.CertificateBrowseBTN);
            this.CertificateTAB.Controls.Add(this.CertificatePasswordTB);
            this.CertificateTAB.Controls.Add(this.CertificateTokenLB);
            this.CertificateTAB.Controls.Add(this.CertificateLB);
            this.CertificateTAB.Controls.Add(this.CertificateImpersonateBTN);
            this.CertificateTAB.Controls.Add(this.CertificatePasswordLB);
            this.CertificateTAB.Controls.Add(this.CertificateTB);
            this.CertificateTAB.Location = new System.Drawing.Point(4, 22);
            this.CertificateTAB.Name = "CertificateTAB";
            this.CertificateTAB.Padding = new System.Windows.Forms.Padding(3);
            this.CertificateTAB.Size = new System.Drawing.Size(761, 213);
            this.CertificateTAB.TabIndex = 1;
            this.CertificateTAB.Text = "Certificate";
            this.CertificateTAB.UseVisualStyleBackColor = true;
            // 
            // CertificateBrowseBTN
            // 
            this.CertificateBrowseBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificateBrowseBTN.Location = new System.Drawing.Point(347, 3);
            this.CertificateBrowseBTN.Name = "CertificateBrowseBTN";
            this.CertificateBrowseBTN.Size = new System.Drawing.Size(32, 20);
            this.CertificateBrowseBTN.TabIndex = 18;
            this.CertificateBrowseBTN.Text = "...";
            this.CertificateBrowseBTN.UseVisualStyleBackColor = true;
            // 
            // CertificatePasswordTB
            // 
            this.CertificatePasswordTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificatePasswordTB.Location = new System.Drawing.Point(74, 32);
            this.CertificatePasswordTB.Name = "CertificatePasswordTB";
            this.CertificatePasswordTB.PasswordChar = '*';
            this.CertificatePasswordTB.Size = new System.Drawing.Size(305, 20);
            this.CertificatePasswordTB.TabIndex = 6;
            // 
            // CertificateTokenLB
            // 
            this.CertificateTokenLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificateTokenLB.Location = new System.Drawing.Point(6, 94);
            this.CertificateTokenLB.Name = "CertificateTokenLB";
            this.CertificateTokenLB.Size = new System.Drawing.Size(376, 116);
            this.CertificateTokenLB.TabIndex = 20;
            this.CertificateTokenLB.Text = "Certificate Token";
            // 
            // CertificateLB
            // 
            this.CertificateLB.AutoSize = true;
            this.CertificateLB.Location = new System.Drawing.Point(4, 9);
            this.CertificateLB.Name = "CertificateLB";
            this.CertificateLB.Size = new System.Drawing.Size(54, 13);
            this.CertificateLB.TabIndex = 1;
            this.CertificateLB.Text = "Certfiicate";
            // 
            // CertificateImpersonateBTN
            // 
            this.CertificateImpersonateBTN.Location = new System.Drawing.Point(7, 58);
            this.CertificateImpersonateBTN.Name = "CertificateImpersonateBTN";
            this.CertificateImpersonateBTN.Size = new System.Drawing.Size(91, 23);
            this.CertificateImpersonateBTN.TabIndex = 11;
            this.CertificateImpersonateBTN.Text = "Impersonate";
            this.CertificateImpersonateBTN.UseVisualStyleBackColor = true;
            this.CertificateImpersonateBTN.Click += new System.EventHandler(this.CertificateImpersonateBTN_Click);
            // 
            // CertificatePasswordLB
            // 
            this.CertificatePasswordLB.AutoSize = true;
            this.CertificatePasswordLB.Location = new System.Drawing.Point(4, 35);
            this.CertificatePasswordLB.Name = "CertificatePasswordLB";
            this.CertificatePasswordLB.Size = new System.Drawing.Size(53, 13);
            this.CertificatePasswordLB.TabIndex = 2;
            this.CertificatePasswordLB.Text = "Password";
            // 
            // CertificateTB
            // 
            this.CertificateTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.CertificateTB.Location = new System.Drawing.Point(74, 6);
            this.CertificateTB.Name = "CertificateTB";
            this.CertificateTB.Size = new System.Drawing.Size(267, 20);
            this.CertificateTB.TabIndex = 5;
            // 
            // KerberosTAB
            // 
            this.KerberosTAB.Controls.Add(this.KerberosDomainLB);
            this.KerberosTAB.Controls.Add(this.KerberosDomainTB);
            this.KerberosTAB.Controls.Add(this.KereberosTokenLB);
            this.KerberosTAB.Controls.Add(this.KerberosPasswordTB);
            this.KerberosTAB.Controls.Add(this.KerberosUserNameTB);
            this.KerberosTAB.Controls.Add(this.KerberosUserNameLB);
            this.KerberosTAB.Controls.Add(this.KerberosImpersonateBTN);
            this.KerberosTAB.Controls.Add(this.KerberosPasswordLB);
            this.KerberosTAB.Location = new System.Drawing.Point(4, 22);
            this.KerberosTAB.Name = "KerberosTAB";
            this.KerberosTAB.Padding = new System.Windows.Forms.Padding(3);
            this.KerberosTAB.Size = new System.Drawing.Size(761, 213);
            this.KerberosTAB.TabIndex = 2;
            this.KerberosTAB.Text = "Kerberos";
            this.KerberosTAB.UseVisualStyleBackColor = true;
            // 
            // KerberosDomainLB
            // 
            this.KerberosDomainLB.AutoSize = true;
            this.KerberosDomainLB.Location = new System.Drawing.Point(7, 68);
            this.KerberosDomainLB.Name = "KerberosDomainLB";
            this.KerberosDomainLB.Size = new System.Drawing.Size(43, 13);
            this.KerberosDomainLB.TabIndex = 12;
            this.KerberosDomainLB.Text = "Domain";
            // 
            // KerberosDomainTB
            // 
            this.KerberosDomainTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.KerberosDomainTB.Location = new System.Drawing.Point(77, 65);
            this.KerberosDomainTB.Name = "KerberosDomainTB";
            this.KerberosDomainTB.Size = new System.Drawing.Size(302, 20);
            this.KerberosDomainTB.TabIndex = 13;
            // 
            // KereberosTokenLB
            // 
            this.KereberosTokenLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.KereberosTokenLB.Location = new System.Drawing.Point(7, 126);
            this.KereberosTokenLB.Name = "KereberosTokenLB";
            this.KereberosTokenLB.Size = new System.Drawing.Size(372, 84);
            this.KereberosTokenLB.TabIndex = 21;
            this.KereberosTokenLB.Text = "Kerberos Token";
            // 
            // KerberosPasswordTB
            // 
            this.KerberosPasswordTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.KerberosPasswordTB.Location = new System.Drawing.Point(77, 39);
            this.KerberosPasswordTB.Name = "KerberosPasswordTB";
            this.KerberosPasswordTB.PasswordChar = '*';
            this.KerberosPasswordTB.Size = new System.Drawing.Size(302, 20);
            this.KerberosPasswordTB.TabIndex = 6;
            // 
            // KerberosUserNameTB
            // 
            this.KerberosUserNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.KerberosUserNameTB.Location = new System.Drawing.Point(77, 13);
            this.KerberosUserNameTB.Name = "KerberosUserNameTB";
            this.KerberosUserNameTB.Size = new System.Drawing.Size(302, 20);
            this.KerberosUserNameTB.TabIndex = 5;
            // 
            // KerberosUserNameLB
            // 
            this.KerberosUserNameLB.AutoSize = true;
            this.KerberosUserNameLB.Location = new System.Drawing.Point(7, 16);
            this.KerberosUserNameLB.Name = "KerberosUserNameLB";
            this.KerberosUserNameLB.Size = new System.Drawing.Size(60, 13);
            this.KerberosUserNameLB.TabIndex = 1;
            this.KerberosUserNameLB.Text = "User Name";
            // 
            // KerberosImpersonateBTN
            // 
            this.KerberosImpersonateBTN.Location = new System.Drawing.Point(10, 91);
            this.KerberosImpersonateBTN.Name = "KerberosImpersonateBTN";
            this.KerberosImpersonateBTN.Size = new System.Drawing.Size(91, 23);
            this.KerberosImpersonateBTN.TabIndex = 11;
            this.KerberosImpersonateBTN.Text = "Impersonate";
            this.KerberosImpersonateBTN.UseVisualStyleBackColor = true;
            this.KerberosImpersonateBTN.Click += new System.EventHandler(this.KerberosImpersonateBTN_Click);
            // 
            // KerberosPasswordLB
            // 
            this.KerberosPasswordLB.AutoSize = true;
            this.KerberosPasswordLB.Location = new System.Drawing.Point(7, 42);
            this.KerberosPasswordLB.Name = "KerberosPasswordLB";
            this.KerberosPasswordLB.Size = new System.Drawing.Size(53, 13);
            this.KerberosPasswordLB.TabIndex = 2;
            this.KerberosPasswordLB.Text = "Password";
            // 
            // AnonymousTAB
            // 
            this.AnonymousTAB.Controls.Add(this.AnonymousImpersonateBTN);
            this.AnonymousTAB.Controls.Add(this.AnonymousTokenLB);
            this.AnonymousTAB.Location = new System.Drawing.Point(4, 22);
            this.AnonymousTAB.Name = "AnonymousTAB";
            this.AnonymousTAB.Padding = new System.Windows.Forms.Padding(3);
            this.AnonymousTAB.Size = new System.Drawing.Size(761, 213);
            this.AnonymousTAB.TabIndex = 3;
            this.AnonymousTAB.Text = "Anonymous";
            this.AnonymousTAB.UseVisualStyleBackColor = true;
            // 
            // AnonymousImpersonateBTN
            // 
            this.AnonymousImpersonateBTN.Location = new System.Drawing.Point(6, 6);
            this.AnonymousImpersonateBTN.Name = "AnonymousImpersonateBTN";
            this.AnonymousImpersonateBTN.Size = new System.Drawing.Size(91, 23);
            this.AnonymousImpersonateBTN.TabIndex = 11;
            this.AnonymousImpersonateBTN.Text = "Impersonate";
            this.AnonymousImpersonateBTN.UseVisualStyleBackColor = true;
            this.AnonymousImpersonateBTN.Click += new System.EventHandler(this.AnonymousImpersonateBTN_Click);
            // 
            // AnonymousTokenLB
            // 
            this.AnonymousTokenLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.AnonymousTokenLB.Location = new System.Drawing.Point(6, 32);
            this.AnonymousTokenLB.Name = "AnonymousTokenLB";
            this.AnonymousTokenLB.Size = new System.Drawing.Size(373, 178);
            this.AnonymousTokenLB.TabIndex = 22;
            this.AnonymousTokenLB.Text = "Anonymous Token";
            // 
            // PreferredLocalesTB
            // 
            this.PreferredLocalesTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.PreferredLocalesTB.Location = new System.Drawing.Point(115, 265);
            this.PreferredLocalesTB.Name = "PreferredLocalesTB";
            this.PreferredLocalesTB.Size = new System.Drawing.Size(676, 20);
            this.PreferredLocalesTB.TabIndex = 13;
            // 
            // PreferredLocalesLB
            // 
            this.PreferredLocalesLB.AutoSize = true;
            this.PreferredLocalesLB.Location = new System.Drawing.Point(19, 268);
            this.PreferredLocalesLB.Name = "PreferredLocalesLB";
            this.PreferredLocalesLB.Size = new System.Drawing.Size(90, 13);
            this.PreferredLocalesLB.TabIndex = 12;
            this.PreferredLocalesLB.Text = "Preferred Locales";
            // 
            // ConnectServerCTRL
            // 
            this.ConnectServerCTRL.Configuration = null;
            this.ConnectServerCTRL.Dock = System.Windows.Forms.DockStyle.Top;
            this.ConnectServerCTRL.Location = new System.Drawing.Point(0, 24);
            this.ConnectServerCTRL.MaximumSize = new System.Drawing.Size(2048, 23);
            this.ConnectServerCTRL.MinimumSize = new System.Drawing.Size(500, 23);
            this.ConnectServerCTRL.Name = "ConnectServerCTRL";
            this.ConnectServerCTRL.Size = new System.Drawing.Size(809, 23);
            this.ConnectServerCTRL.StatusStrip = this.StatusBar;
            this.ConnectServerCTRL.TabIndex = 4;
            this.ConnectServerCTRL.UseSecurity = true;
            this.ConnectServerCTRL.ConnectComplete += new System.EventHandler(this.Server_ConnectComplete);
            this.ConnectServerCTRL.ReconnectStarting += new System.EventHandler(this.Server_ReconnectStarting);
            this.ConnectServerCTRL.ReconnectComplete += new System.EventHandler(this.Server_ReconnectComplete);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(809, 444);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ConnectServerCTRL);
            this.Controls.Add(this.StatusBar);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "Quickstart UserAuthentication Client";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.AccessControlCheckGB.ResumeLayout(false);
            this.AccessControlCheckGB.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.UserNameTAB.ResumeLayout(false);
            this.UserNameTAB.PerformLayout();
            this.CertificateTAB.ResumeLayout(false);
            this.CertificateTAB.PerformLayout();
            this.KerberosTAB.ResumeLayout(false);
            this.KerberosTAB.PerformLayout();
            this.AnonymousTAB.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.StatusStrip StatusBar;
        private System.Windows.Forms.ToolStripMenuItem ServerMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DiscoverMI;
        private System.Windows.Forms.ToolStripMenuItem Server_ConnectMI;
        private System.Windows.Forms.ToolStripMenuItem Server_DisconnectMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ToolStripMenuItem HelpMI;
        private System.Windows.Forms.ToolStripMenuItem Help_ContentsMI;
        private System.Windows.Forms.Button UserNameImpersonateBTN;
        private System.Windows.Forms.TextBox PasswordTB;
        private System.Windows.Forms.TextBox UserNameTB;
        private System.Windows.Forms.Label PasswordLB;
        private System.Windows.Forms.Label UserNameLB;
        private System.Windows.Forms.TextBox PreferredLocalesTB;
        private System.Windows.Forms.Label PreferredLocalesLB;
        private System.Windows.Forms.TextBox KerberosPasswordTB;
        private System.Windows.Forms.Label KerberosUserNameLB;
        private System.Windows.Forms.Label KerberosPasswordLB;
        private System.Windows.Forms.TextBox KerberosUserNameTB;
        private System.Windows.Forms.Button KerberosImpersonateBTN;
        private System.Windows.Forms.TextBox CertificatePasswordTB;
        private System.Windows.Forms.Label CertificateLB;
        private System.Windows.Forms.Label CertificatePasswordLB;
        private System.Windows.Forms.TextBox CertificateTB;
        private System.Windows.Forms.Button CertificateImpersonateBTN;
        private System.Windows.Forms.Label KerberosDomainLB;
        private System.Windows.Forms.TextBox KerberosDomainTB;
        private System.Windows.Forms.Button CertificateBrowseBTN;
        private System.Windows.Forms.Button AnonymousImpersonateBTN;
        private System.Windows.Forms.Label UserNameTokenLB;
        private System.Windows.Forms.Label CertificateTokenLB;
        private System.Windows.Forms.Label AnonymousTokenLB;
        private System.Windows.Forms.Label KereberosTokenLB;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage UserNameTAB;
        private System.Windows.Forms.TabPage CertificateTAB;
        private System.Windows.Forms.TabPage KerberosTAB;
        private System.Windows.Forms.TabPage AnonymousTAB;
        private System.Windows.Forms.TextBox LogFilePathTB;
        private System.Windows.Forms.Label LogFilePathLB;
        private System.Windows.Forms.Button ChangeLogFileBTN;
        private System.Windows.Forms.GroupBox AccessControlCheckGB;
        private Opc.Ua.Client.Controls.ConnectServerCtrl ConnectServerCTRL;
    }
}
