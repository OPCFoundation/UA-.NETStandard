namespace Opc.Ua.Gds.Client.Controls
{
    partial class DiscoveryUrlsDialog
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
            this.AbandonButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.DiscoveryUrlsTextBox = new System.Windows.Forms.TextBox();
            this.ButtonPanel.SuspendLayout();
            this.MainPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonPanel
            // 
            this.ButtonPanel.Controls.Add(this.AbandonButton);
            this.ButtonPanel.Controls.Add(this.OkButton);
            this.ButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonPanel.Location = new System.Drawing.Point(0, 208);
            this.ButtonPanel.Name = "ButtonPanel";
            this.ButtonPanel.Size = new System.Drawing.Size(449, 30);
            this.ButtonPanel.TabIndex = 0;
            // 
            // AbandonButton
            // 
            this.AbandonButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.AbandonButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.AbandonButton.Location = new System.Drawing.Point(371, 3);
            this.AbandonButton.Name = "AbandonButton";
            this.AbandonButton.Size = new System.Drawing.Size(75, 24);
            this.AbandonButton.TabIndex = 1;
            this.AbandonButton.Text = "Cancel";
            this.AbandonButton.UseVisualStyleBackColor = true;
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
            this.MainPanel.Controls.Add(this.DiscoveryUrlsTextBox);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Padding = new System.Windows.Forms.Padding(3);
            this.MainPanel.Size = new System.Drawing.Size(449, 208);
            this.MainPanel.TabIndex = 1;
            // 
            // DiscoveryUrlsTextBox
            // 
            this.DiscoveryUrlsTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryUrlsTextBox.Location = new System.Drawing.Point(3, 3);
            this.DiscoveryUrlsTextBox.Multiline = true;
            this.DiscoveryUrlsTextBox.Name = "DiscoveryUrlsTextBox";
            this.DiscoveryUrlsTextBox.Size = new System.Drawing.Size(443, 202);
            this.DiscoveryUrlsTextBox.TabIndex = 0;
            // 
            // DiscoveryUrlsDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(449, 238);
            this.Controls.Add(this.MainPanel);
            this.Controls.Add(this.ButtonPanel);
            this.Name = "DiscoveryUrlsDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Discovery URLs";
            this.VisibleChanged += new System.EventHandler(this.DiscoveryUrlsDialog_VisibleChanged);
            this.ButtonPanel.ResumeLayout(false);
            this.MainPanel.ResumeLayout(false);
            this.MainPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonPanel;
        private System.Windows.Forms.Button AbandonButton;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.Panel MainPanel;
        private System.Windows.Forms.TextBox DiscoveryUrlsTextBox;
    }
}