namespace Opc.Ua.Gds.Client.Controls
{
    partial class ViewApplicationRecordsDialog
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
            this.OkButton = new System.Windows.Forms.Button();
            this.UnregisterButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.ApplicationRecordDataGridView = new System.Windows.Forms.DataGridView();
            this.ApplicationNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ApplicationTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ProductUriColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DiscoveryUrlsColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerCapabilitiesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.InstructionsLabel = new System.Windows.Forms.Label();
            this.MainPanel = new System.Windows.Forms.Panel();
            this.ButtonPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ApplicationRecordDataGridView)).BeginInit();
            this.MainPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonPanel
            // 
            this.ButtonPanel.Controls.Add(this.OkButton);
            this.ButtonPanel.Controls.Add(this.UnregisterButton);
            this.ButtonPanel.Controls.Add(this.CloseButton);
            this.ButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonPanel.Location = new System.Drawing.Point(0, 172);
            this.ButtonPanel.Name = "ButtonPanel";
            this.ButtonPanel.Size = new System.Drawing.Size(584, 30);
            this.ButtonPanel.TabIndex = 1;
            // 
            // OkButton
            // 
            this.OkButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkButton.Location = new System.Drawing.Point(3, 3);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(75, 24);
            this.OkButton.TabIndex = 5;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            // 
            // UnregisterButton
            // 
            this.UnregisterButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.UnregisterButton.Enabled = false;
            this.UnregisterButton.Location = new System.Drawing.Point(255, 3);
            this.UnregisterButton.Name = "UnregisterButton";
            this.UnregisterButton.Size = new System.Drawing.Size(75, 24);
            this.UnregisterButton.TabIndex = 4;
            this.UnregisterButton.Text = "Unregister";
            this.UnregisterButton.UseVisualStyleBackColor = true;
            this.UnregisterButton.Click += new System.EventHandler(this.UnregisterButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseButton.Location = new System.Drawing.Point(506, 3);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 24);
            this.CloseButton.TabIndex = 3;
            this.CloseButton.Text = "Cancel";
            this.CloseButton.UseVisualStyleBackColor = true;
            // 
            // ApplicationRecordDataGridView
            // 
            this.ApplicationRecordDataGridView.AllowUserToAddRows = false;
            this.ApplicationRecordDataGridView.AllowUserToDeleteRows = false;
            this.ApplicationRecordDataGridView.AllowUserToResizeRows = false;
            this.ApplicationRecordDataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ApplicationRecordDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ApplicationRecordDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ApplicationNameColumn,
            this.ApplicationTypeColumn,
            this.ProductUriColumn,
            this.DiscoveryUrlsColumn,
            this.ServerCapabilitiesColumn});
            this.ApplicationRecordDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationRecordDataGridView.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ApplicationRecordDataGridView.Location = new System.Drawing.Point(3, 3);
            this.ApplicationRecordDataGridView.Name = "ApplicationRecordDataGridView";
            this.ApplicationRecordDataGridView.ReadOnly = true;
            this.ApplicationRecordDataGridView.RowHeadersVisible = false;
            this.ApplicationRecordDataGridView.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.ApplicationRecordDataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ApplicationRecordDataGridView.Size = new System.Drawing.Size(578, 116);
            this.ApplicationRecordDataGridView.TabIndex = 3;
            this.ApplicationRecordDataGridView.SelectionChanged += new System.EventHandler(this.ApplicationRecordDataGridView_SelectionChanged);
            // 
            // ApplicationNameColumn
            // 
            this.ApplicationNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ApplicationNameColumn.DataPropertyName = "ApplicationName";
            this.ApplicationNameColumn.HeaderText = "Name";
            this.ApplicationNameColumn.Name = "ApplicationNameColumn";
            this.ApplicationNameColumn.ReadOnly = true;
            this.ApplicationNameColumn.Width = 60;
            // 
            // ApplicationTypeColumn
            // 
            this.ApplicationTypeColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ApplicationTypeColumn.DataPropertyName = "ApplicationType";
            this.ApplicationTypeColumn.HeaderText = "Type";
            this.ApplicationTypeColumn.Name = "ApplicationTypeColumn";
            this.ApplicationTypeColumn.ReadOnly = true;
            this.ApplicationTypeColumn.Visible = false;
            // 
            // ProductUriColumn
            // 
            this.ProductUriColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ProductUriColumn.DataPropertyName = "ProductUri";
            this.ProductUriColumn.HeaderText = "ProductUri";
            this.ProductUriColumn.Name = "ProductUriColumn";
            this.ProductUriColumn.ReadOnly = true;
            // 
            // DiscoveryUrlsColumn
            // 
            this.DiscoveryUrlsColumn.DataPropertyName = "DiscoveryUrls";
            this.DiscoveryUrlsColumn.HeaderText = "DiscoveryUrls";
            this.DiscoveryUrlsColumn.Name = "DiscoveryUrlsColumn";
            this.DiscoveryUrlsColumn.ReadOnly = true;
            this.DiscoveryUrlsColumn.Width = 150;
            // 
            // ServerCapabilitiesColumn
            // 
            this.ServerCapabilitiesColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ServerCapabilitiesColumn.DataPropertyName = "ServerCapabilities";
            this.ServerCapabilitiesColumn.HeaderText = "Capabilities";
            this.ServerCapabilitiesColumn.Name = "ServerCapabilitiesColumn";
            this.ServerCapabilitiesColumn.ReadOnly = true;
            this.ServerCapabilitiesColumn.Width = 85;
            // 
            // InstructionsLabel
            // 
            this.InstructionsLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.InstructionsLabel.Location = new System.Drawing.Point(0, 0);
            this.InstructionsLabel.Name = "InstructionsLabel";
            this.InstructionsLabel.Padding = new System.Windows.Forms.Padding(5, 5, 0, 5);
            this.InstructionsLabel.Size = new System.Drawing.Size(584, 50);
            this.InstructionsLabel.TabIndex = 4;
            this.InstructionsLabel.Text = "Applications with the same Application URI already exist. \r\nPlease select the rec" +
    "ord to replace or delete any unneeded records.\r\nDuplicate Application URIs are a" +
    " database error and should be resolved.";
            // 
            // MainPanel
            // 
            this.MainPanel.Controls.Add(this.ApplicationRecordDataGridView);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 50);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.Padding = new System.Windows.Forms.Padding(3);
            this.MainPanel.Size = new System.Drawing.Size(584, 122);
            this.MainPanel.TabIndex = 5;
            // 
            // ViewApplicationRecordsDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(584, 202);
            this.Controls.Add(this.MainPanel);
            this.Controls.Add(this.InstructionsLabel);
            this.Controls.Add(this.ButtonPanel);
            this.MinimumSize = new System.Drawing.Size(600, 240);
            this.Name = "ViewApplicationRecordsDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Application Records";
            this.ButtonPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ApplicationRecordDataGridView)).EndInit();
            this.MainPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonPanel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button UnregisterButton;
        private System.Windows.Forms.DataGridView ApplicationRecordDataGridView;
        private System.Windows.Forms.Label InstructionsLabel;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.DataGridViewTextBoxColumn ApplicationNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ApplicationTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ProductUriColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn DiscoveryUrlsColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerCapabilitiesColumn;
        private System.Windows.Forms.Panel MainPanel;
    }
}