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
    partial class ConfiguredServerDlg
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
            this.RefreshBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.UserSecurityPoliciesLB = new System.Windows.Forms.Label();
            this.UserSecurityPoliciesTB = new System.Windows.Forms.TextBox();
            this.DiscoveryProfileURI = new System.Windows.Forms.Label();
            this.GatewayServerURI = new System.Windows.Forms.Label();
            this.DiscoveryProfileUriTB = new System.Windows.Forms.TextBox();
            this.GatewayServerUriTB = new System.Windows.Forms.TextBox();
            this.EndpointListLB = new System.Windows.Forms.ListBox();
            this.TransportProfileUriLB = new System.Windows.Forms.Label();
            this.ProductUriLB = new System.Windows.Forms.Label();
            this.ApplicationUriLB = new System.Windows.Forms.Label();
            this.ApplicationTypeLB = new System.Windows.Forms.Label();
            this.ApplicationNameLB = new System.Windows.Forms.Label();
            this.TransportProfileUriTB = new System.Windows.Forms.TextBox();
            this.ProductUriTB = new System.Windows.Forms.TextBox();
            this.ApplicationUriTB = new System.Windows.Forms.TextBox();
            this.ApplicationTypeTB = new System.Windows.Forms.TextBox();
            this.ApplicationNameTB = new System.Windows.Forms.TextBox();
            this.StatusTB = new System.Windows.Forms.TextBox();
            this.EncodingCB = new System.Windows.Forms.ComboBox();
            this.SecurityModeCB = new System.Windows.Forms.ComboBox();
            this.SecurityPolicyCB = new System.Windows.Forms.ComboBox();
            this.ProtocolCB = new System.Windows.Forms.ComboBox();
            this.EncodingLB = new System.Windows.Forms.Label();
            this.SecurityModeLB = new System.Windows.Forms.Label();
            this.SecurityPolicyLB = new System.Windows.Forms.Label();
            this.ProtocolLB = new System.Windows.Forms.Label();
            this.SecurityLevelLB = new System.Windows.Forms.Label();
            this.SecurityLevelTB = new System.Windows.Forms.TextBox();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.RefreshBTN);
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 370);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(799, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // RefreshBTN
            // 
            this.RefreshBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)));
            this.RefreshBTN.Location = new System.Drawing.Point(362, 4);
            this.RefreshBTN.Name = "RefreshBTN";
            this.RefreshBTN.Size = new System.Drawing.Size(75, 23);
            this.RefreshBTN.TabIndex = 2;
            this.RefreshBTN.Text = "Refresh";
            this.RefreshBTN.UseVisualStyleBackColor = true;
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
            this.CancelBTN.Location = new System.Drawing.Point(720, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.SecurityLevelTB);
            this.MainPN.Controls.Add(this.SecurityLevelLB);
            this.MainPN.Controls.Add(this.UserSecurityPoliciesLB);
            this.MainPN.Controls.Add(this.UserSecurityPoliciesTB);
            this.MainPN.Controls.Add(this.DiscoveryProfileURI);
            this.MainPN.Controls.Add(this.GatewayServerURI);
            this.MainPN.Controls.Add(this.DiscoveryProfileUriTB);
            this.MainPN.Controls.Add(this.GatewayServerUriTB);
            this.MainPN.Controls.Add(this.EndpointListLB);
            this.MainPN.Controls.Add(this.TransportProfileUriLB);
            this.MainPN.Controls.Add(this.ProductUriLB);
            this.MainPN.Controls.Add(this.ApplicationUriLB);
            this.MainPN.Controls.Add(this.ApplicationTypeLB);
            this.MainPN.Controls.Add(this.ApplicationNameLB);
            this.MainPN.Controls.Add(this.TransportProfileUriTB);
            this.MainPN.Controls.Add(this.ProductUriTB);
            this.MainPN.Controls.Add(this.ApplicationUriTB);
            this.MainPN.Controls.Add(this.ApplicationTypeTB);
            this.MainPN.Controls.Add(this.ApplicationNameTB);
            this.MainPN.Controls.Add(this.StatusTB);
            this.MainPN.Controls.Add(this.EncodingCB);
            this.MainPN.Controls.Add(this.SecurityModeCB);
            this.MainPN.Controls.Add(this.SecurityPolicyCB);
            this.MainPN.Controls.Add(this.ProtocolCB);
            this.MainPN.Controls.Add(this.EncodingLB);
            this.MainPN.Controls.Add(this.SecurityModeLB);
            this.MainPN.Controls.Add(this.SecurityPolicyLB);
            this.MainPN.Controls.Add(this.ProtocolLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(799, 401);
            this.MainPN.TabIndex = 0;
            // 
            // UserSecurityPoliciesLB
            // 
            this.UserSecurityPoliciesLB.AutoSize = true;
            this.UserSecurityPoliciesLB.Location = new System.Drawing.Point(368, 299);
            this.UserSecurityPoliciesLB.Name = "UserSecurityPoliciesLB";
            this.UserSecurityPoliciesLB.Size = new System.Drawing.Size(109, 13);
            this.UserSecurityPoliciesLB.TabIndex = 38;
            this.UserSecurityPoliciesLB.Text = "User Security Policies";
            this.UserSecurityPoliciesLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserSecurityPoliciesTB
            // 
            this.UserSecurityPoliciesTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UserSecurityPoliciesTB.Location = new System.Drawing.Point(487, 296);
            this.UserSecurityPoliciesTB.Name = "UserSecurityPoliciesTB";
            this.UserSecurityPoliciesTB.ReadOnly = true;
            this.UserSecurityPoliciesTB.Size = new System.Drawing.Size(300, 20);
            this.UserSecurityPoliciesTB.TabIndex = 37;
            // 
            // DiscoveryProfileURI
            // 
            this.DiscoveryProfileURI.AutoSize = true;
            this.DiscoveryProfileURI.Location = new System.Drawing.Point(368, 273);
            this.DiscoveryProfileURI.Name = "DiscoveryProfileURI";
            this.DiscoveryProfileURI.Size = new System.Drawing.Size(108, 13);
            this.DiscoveryProfileURI.TabIndex = 36;
            this.DiscoveryProfileURI.Text = "Discovery Profile URI";
            this.DiscoveryProfileURI.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // GatewayServerURI
            // 
            this.GatewayServerURI.AutoSize = true;
            this.GatewayServerURI.Location = new System.Drawing.Point(368, 247);
            this.GatewayServerURI.Name = "GatewayServerURI";
            this.GatewayServerURI.Size = new System.Drawing.Size(105, 13);
            this.GatewayServerURI.TabIndex = 35;
            this.GatewayServerURI.Text = "Gateway Server URI";
            this.GatewayServerURI.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DiscoveryProfileUriTB
            // 
            this.DiscoveryProfileUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DiscoveryProfileUriTB.Location = new System.Drawing.Point(487, 270);
            this.DiscoveryProfileUriTB.Name = "DiscoveryProfileUriTB";
            this.DiscoveryProfileUriTB.ReadOnly = true;
            this.DiscoveryProfileUriTB.Size = new System.Drawing.Size(300, 20);
            this.DiscoveryProfileUriTB.TabIndex = 34;
            // 
            // GatewayServerUriTB
            // 
            this.GatewayServerUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.GatewayServerUriTB.Location = new System.Drawing.Point(487, 244);
            this.GatewayServerUriTB.Name = "GatewayServerUriTB";
            this.GatewayServerUriTB.ReadOnly = true;
            this.GatewayServerUriTB.Size = new System.Drawing.Size(300, 20);
            this.GatewayServerUriTB.TabIndex = 33;
            // 
            // EndpointListLB
            // 
            this.EndpointListLB.FormattingEnabled = true;
            this.EndpointListLB.HorizontalScrollbar = true;
            this.EndpointListLB.Location = new System.Drawing.Point(12, 12);
            this.EndpointListLB.Name = "EndpointListLB";
            this.EndpointListLB.Size = new System.Drawing.Size(350, 329);
            this.EndpointListLB.TabIndex = 32;
            this.EndpointListLB.SelectedIndexChanged += new System.EventHandler(this.EndpointListLB_SelectedIndexChanged);
            // 
            // TransportProfileUriLB
            // 
            this.TransportProfileUriLB.AutoSize = true;
            this.TransportProfileUriLB.Location = new System.Drawing.Point(368, 221);
            this.TransportProfileUriLB.Name = "TransportProfileUriLB";
            this.TransportProfileUriLB.Size = new System.Drawing.Size(106, 13);
            this.TransportProfileUriLB.TabIndex = 31;
            this.TransportProfileUriLB.Text = "Transport Profile URI";
            this.TransportProfileUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriLB
            // 
            this.ProductUriLB.AutoSize = true;
            this.ProductUriLB.Location = new System.Drawing.Point(368, 195);
            this.ProductUriLB.Name = "ProductUriLB";
            this.ProductUriLB.Size = new System.Drawing.Size(66, 13);
            this.ProductUriLB.TabIndex = 30;
            this.ProductUriLB.Text = "Product URI";
            this.ProductUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriLB
            // 
            this.ApplicationUriLB.AutoSize = true;
            this.ApplicationUriLB.Location = new System.Drawing.Point(368, 169);
            this.ApplicationUriLB.Name = "ApplicationUriLB";
            this.ApplicationUriLB.Size = new System.Drawing.Size(81, 13);
            this.ApplicationUriLB.TabIndex = 29;
            this.ApplicationUriLB.Text = "Application URI";
            this.ApplicationUriLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationTypeLB
            // 
            this.ApplicationTypeLB.AutoSize = true;
            this.ApplicationTypeLB.Location = new System.Drawing.Point(368, 143);
            this.ApplicationTypeLB.Name = "ApplicationTypeLB";
            this.ApplicationTypeLB.Size = new System.Drawing.Size(86, 13);
            this.ApplicationTypeLB.TabIndex = 28;
            this.ApplicationTypeLB.Text = "Application Type";
            this.ApplicationTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameLB
            // 
            this.ApplicationNameLB.AutoSize = true;
            this.ApplicationNameLB.Location = new System.Drawing.Point(368, 117);
            this.ApplicationNameLB.Name = "ApplicationNameLB";
            this.ApplicationNameLB.Size = new System.Drawing.Size(90, 13);
            this.ApplicationNameLB.TabIndex = 27;
            this.ApplicationNameLB.Text = "Application Name";
            this.ApplicationNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TransportProfileUriTB
            // 
            this.TransportProfileUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.TransportProfileUriTB.Location = new System.Drawing.Point(487, 218);
            this.TransportProfileUriTB.Name = "TransportProfileUriTB";
            this.TransportProfileUriTB.ReadOnly = true;
            this.TransportProfileUriTB.Size = new System.Drawing.Size(300, 20);
            this.TransportProfileUriTB.TabIndex = 26;
            // 
            // ProductUriTB
            // 
            this.ProductUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProductUriTB.Location = new System.Drawing.Point(487, 192);
            this.ProductUriTB.Name = "ProductUriTB";
            this.ProductUriTB.ReadOnly = true;
            this.ProductUriTB.Size = new System.Drawing.Size(300, 20);
            this.ProductUriTB.TabIndex = 25;
            // 
            // ApplicationUriTB
            // 
            this.ApplicationUriTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplicationUriTB.Location = new System.Drawing.Point(487, 166);
            this.ApplicationUriTB.Name = "ApplicationUriTB";
            this.ApplicationUriTB.ReadOnly = true;
            this.ApplicationUriTB.Size = new System.Drawing.Size(300, 20);
            this.ApplicationUriTB.TabIndex = 24;
            // 
            // ApplicationTypeTB
            // 
            this.ApplicationTypeTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplicationTypeTB.Location = new System.Drawing.Point(487, 140);
            this.ApplicationTypeTB.Name = "ApplicationTypeTB";
            this.ApplicationTypeTB.ReadOnly = true;
            this.ApplicationTypeTB.Size = new System.Drawing.Size(300, 20);
            this.ApplicationTypeTB.TabIndex = 23;
            // 
            // ApplicationNameTB
            // 
            this.ApplicationNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ApplicationNameTB.Location = new System.Drawing.Point(487, 114);
            this.ApplicationNameTB.Name = "ApplicationNameTB";
            this.ApplicationNameTB.ReadOnly = true;
            this.ApplicationNameTB.Size = new System.Drawing.Size(300, 20);
            this.ApplicationNameTB.TabIndex = 22;
            // 
            // StatusTB
            // 
            this.StatusTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTB.Location = new System.Drawing.Point(12, 347);
            this.StatusTB.Name = "StatusTB";
            this.StatusTB.ReadOnly = true;
            this.StatusTB.Size = new System.Drawing.Size(775, 20);
            this.StatusTB.TabIndex = 21;
            // 
            // EncodingCB
            // 
            this.EncodingCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncodingCB.FormattingEnabled = true;
            this.EncodingCB.Location = new System.Drawing.Point(487, 87);
            this.EncodingCB.Name = "EncodingCB";
            this.EncodingCB.Size = new System.Drawing.Size(181, 21);
            this.EncodingCB.TabIndex = 7;
            // 
            // SecurityModeCB
            // 
            this.SecurityModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityModeCB.FormattingEnabled = true;
            this.SecurityModeCB.Location = new System.Drawing.Point(487, 33);
            this.SecurityModeCB.Name = "SecurityModeCB";
            this.SecurityModeCB.Size = new System.Drawing.Size(181, 21);
            this.SecurityModeCB.TabIndex = 3;
            this.SecurityModeCB.SelectedIndexChanged += new System.EventHandler(this.SecurityModeCB_SelectedIndexChanged);
            // 
            // SecurityPolicyCB
            // 
            this.SecurityPolicyCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityPolicyCB.FormattingEnabled = true;
            this.SecurityPolicyCB.Location = new System.Drawing.Point(487, 60);
            this.SecurityPolicyCB.Name = "SecurityPolicyCB";
            this.SecurityPolicyCB.Size = new System.Drawing.Size(181, 21);
            this.SecurityPolicyCB.TabIndex = 5;
            this.SecurityPolicyCB.SelectedIndexChanged += new System.EventHandler(this.SecurityPolicyCB_SelectedIndexChanged);
            // 
            // ProtocolCB
            // 
            this.ProtocolCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ProtocolCB.FormattingEnabled = true;
            this.ProtocolCB.Location = new System.Drawing.Point(487, 6);
            this.ProtocolCB.Name = "ProtocolCB";
            this.ProtocolCB.Size = new System.Drawing.Size(181, 21);
            this.ProtocolCB.TabIndex = 1;
            this.ProtocolCB.SelectedIndexChanged += new System.EventHandler(this.ProtocolCB_SelectedIndexChanged);
            // 
            // EncodingLB
            // 
            this.EncodingLB.AutoSize = true;
            this.EncodingLB.Location = new System.Drawing.Point(368, 90);
            this.EncodingLB.Name = "EncodingLB";
            this.EncodingLB.Size = new System.Drawing.Size(98, 13);
            this.EncodingLB.TabIndex = 6;
            this.EncodingLB.Text = "Message Encoding";
            this.EncodingLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityModeLB
            // 
            this.SecurityModeLB.AutoSize = true;
            this.SecurityModeLB.Location = new System.Drawing.Point(368, 36);
            this.SecurityModeLB.Name = "SecurityModeLB";
            this.SecurityModeLB.Size = new System.Drawing.Size(75, 13);
            this.SecurityModeLB.TabIndex = 2;
            this.SecurityModeLB.Text = "Security Mode";
            this.SecurityModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityPolicyLB
            // 
            this.SecurityPolicyLB.AutoSize = true;
            this.SecurityPolicyLB.Location = new System.Drawing.Point(368, 63);
            this.SecurityPolicyLB.Name = "SecurityPolicyLB";
            this.SecurityPolicyLB.Size = new System.Drawing.Size(76, 13);
            this.SecurityPolicyLB.TabIndex = 4;
            this.SecurityPolicyLB.Text = "Security Policy";
            this.SecurityPolicyLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProtocolLB
            // 
            this.ProtocolLB.AutoSize = true;
            this.ProtocolLB.Location = new System.Drawing.Point(368, 9);
            this.ProtocolLB.Name = "ProtocolLB";
            this.ProtocolLB.Size = new System.Drawing.Size(46, 13);
            this.ProtocolLB.TabIndex = 0;
            this.ProtocolLB.Text = "Protocol";
            this.ProtocolLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityLevelLB
            // 
            this.SecurityLevelLB.AutoSize = true;
            this.SecurityLevelLB.Location = new System.Drawing.Point(368, 325);
            this.SecurityLevelLB.Name = "SecurityLevelLB";
            this.SecurityLevelLB.Size = new System.Drawing.Size(71, 13);
            this.SecurityLevelLB.TabIndex = 39;
            this.SecurityLevelLB.Text = "SecurityLevel";
            this.SecurityLevelLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityLevelTB
            // 
            this.SecurityLevelTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SecurityLevelTB.Location = new System.Drawing.Point(487, 322);
            this.SecurityLevelTB.Name = "SecurityLevelTB";
            this.SecurityLevelTB.ReadOnly = true;
            this.SecurityLevelTB.Size = new System.Drawing.Size(300, 20);
            this.SecurityLevelTB.TabIndex = 40;
            // 
            // ConfiguredServerDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(799, 401);
            this.Controls.Add(this.ButtonsPN);
            this.Controls.Add(this.MainPN);
            this.MaximumSize = new System.Drawing.Size(1920, 439);
            this.MinimumSize = new System.Drawing.Size(16, 439);
            this.Name = "ConfiguredServerDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Server Configuration";
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
        private System.Windows.Forms.Label ProtocolLB;
        private System.Windows.Forms.Label SecurityPolicyLB;
        private System.Windows.Forms.Label EncodingLB;
        private System.Windows.Forms.Label SecurityModeLB;
        private System.Windows.Forms.ComboBox ProtocolCB;
        private System.Windows.Forms.ComboBox SecurityPolicyCB;
        private System.Windows.Forms.ComboBox SecurityModeCB;
        private System.Windows.Forms.ComboBox EncodingCB;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.TextBox StatusTB;
        private System.Windows.Forms.Label ApplicationNameLB;
        private System.Windows.Forms.TextBox TransportProfileUriTB;
        private System.Windows.Forms.TextBox ProductUriTB;
        private System.Windows.Forms.TextBox ApplicationUriTB;
        private System.Windows.Forms.TextBox ApplicationTypeTB;
        private System.Windows.Forms.TextBox ApplicationNameTB;
        private System.Windows.Forms.Label TransportProfileUriLB;
        private System.Windows.Forms.Label ProductUriLB;
        private System.Windows.Forms.Label ApplicationUriLB;
        private System.Windows.Forms.Label ApplicationTypeLB;
        private System.Windows.Forms.ListBox EndpointListLB;
        private System.Windows.Forms.Label DiscoveryProfileURI;
        private System.Windows.Forms.Label GatewayServerURI;
        private System.Windows.Forms.TextBox DiscoveryProfileUriTB;
        private System.Windows.Forms.TextBox GatewayServerUriTB;
        private System.Windows.Forms.Label UserSecurityPoliciesLB;
        private System.Windows.Forms.TextBox UserSecurityPoliciesTB;
        private System.Windows.Forms.TextBox SecurityLevelTB;
        private System.Windows.Forms.Label SecurityLevelLB;
    }
}
