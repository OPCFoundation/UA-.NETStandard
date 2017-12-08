namespace Opc.Ua.Gds.Client
{
    partial class ApplicationTrustListControl
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
            this.CertificateStoreControl = new Opc.Ua.Gds.Client.Controls.CertificateStoreControl();
            this.RegistrationButtonsPanel = new System.Windows.Forms.Panel();
            this.PushToServerButton = new System.Windows.Forms.Button();
            this.MergeWithGdsButton = new System.Windows.Forms.Button();
            this.PullFromGdsButton = new System.Windows.Forms.Button();
            this.ReadTrustListButton = new System.Windows.Forms.Button();
            this.ToolTips = new System.Windows.Forms.ToolTip(this.components);
            this.ApplyChangesButton = new System.Windows.Forms.Button();
            this.RegistrationPanel.SuspendLayout();
            this.RegistrationButtonsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // RegistrationPanel
            // 
            this.RegistrationPanel.Controls.Add(this.CertificateStoreControl);
            this.RegistrationPanel.Controls.Add(this.RegistrationButtonsPanel);
            this.RegistrationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationPanel.Location = new System.Drawing.Point(0, 0);
            this.RegistrationPanel.Name = "RegistrationPanel";
            this.RegistrationPanel.Size = new System.Drawing.Size(879, 693);
            this.RegistrationPanel.TabIndex = 50;
            // 
            // CertificateStoreControl
            // 
            this.CertificateStoreControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateStoreControl.Location = new System.Drawing.Point(0, 0);
            this.CertificateStoreControl.Name = "CertificateStoreControl";
            this.CertificateStoreControl.Padding = new System.Windows.Forms.Padding(3);
            this.CertificateStoreControl.Size = new System.Drawing.Size(879, 661);
            this.CertificateStoreControl.TabIndex = 14;
            // 
            // RegistrationButtonsPanel
            // 
            this.RegistrationButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButtonsPanel.Controls.Add(this.ApplyChangesButton);
            this.RegistrationButtonsPanel.Controls.Add(this.PushToServerButton);
            this.RegistrationButtonsPanel.Controls.Add(this.MergeWithGdsButton);
            this.RegistrationButtonsPanel.Controls.Add(this.PullFromGdsButton);
            this.RegistrationButtonsPanel.Controls.Add(this.ReadTrustListButton);
            this.RegistrationButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RegistrationButtonsPanel.Location = new System.Drawing.Point(0, 661);
            this.RegistrationButtonsPanel.Name = "RegistrationButtonsPanel";
            this.RegistrationButtonsPanel.Size = new System.Drawing.Size(879, 32);
            this.RegistrationButtonsPanel.TabIndex = 13;
            // 
            // PushToServerButton
            // 
            this.PushToServerButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.PushToServerButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.PushToServerButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PushToServerButton.ForeColor = System.Drawing.Color.White;
            this.PushToServerButton.Location = new System.Drawing.Point(387, 0);
            this.PushToServerButton.Name = "PushToServerButton";
            this.PushToServerButton.Size = new System.Drawing.Size(129, 32);
            this.PushToServerButton.TabIndex = 3;
            this.PushToServerButton.Text = "Push To Server";
            this.ToolTips.SetToolTip(this.PushToServerButton, "Updates the Trust List on the remote Server.");
            this.PushToServerButton.UseVisualStyleBackColor = false;
            this.PushToServerButton.Click += new System.EventHandler(this.PushToServerButton_Click);
            this.PushToServerButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.PushToServerButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // MergeWithGdsButton
            // 
            this.MergeWithGdsButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.MergeWithGdsButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.MergeWithGdsButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MergeWithGdsButton.ForeColor = System.Drawing.Color.White;
            this.MergeWithGdsButton.Location = new System.Drawing.Point(258, 0);
            this.MergeWithGdsButton.Name = "MergeWithGdsButton";
            this.MergeWithGdsButton.Size = new System.Drawing.Size(129, 32);
            this.MergeWithGdsButton.TabIndex = 4;
            this.MergeWithGdsButton.Text = "Merge with GDS";
            this.ToolTips.SetToolTip(this.MergeWithGdsButton, "Adds the Certificsates and CRLs provided by the GDS to the Trust List.");
            this.MergeWithGdsButton.UseVisualStyleBackColor = false;
            this.MergeWithGdsButton.Click += new System.EventHandler(this.MergeWithGdsButton_Click);
            this.MergeWithGdsButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.MergeWithGdsButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // PullFromGdsButton
            // 
            this.PullFromGdsButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.PullFromGdsButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.PullFromGdsButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.PullFromGdsButton.ForeColor = System.Drawing.Color.White;
            this.PullFromGdsButton.Location = new System.Drawing.Point(129, 0);
            this.PullFromGdsButton.Name = "PullFromGdsButton";
            this.PullFromGdsButton.Size = new System.Drawing.Size(129, 32);
            this.PullFromGdsButton.TabIndex = 0;
            this.PullFromGdsButton.Text = "Replace with GDS";
            this.ToolTips.SetToolTip(this.PullFromGdsButton, "Replaces all Certificates and CRLs in the Trust Lsts with the contents of the Tru" +
        "st List provided by the GDS.");
            this.PullFromGdsButton.UseVisualStyleBackColor = false;
            this.PullFromGdsButton.Click += new System.EventHandler(this.PullFromGdsButton_Click);
            this.PullFromGdsButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.PullFromGdsButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // ReadTrustListButton
            // 
            this.ReadTrustListButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ReadTrustListButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ReadTrustListButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ReadTrustListButton.ForeColor = System.Drawing.Color.White;
            this.ReadTrustListButton.Location = new System.Drawing.Point(0, 0);
            this.ReadTrustListButton.Name = "ReadTrustListButton";
            this.ReadTrustListButton.Size = new System.Drawing.Size(129, 32);
            this.ReadTrustListButton.TabIndex = 2;
            this.ReadTrustListButton.Text = "Reload";
            this.ToolTips.SetToolTip(this.ReadTrustListButton, "Reloads the Trust List from disk or by reading it from the remote Server.");
            this.ReadTrustListButton.UseVisualStyleBackColor = false;
            this.ReadTrustListButton.Click += new System.EventHandler(this.ReloadTrustListButton_Click);
            this.ReadTrustListButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ReadTrustListButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // ApplyChangesButton
            // 
            this.ApplyChangesButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ApplyChangesButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ApplyChangesButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApplyChangesButton.ForeColor = System.Drawing.Color.White;
            this.ApplyChangesButton.Location = new System.Drawing.Point(516, 0);
            this.ApplyChangesButton.Name = "ApplyChangesButton";
            this.ApplyChangesButton.Size = new System.Drawing.Size(129, 32);
            this.ApplyChangesButton.TabIndex = 5;
            this.ApplyChangesButton.Text = "Apply Changes";
            this.ApplyChangesButton.UseVisualStyleBackColor = false;
            this.ApplyChangesButton.Click += new System.EventHandler(this.ApplyChangesButton_Click);
            // 
            // ApplicationTrustListControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RegistrationPanel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "ApplicationTrustListControl";
            this.Size = new System.Drawing.Size(879, 693);
            this.RegistrationPanel.ResumeLayout(false);
            this.RegistrationButtonsPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel RegistrationPanel;
        private System.Windows.Forms.Panel RegistrationButtonsPanel;
        private System.Windows.Forms.Button PullFromGdsButton;
        private System.Windows.Forms.Button ReadTrustListButton;
        private Opc.Ua.Gds.Client.Controls.CertificateStoreControl CertificateStoreControl;
        private System.Windows.Forms.Button PushToServerButton;
        private System.Windows.Forms.Button MergeWithGdsButton;
        private System.Windows.Forms.ToolTip ToolTips;
        private System.Windows.Forms.Button ApplyChangesButton;
    }
}
