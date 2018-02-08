namespace Opc.Ua.Gds.Client
{
    partial class ApplicationCertificateControl
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
            this.RequestProgressLabel = new System.Windows.Forms.Label();
            this.WarningLabel = new System.Windows.Forms.Label();
            this.CertificateControl = new Opc.Ua.Gds.Client.Controls.EditValueCtrl();
            this.RegistrationButtonsPanel = new System.Windows.Forms.Panel();
            this.ApplyChangesButton = new System.Windows.Forms.Button();
            this.RequestNewButton = new System.Windows.Forms.Button();
            this.CertificateRequestTimer = new System.Windows.Forms.Timer(this.components);
            this.RegistrationPanel.SuspendLayout();
            this.RegistrationButtonsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // RegistrationPanel
            // 
            this.RegistrationPanel.Controls.Add(this.RequestProgressLabel);
            this.RegistrationPanel.Controls.Add(this.WarningLabel);
            this.RegistrationPanel.Controls.Add(this.CertificateControl);
            this.RegistrationPanel.Controls.Add(this.RegistrationButtonsPanel);
            this.RegistrationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RegistrationPanel.Location = new System.Drawing.Point(0, 0);
            this.RegistrationPanel.Name = "RegistrationPanel";
            this.RegistrationPanel.Size = new System.Drawing.Size(879, 693);
            this.RegistrationPanel.TabIndex = 50;
            // 
            // RequestProgressLabel
            // 
            this.RequestProgressLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.RequestProgressLabel.BackColor = System.Drawing.Color.ForestGreen;
            this.RequestProgressLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RequestProgressLabel.ForeColor = System.Drawing.Color.White;
            this.RequestProgressLabel.Location = new System.Drawing.Point(9, 625);
            this.RequestProgressLabel.Name = "RequestProgressLabel";
            this.RequestProgressLabel.Size = new System.Drawing.Size(344, 29);
            this.RequestProgressLabel.TabIndex = 16;
            this.RequestProgressLabel.Text = "A certificate request is in progress.";
            this.RequestProgressLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.RequestProgressLabel.Visible = false;
            // 
            // WarningLabel
            // 
            this.WarningLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.WarningLabel.BackColor = System.Drawing.Color.Red;
            this.WarningLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.WarningLabel.ForeColor = System.Drawing.Color.White;
            this.WarningLabel.Location = new System.Drawing.Point(9, 625);
            this.WarningLabel.Name = "WarningLabel";
            this.WarningLabel.Size = new System.Drawing.Size(452, 29);
            this.WarningLabel.TabIndex = 15;
            this.WarningLabel.Text = "No certificate available. Check registration settings.";
            this.WarningLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.WarningLabel.Visible = false;
            // 
            // CertificateControl
            // 
            this.CertificateControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateControl.Location = new System.Drawing.Point(0, 0);
            this.CertificateControl.Margin = new System.Windows.Forms.Padding(0);
            this.CertificateControl.Name = "CertificateControl";
            this.CertificateControl.Padding = new System.Windows.Forms.Padding(3, 0, 3, 3);
            this.CertificateControl.Size = new System.Drawing.Size(879, 661);
            this.CertificateControl.TabIndex = 14;
            // 
            // RegistrationButtonsPanel
            // 
            this.RegistrationButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButtonsPanel.Controls.Add(this.ApplyChangesButton);
            this.RegistrationButtonsPanel.Controls.Add(this.RequestNewButton);
            this.RegistrationButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RegistrationButtonsPanel.Location = new System.Drawing.Point(0, 661);
            this.RegistrationButtonsPanel.Name = "RegistrationButtonsPanel";
            this.RegistrationButtonsPanel.Size = new System.Drawing.Size(879, 32);
            this.RegistrationButtonsPanel.TabIndex = 13;
            // 
            // ApplyChangesButton
            // 
            this.ApplyChangesButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ApplyChangesButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ApplyChangesButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApplyChangesButton.ForeColor = System.Drawing.Color.White;
            this.ApplyChangesButton.Location = new System.Drawing.Point(129, 0);
            this.ApplyChangesButton.Name = "ApplyChangesButton";
            this.ApplyChangesButton.Size = new System.Drawing.Size(129, 32);
            this.ApplyChangesButton.TabIndex = 4;
            this.ApplyChangesButton.Text = "Apply Changes";
            this.ApplyChangesButton.UseVisualStyleBackColor = false;
            this.ApplyChangesButton.Click += new System.EventHandler(this.ApplyChangesButton_Click);
            this.ApplyChangesButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ApplyChangesButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // RequestNewButton
            // 
            this.RequestNewButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.RequestNewButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.RequestNewButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.RequestNewButton.ForeColor = System.Drawing.Color.White;
            this.RequestNewButton.Location = new System.Drawing.Point(0, 0);
            this.RequestNewButton.Name = "RequestNewButton";
            this.RequestNewButton.Size = new System.Drawing.Size(129, 32);
            this.RequestNewButton.TabIndex = 2;
            this.RequestNewButton.Text = "Request New";
            this.RequestNewButton.UseVisualStyleBackColor = false;
            this.RequestNewButton.Click += new System.EventHandler(this.RequestNewButton_Click);
            this.RequestNewButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.RequestNewButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // CertificateRequestTimer
            // 
            this.CertificateRequestTimer.Interval = 5000;
            this.CertificateRequestTimer.Tick += new System.EventHandler(this.CertificateRequestTimer_Tick);
            // 
            // ApplicationCertificateControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.RegistrationPanel);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "ApplicationCertificateControl";
            this.Size = new System.Drawing.Size(879, 693);
            this.RegistrationPanel.ResumeLayout(false);
            this.RegistrationButtonsPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel RegistrationPanel;
        private System.Windows.Forms.Panel RegistrationButtonsPanel;
        private System.Windows.Forms.Button RequestNewButton;
        private Opc.Ua.Gds.Client.Controls.EditValueCtrl CertificateControl;
        private System.Windows.Forms.Label WarningLabel;
        private System.Windows.Forms.Timer CertificateRequestTimer;
        private System.Windows.Forms.Label RequestProgressLabel;
        private System.Windows.Forms.Button ApplyChangesButton;
    }
}
