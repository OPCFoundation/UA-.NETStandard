namespace Opc.Ua.Gds
{
    partial class SelectGdsDialog
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
            this.CloseButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.ServersListBox = new System.Windows.Forms.ListBox();
            this.ServersGroupBox = new System.Windows.Forms.GroupBox();
            this.SelectedServerGroupBox = new System.Windows.Forms.GroupBox();
            this.ServerUrlTextBox = new System.Windows.Forms.TextBox();
            this.ButtonPanel.SuspendLayout();
            this.MainPanel.SuspendLayout();
            this.ServersGroupBox.SuspendLayout();
            this.SelectedServerGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonPanel
            // 
            this.ButtonPanel.Controls.Add(this.CloseButton);
            this.ButtonPanel.Controls.Add(this.OkButton);
            this.ButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonPanel.Location = new System.Drawing.Point(0, 200);
            this.ButtonPanel.Name = "ButtonPanel";
            this.ButtonPanel.Size = new System.Drawing.Size(563, 30);
            this.ButtonPanel.TabIndex = 0;
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseButton.Location = new System.Drawing.Point(485, 3);
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
            this.MainPanel.Size = new System.Drawing.Size(563, 200);
            this.MainPanel.TabIndex = 1;
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
            this.ServersListBox.Size = new System.Drawing.Size(551, 134);
            this.ServersListBox.TabIndex = 0;
            this.ServersListBox.SelectedIndexChanged += new System.EventHandler(this.ServersListBox_SelectedIndexChanged);
            // 
            // ServersGroupBox
            // 
            this.ServersGroupBox.Controls.Add(this.ServersListBox);
            this.ServersGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersGroupBox.Location = new System.Drawing.Point(3, 47);
            this.ServersGroupBox.Name = "ServersGroupBox";
            this.ServersGroupBox.Size = new System.Drawing.Size(557, 153);
            this.ServersGroupBox.TabIndex = 3;
            this.ServersGroupBox.TabStop = false;
            this.ServersGroupBox.Text = "Available Servers";
            // 
            // SelectedServerGroupBox
            // 
            this.SelectedServerGroupBox.Controls.Add(this.ServerUrlTextBox);
            this.SelectedServerGroupBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.SelectedServerGroupBox.Location = new System.Drawing.Point(3, 3);
            this.SelectedServerGroupBox.Name = "SelectedServerGroupBox";
            this.SelectedServerGroupBox.Size = new System.Drawing.Size(557, 44);
            this.SelectedServerGroupBox.TabIndex = 4;
            this.SelectedServerGroupBox.TabStop = false;
            this.SelectedServerGroupBox.Text = "Selected Server";
            // 
            // ServerUrlTextBox
            // 
            this.ServerUrlTextBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerUrlTextBox.Location = new System.Drawing.Point(3, 16);
            this.ServerUrlTextBox.Name = "ServerUrlTextBox";
            this.ServerUrlTextBox.Size = new System.Drawing.Size(551, 20);
            this.ServerUrlTextBox.TabIndex = 3;
            this.ServerUrlTextBox.TextChanged += new System.EventHandler(this.ServerUrlTextBox_TextChanged);
            // 
            // SelectGdsDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(563, 230);
            this.Controls.Add(this.MainPanel);
            this.Controls.Add(this.ButtonPanel);
            this.MaximumSize = new System.Drawing.Size(579, 269);
            this.Name = "SelectGdsDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Available Global Discovery Servers";
            this.ButtonPanel.ResumeLayout(false);
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
    }
}