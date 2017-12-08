namespace Opc.Ua.Gds.Client.Controls
{
    partial class ViewServersOnNetworkDialog
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
            this.ButtonsPanel = new System.Windows.Forms.Panel();
            this.NextButton = new System.Windows.Forms.Button();
            this.NumberOfRecordsUpDown = new System.Windows.Forms.NumericUpDown();
            this.StopButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            this.SearchButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.ServersDataGridView = new System.Windows.Forms.DataGridView();
            this.ApplicationNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.DiscoveryUrlColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerCapabilitiesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MainPanel = new System.Windows.Forms.TableLayoutPanel();
            this.ServerCapabilitiesTextBox = new System.Windows.Forms.TextBox();
            this.ServerCapabilitiesLabel = new System.Windows.Forms.Label();
            this.ApplicationNameLabel = new System.Windows.Forms.Label();
            this.ProductUriLabel = new System.Windows.Forms.Label();
            this.ApplicationUriLabel = new System.Windows.Forms.Label();
            this.ApplicationUriTextBox = new System.Windows.Forms.TextBox();
            this.ApplicationNameTextBox = new System.Windows.Forms.TextBox();
            this.ProductUriTextBox = new System.Windows.Forms.TextBox();
            this.ServerCapabilitiesButton = new System.Windows.Forms.Button();
            this.ButtonsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NumberOfRecordsUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ServersDataGridView)).BeginInit();
            this.MainPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // ButtonsPanel
            // 
            this.ButtonsPanel.Controls.Add(this.NextButton);
            this.ButtonsPanel.Controls.Add(this.NumberOfRecordsUpDown);
            this.ButtonsPanel.Controls.Add(this.StopButton);
            this.ButtonsPanel.Controls.Add(this.OkButton);
            this.ButtonsPanel.Controls.Add(this.SearchButton);
            this.ButtonsPanel.Controls.Add(this.CloseButton);
            this.ButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPanel.Location = new System.Drawing.Point(0, 373);
            this.ButtonsPanel.Name = "ButtonsPanel";
            this.ButtonsPanel.Size = new System.Drawing.Size(792, 29);
            this.ButtonsPanel.TabIndex = 1;
            // 
            // NextButton
            // 
            this.NextButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.NextButton.Location = new System.Drawing.Point(270, 3);
            this.NextButton.Name = "NextButton";
            this.NextButton.Size = new System.Drawing.Size(80, 23);
            this.NextButton.TabIndex = 8;
            this.NextButton.Text = "Next";
            this.NextButton.UseVisualStyleBackColor = true;
            this.NextButton.Visible = false;
            this.NextButton.Click += new System.EventHandler(this.NextButton_Click);
            // 
            // NumberOfRecordsUpDown
            // 
            this.NumberOfRecordsUpDown.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.NumberOfRecordsUpDown.Location = new System.Drawing.Point(442, 5);
            this.NumberOfRecordsUpDown.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NumberOfRecordsUpDown.Name = "NumberOfRecordsUpDown";
            this.NumberOfRecordsUpDown.Size = new System.Drawing.Size(56, 20);
            this.NumberOfRecordsUpDown.TabIndex = 7;
            this.NumberOfRecordsUpDown.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // StopButton
            // 
            this.StopButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.StopButton.Location = new System.Drawing.Point(356, 3);
            this.StopButton.Name = "StopButton";
            this.StopButton.Size = new System.Drawing.Size(80, 23);
            this.StopButton.TabIndex = 6;
            this.StopButton.Text = "Stop";
            this.StopButton.UseVisualStyleBackColor = true;
            this.StopButton.Visible = false;
            this.StopButton.Click += new System.EventHandler(this.StopButton_Click);
            // 
            // OkButton
            // 
            this.OkButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkButton.Location = new System.Drawing.Point(3, 3);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(80, 23);
            this.OkButton.TabIndex = 5;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            // 
            // SearchButton
            // 
            this.SearchButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.SearchButton.Location = new System.Drawing.Point(356, 3);
            this.SearchButton.Name = "SearchButton";
            this.SearchButton.Size = new System.Drawing.Size(80, 23);
            this.SearchButton.TabIndex = 4;
            this.SearchButton.Text = "Search";
            this.SearchButton.UseVisualStyleBackColor = true;
            this.SearchButton.Click += new System.EventHandler(this.SearchButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseButton.Location = new System.Drawing.Point(709, 3);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(80, 23);
            this.CloseButton.TabIndex = 3;
            this.CloseButton.Text = "Cancel";
            this.CloseButton.UseVisualStyleBackColor = true;
            // 
            // ServersDataGridView
            // 
            this.ServersDataGridView.AllowUserToAddRows = false;
            this.ServersDataGridView.AllowUserToDeleteRows = false;
            this.ServersDataGridView.AllowUserToResizeRows = false;
            this.ServersDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ServersDataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ApplicationNameColumn,
            this.DiscoveryUrlColumn,
            this.ServerCapabilitiesColumn});
            this.ServersDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersDataGridView.EditMode = System.Windows.Forms.DataGridViewEditMode.EditProgrammatically;
            this.ServersDataGridView.Location = new System.Drawing.Point(0, 108);
            this.ServersDataGridView.Name = "ServersDataGridView";
            this.ServersDataGridView.ReadOnly = true;
            this.ServersDataGridView.RowHeadersVisible = false;
            this.ServersDataGridView.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.ServersDataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ServersDataGridView.Size = new System.Drawing.Size(792, 265);
            this.ServersDataGridView.TabIndex = 3;
            this.ServersDataGridView.SelectionChanged += new System.EventHandler(this.ApplicationRecordDataGridView_SelectionChanged);
            // 
            // ApplicationNameColumn
            // 
            this.ApplicationNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ApplicationNameColumn.DataPropertyName = "ServerName";
            this.ApplicationNameColumn.HeaderText = "Server Name";
            this.ApplicationNameColumn.Name = "ApplicationNameColumn";
            this.ApplicationNameColumn.ReadOnly = true;
            this.ApplicationNameColumn.Width = 94;
            // 
            // DiscoveryUrlColumn
            // 
            this.DiscoveryUrlColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.DiscoveryUrlColumn.DataPropertyName = "DiscoveryUrl";
            this.DiscoveryUrlColumn.HeaderText = "URL";
            this.DiscoveryUrlColumn.Name = "DiscoveryUrlColumn";
            this.DiscoveryUrlColumn.ReadOnly = true;
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
            // MainPanel
            // 
            this.MainPanel.ColumnCount = 3;
            this.MainPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.MainPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPanel.Controls.Add(this.ServerCapabilitiesTextBox, 1, 3);
            this.MainPanel.Controls.Add(this.ServerCapabilitiesLabel, 0, 3);
            this.MainPanel.Controls.Add(this.ApplicationNameLabel, 0, 1);
            this.MainPanel.Controls.Add(this.ProductUriLabel, 0, 2);
            this.MainPanel.Controls.Add(this.ApplicationUriLabel, 0, 0);
            this.MainPanel.Controls.Add(this.ApplicationUriTextBox, 1, 0);
            this.MainPanel.Controls.Add(this.ApplicationNameTextBox, 1, 1);
            this.MainPanel.Controls.Add(this.ProductUriTextBox, 1, 2);
            this.MainPanel.Controls.Add(this.ServerCapabilitiesButton, 2, 3);
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            this.MainPanel.RowCount = 5;
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.MainPanel.Size = new System.Drawing.Size(792, 108);
            this.MainPanel.TabIndex = 4;
            // 
            // ServerCapabilitiesTextBox
            // 
            this.ServerCapabilitiesTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerCapabilitiesTextBox.Location = new System.Drawing.Point(103, 81);
            this.ServerCapabilitiesTextBox.Name = "ServerCapabilitiesTextBox";
            this.ServerCapabilitiesTextBox.ReadOnly = true;
            this.ServerCapabilitiesTextBox.Size = new System.Drawing.Size(655, 20);
            this.ServerCapabilitiesTextBox.TabIndex = 14;
            // 
            // ServerCapabilitiesLabel
            // 
            this.ServerCapabilitiesLabel.AutoSize = true;
            this.ServerCapabilitiesLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerCapabilitiesLabel.Location = new System.Drawing.Point(3, 78);
            this.ServerCapabilitiesLabel.Name = "ServerCapabilitiesLabel";
            this.ServerCapabilitiesLabel.Size = new System.Drawing.Size(94, 26);
            this.ServerCapabilitiesLabel.TabIndex = 8;
            this.ServerCapabilitiesLabel.Text = "Server Capabilities";
            this.ServerCapabilitiesLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameLabel
            // 
            this.ApplicationNameLabel.AutoSize = true;
            this.ApplicationNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameLabel.Location = new System.Drawing.Point(3, 26);
            this.ApplicationNameLabel.Name = "ApplicationNameLabel";
            this.ApplicationNameLabel.Size = new System.Drawing.Size(94, 26);
            this.ApplicationNameLabel.TabIndex = 6;
            this.ApplicationNameLabel.Text = "Application Name";
            this.ApplicationNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriLabel
            // 
            this.ProductUriLabel.AutoSize = true;
            this.ProductUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriLabel.Location = new System.Drawing.Point(3, 52);
            this.ProductUriLabel.Name = "ProductUriLabel";
            this.ProductUriLabel.Size = new System.Drawing.Size(94, 26);
            this.ProductUriLabel.TabIndex = 5;
            this.ProductUriLabel.Text = "Product URI";
            this.ProductUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriLabel
            // 
            this.ApplicationUriLabel.AutoSize = true;
            this.ApplicationUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriLabel.Location = new System.Drawing.Point(3, 0);
            this.ApplicationUriLabel.Name = "ApplicationUriLabel";
            this.ApplicationUriLabel.Size = new System.Drawing.Size(94, 26);
            this.ApplicationUriLabel.TabIndex = 0;
            this.ApplicationUriLabel.Text = "Application URI";
            this.ApplicationUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriTextBox
            // 
            this.ApplicationUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriTextBox.Location = new System.Drawing.Point(103, 3);
            this.ApplicationUriTextBox.Name = "ApplicationUriTextBox";
            this.ApplicationUriTextBox.Size = new System.Drawing.Size(655, 20);
            this.ApplicationUriTextBox.TabIndex = 9;
            this.ApplicationUriTextBox.TextChanged += new System.EventHandler(this.ApplicationUriTextBox_TextChanged);
            // 
            // ApplicationNameTextBox
            // 
            this.ApplicationNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameTextBox.Location = new System.Drawing.Point(103, 29);
            this.ApplicationNameTextBox.Name = "ApplicationNameTextBox";
            this.ApplicationNameTextBox.Size = new System.Drawing.Size(655, 20);
            this.ApplicationNameTextBox.TabIndex = 11;
            this.ApplicationNameTextBox.TextChanged += new System.EventHandler(this.ApplicationNameTextBox_TextChanged);
            // 
            // ProductUriTextBox
            // 
            this.ProductUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriTextBox.Location = new System.Drawing.Point(103, 55);
            this.ProductUriTextBox.Name = "ProductUriTextBox";
            this.ProductUriTextBox.Size = new System.Drawing.Size(655, 20);
            this.ProductUriTextBox.TabIndex = 12;
            this.ProductUriTextBox.TextChanged += new System.EventHandler(this.ProductUriTextBox_TextChanged);
            // 
            // ServerCapabilitiesButton
            // 
            this.ServerCapabilitiesButton.Location = new System.Drawing.Point(764, 81);
            this.ServerCapabilitiesButton.Name = "ServerCapabilitiesButton";
            this.ServerCapabilitiesButton.Size = new System.Drawing.Size(25, 20);
            this.ServerCapabilitiesButton.TabIndex = 15;
            this.ServerCapabilitiesButton.Text = "...";
            this.ServerCapabilitiesButton.UseVisualStyleBackColor = true;
            this.ServerCapabilitiesButton.Click += new System.EventHandler(this.ServerCapabilitiesButton_Click);
            // 
            // ViewServersOnNetworkDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(792, 402);
            this.Controls.Add(this.ServersDataGridView);
            this.Controls.Add(this.MainPanel);
            this.Controls.Add(this.ButtonsPanel);
            this.MinimumSize = new System.Drawing.Size(600, 240);
            this.Name = "ViewServersOnNetworkDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Query Servers";
            this.ButtonsPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.NumberOfRecordsUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ServersDataGridView)).EndInit();
            this.MainPanel.ResumeLayout(false);
            this.MainPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPanel;
        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button SearchButton;
        private System.Windows.Forms.DataGridView ServersDataGridView;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.DataGridViewTextBoxColumn ApplicationNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn DiscoveryUrlColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerCapabilitiesColumn;
        private System.Windows.Forms.TableLayoutPanel MainPanel;
        private System.Windows.Forms.TextBox ServerCapabilitiesTextBox;
        private System.Windows.Forms.Label ServerCapabilitiesLabel;
        private System.Windows.Forms.Label ApplicationNameLabel;
        private System.Windows.Forms.Label ProductUriLabel;
        private System.Windows.Forms.Label ApplicationUriLabel;
        private System.Windows.Forms.TextBox ApplicationUriTextBox;
        private System.Windows.Forms.TextBox ApplicationNameTextBox;
        private System.Windows.Forms.TextBox ProductUriTextBox;
        private System.Windows.Forms.Button ServerCapabilitiesButton;
        private System.Windows.Forms.Button StopButton;
        private System.Windows.Forms.NumericUpDown NumberOfRecordsUpDown;
        private System.Windows.Forms.Button NextButton;
    }
}