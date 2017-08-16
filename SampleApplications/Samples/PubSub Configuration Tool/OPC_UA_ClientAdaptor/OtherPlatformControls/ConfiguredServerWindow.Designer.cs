namespace ClientAdaptor
{
    partial class ConfiguredServerWindow
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
            this.StatusTB = new System.Windows.Forms.TextBox();
            this.UseDefaultLimitsBTN = new System.Windows.Forms.Button();
            this.UseDefaultLimitsCB = new System.Windows.Forms.ComboBox();
            this.UseDefaultLimitsLB = new System.Windows.Forms.Label();
            this.IssuedTokenTypeLB = new System.Windows.Forms.Label();
            this.IssuedTokenTypeCB = new System.Windows.Forms.ComboBox();
            this.EncodingCB = new System.Windows.Forms.ComboBox();
            this.SecurityModeCB = new System.Windows.Forms.ComboBox();
            this.SecurityPolicyCB = new System.Windows.Forms.ComboBox();
            this.ProtocolCB = new System.Windows.Forms.ComboBox();
            this.UserTokenPolicyLB = new System.Windows.Forms.Label();
            this.UserTokenTypeCB = new System.Windows.Forms.ComboBox();
            this.EncodingLB = new System.Windows.Forms.Label();
            this.SecurityModeLB = new System.Windows.Forms.Label();
            this.SecurityPolicyLB = new System.Windows.Forms.Label();
            this.UserIdentityBTN = new System.Windows.Forms.Button();
            this.UserIdentityTB = new System.Windows.Forms.TextBox();
            this.ProtocolLB = new System.Windows.Forms.Label();
            this.UserIdentityLB = new System.Windows.Forms.Label();
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
            this.ButtonsPN.Location = new System.Drawing.Point(0, 243);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(484, 31);
            this.ButtonsPN.TabIndex = 0;
            // 
            // RefreshBTN
            // 
            this.RefreshBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)));
            this.RefreshBTN.Location = new System.Drawing.Point(205, 4);
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
            this.CancelBTN.Location = new System.Drawing.Point(405, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.StatusTB);
            //this.MainPN.Controls.Add(this.UseDefaultLimitsBTN);
            //this.MainPN.Controls.Add(this.UseDefaultLimitsCB);
            //this.MainPN.Controls.Add(this.UseDefaultLimitsLB);
            this.MainPN.Controls.Add(this.IssuedTokenTypeLB);
            this.MainPN.Controls.Add(this.IssuedTokenTypeCB);
            this.MainPN.Controls.Add(this.EncodingCB);
            this.MainPN.Controls.Add(this.SecurityModeCB);
            this.MainPN.Controls.Add(this.SecurityPolicyCB);
            this.MainPN.Controls.Add(this.ProtocolCB);
            this.MainPN.Controls.Add(this.UserTokenPolicyLB);
            this.MainPN.Controls.Add(this.UserTokenTypeCB);
            this.MainPN.Controls.Add(this.EncodingLB);
            this.MainPN.Controls.Add(this.SecurityModeLB);
            this.MainPN.Controls.Add(this.SecurityPolicyLB);
            this.MainPN.Controls.Add(this.UserIdentityBTN);
            this.MainPN.Controls.Add(this.UserIdentityTB);
            this.MainPN.Controls.Add(this.ProtocolLB);
            this.MainPN.Controls.Add(this.UserIdentityLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(484, 274);
            this.MainPN.TabIndex = 0;
            // 
            // StatusTB
            // 
            this.StatusTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.StatusTB.Location = new System.Drawing.Point(4, 221);
            this.StatusTB.Name = "StatusTB";
            this.StatusTB.ReadOnly = true;
            this.StatusTB.Size = new System.Drawing.Size(476, 20);
            this.StatusTB.TabIndex = 21;
            // 
            // UseDefaultLimitsBTN
            // 
            //this.UseDefaultLimitsBTN.Location = new System.Drawing.Point(189, 193);
            //this.UseDefaultLimitsBTN.Name = "UseDefaultLimitsBTN";
            //this.UseDefaultLimitsBTN.Size = new System.Drawing.Size(25, 22);
            //this.UseDefaultLimitsBTN.TabIndex = 20;
            //this.UseDefaultLimitsBTN.Text = "...";
            //this.UseDefaultLimitsBTN.UseVisualStyleBackColor = true;
            //// 
            //// UseDefaultLimitsCB
            //// 
            //this.UseDefaultLimitsCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            //this.UseDefaultLimitsCB.FormattingEnabled = true;
            //this.UseDefaultLimitsCB.Location = new System.Drawing.Point(123, 194);
            //this.UseDefaultLimitsCB.Name = "UseDefaultLimitsCB";
            //this.UseDefaultLimitsCB.Size = new System.Drawing.Size(60, 21);
            //this.UseDefaultLimitsCB.TabIndex = 19;
            //this.UseDefaultLimitsCB.SelectedIndexChanged += new System.EventHandler(this.OverrideLimitsCB_SelectedIndexChanged);
            //// 
            //// UseDefaultLimitsLB
            //// 
            //this.UseDefaultLimitsLB.AutoSize = true;
            //this.UseDefaultLimitsLB.Location = new System.Drawing.Point(4, 197);
            //this.UseDefaultLimitsLB.Name = "UseDefaultLimitsLB";
            //this.UseDefaultLimitsLB.Size = new System.Drawing.Size(95, 13);
            //this.UseDefaultLimitsLB.TabIndex = 18;
            //this.UseDefaultLimitsLB.Text = "User Default Limits";
            //this.UseDefaultLimitsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IssuedTokenTypeLB
            // 
            this.IssuedTokenTypeLB.AutoSize = true;
            this.IssuedTokenTypeLB.Location = new System.Drawing.Point(4, 144);
            this.IssuedTokenTypeLB.Name = "IssuedTokenTypeLB";
            this.IssuedTokenTypeLB.Size = new System.Drawing.Size(99, 13);
            this.IssuedTokenTypeLB.TabIndex = 10;
            this.IssuedTokenTypeLB.Text = "Issued Token Type";
            this.IssuedTokenTypeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IssuedTokenTypeCB
            // 
            this.IssuedTokenTypeCB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.IssuedTokenTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.IssuedTokenTypeCB.FormattingEnabled = true;
            this.IssuedTokenTypeCB.Location = new System.Drawing.Point(123, 141);
            this.IssuedTokenTypeCB.Name = "IssuedTokenTypeCB";
            this.IssuedTokenTypeCB.Size = new System.Drawing.Size(357, 21);
            this.IssuedTokenTypeCB.TabIndex = 11;
            // 
            // EncodingCB
            // 
            this.EncodingCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.EncodingCB.FormattingEnabled = true;
            this.EncodingCB.Location = new System.Drawing.Point(123, 87);
            this.EncodingCB.Name = "EncodingCB";
            this.EncodingCB.Size = new System.Drawing.Size(181, 21);
            this.EncodingCB.TabIndex = 7;
            // 
            // SecurityModeCB
            // 
            this.SecurityModeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityModeCB.FormattingEnabled = true;
            this.SecurityModeCB.Location = new System.Drawing.Point(123, 33);
            this.SecurityModeCB.Name = "SecurityModeCB";
            this.SecurityModeCB.Size = new System.Drawing.Size(181, 21);
            this.SecurityModeCB.TabIndex = 3;
            this.SecurityModeCB.SelectedIndexChanged += new System.EventHandler(this.SecurityModeCB_SelectedIndexChanged);
            // 
            // SecurityPolicyCB
            // 
            this.SecurityPolicyCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SecurityPolicyCB.FormattingEnabled = true;
            this.SecurityPolicyCB.Location = new System.Drawing.Point(123, 60);
            this.SecurityPolicyCB.Name = "SecurityPolicyCB";
            this.SecurityPolicyCB.Size = new System.Drawing.Size(181, 21);
            this.SecurityPolicyCB.TabIndex = 5;
            this.SecurityPolicyCB.SelectedIndexChanged += new System.EventHandler(this.SecurityPolicyCB_SelectedIndexChanged);
            // 
            // ProtocolCB
            // 
            this.ProtocolCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ProtocolCB.FormattingEnabled = true;
            this.ProtocolCB.Location = new System.Drawing.Point(123, 6);
            this.ProtocolCB.Name = "ProtocolCB";
            this.ProtocolCB.Size = new System.Drawing.Size(181, 21);
            this.ProtocolCB.TabIndex = 1;
            this.ProtocolCB.SelectedIndexChanged += new System.EventHandler(this.ProtocolCB_SelectedIndexChanged);
            // 
            // UserTokenPolicyLB
            // 
            this.UserTokenPolicyLB.AutoSize = true;
            this.UserTokenPolicyLB.Location = new System.Drawing.Point(4, 117);
            this.UserTokenPolicyLB.Name = "UserTokenPolicyLB";
            this.UserTokenPolicyLB.Size = new System.Drawing.Size(93, 13);
            this.UserTokenPolicyLB.TabIndex = 8;
            this.UserTokenPolicyLB.Text = "User Identity Type";
            this.UserTokenPolicyLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserTokenTypeCB
            // 
            this.UserTokenTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.UserTokenTypeCB.FormattingEnabled = true;
            this.UserTokenTypeCB.Location = new System.Drawing.Point(123, 114);
            this.UserTokenTypeCB.Name = "UserTokenTypeCB";
            this.UserTokenTypeCB.Size = new System.Drawing.Size(181, 21);
            this.UserTokenTypeCB.TabIndex = 9;
            this.UserTokenTypeCB.SelectedIndexChanged += new System.EventHandler(this.UserTokenPolicyCB_SelectedIndexChanged);
            // 
            // EncodingLB
            // 
            this.EncodingLB.AutoSize = true;
            this.EncodingLB.Location = new System.Drawing.Point(4, 90);
            this.EncodingLB.Name = "EncodingLB";
            this.EncodingLB.Size = new System.Drawing.Size(98, 13);
            this.EncodingLB.TabIndex = 6;
            this.EncodingLB.Text = "Message Encoding";
            this.EncodingLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityModeLB
            // 
            this.SecurityModeLB.AutoSize = true;
            this.SecurityModeLB.Location = new System.Drawing.Point(4, 36);
            this.SecurityModeLB.Name = "SecurityModeLB";
            this.SecurityModeLB.Size = new System.Drawing.Size(75, 13);
            this.SecurityModeLB.TabIndex = 2;
            this.SecurityModeLB.Text = "Security Mode";
            this.SecurityModeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecurityPolicyLB
            // 
            this.SecurityPolicyLB.AutoSize = true;
            this.SecurityPolicyLB.Location = new System.Drawing.Point(4, 63);
            this.SecurityPolicyLB.Name = "SecurityPolicyLB";
            this.SecurityPolicyLB.Size = new System.Drawing.Size(76, 13);
            this.SecurityPolicyLB.TabIndex = 4;
            this.SecurityPolicyLB.Text = "Security Policy";
            this.SecurityPolicyLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserIdentityBTN
            // 
            this.UserIdentityBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.UserIdentityBTN.Location = new System.Drawing.Point(455, 166);
            this.UserIdentityBTN.Name = "UserIdentityBTN";
            this.UserIdentityBTN.Size = new System.Drawing.Size(25, 22);
            this.UserIdentityBTN.TabIndex = 14;
            this.UserIdentityBTN.Text = "...";
            this.UserIdentityBTN.UseVisualStyleBackColor = true;
            this.UserIdentityBTN.Click += new System.EventHandler(this.UserIdentityBTN_Click);
            // 
            // UserIdentityTB
            // 
            this.UserIdentityTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.UserIdentityTB.Location = new System.Drawing.Point(123, 168);
            this.UserIdentityTB.Name = "UserIdentityTB";
            this.UserIdentityTB.Size = new System.Drawing.Size(329, 20);
            this.UserIdentityTB.TabIndex = 13;
            // 
            // ProtocolLB
            // 
            this.ProtocolLB.AutoSize = true;
            this.ProtocolLB.Location = new System.Drawing.Point(4, 9);
            this.ProtocolLB.Name = "ProtocolLB";
            this.ProtocolLB.Size = new System.Drawing.Size(46, 13);
            this.ProtocolLB.TabIndex = 0;
            this.ProtocolLB.Text = "Protocol";
            this.ProtocolLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserIdentityLB
            // 
            this.UserIdentityLB.AutoSize = true;
            this.UserIdentityLB.Location = new System.Drawing.Point(4, 171);
            this.UserIdentityLB.Name = "UserIdentityLB";
            this.UserIdentityLB.Size = new System.Drawing.Size(66, 13);
            this.UserIdentityLB.TabIndex = 12;
            this.UserIdentityLB.Text = "User Identity";
            this.UserIdentityLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ConfiguredServerDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 274);
            this.Controls.Add(this.ButtonsPN);
            this.Controls.Add(this.MainPN);
            this.MaximumSize = new System.Drawing.Size(1024, 1024);
            this.MinimumSize = new System.Drawing.Size(500, 200);
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
        private System.Windows.Forms.Label UserIdentityLB;
        private System.Windows.Forms.Button UserIdentityBTN;
        private System.Windows.Forms.TextBox UserIdentityTB;
        private System.Windows.Forms.Label SecurityPolicyLB;
        private System.Windows.Forms.Label EncodingLB;
        private System.Windows.Forms.Label SecurityModeLB;
        private System.Windows.Forms.Label UserTokenPolicyLB;
        private System.Windows.Forms.ComboBox UserTokenTypeCB;
        private System.Windows.Forms.ComboBox ProtocolCB;
        private System.Windows.Forms.ComboBox SecurityPolicyCB;
        private System.Windows.Forms.ComboBox SecurityModeCB;
        private System.Windows.Forms.ComboBox EncodingCB;
        private System.Windows.Forms.Button RefreshBTN;
        private System.Windows.Forms.Label IssuedTokenTypeLB;
        private System.Windows.Forms.ComboBox IssuedTokenTypeCB;
        private System.Windows.Forms.ComboBox UseDefaultLimitsCB;
        private System.Windows.Forms.Label UseDefaultLimitsLB;
        private System.Windows.Forms.Button UseDefaultLimitsBTN;
        private System.Windows.Forms.TextBox StatusTB;
    }
}
