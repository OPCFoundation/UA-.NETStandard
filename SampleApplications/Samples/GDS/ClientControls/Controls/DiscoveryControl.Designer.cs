namespace Opc.Ua.Gds.Client.Controls
{
    partial class DiscoveryControl
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
            this.DiscoveryTreeView = new System.Windows.Forms.TreeView();
            this.PopupMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.RefreshMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.RefreshWithParametersMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.AddEndpointSeparatorMenuItem = new System.Windows.Forms.ToolStripSeparator();
            this.AddEndpointMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.MainPanel = new System.Windows.Forms.SplitContainer();
            this.ServersGridView = new System.Windows.Forms.DataGridView();
            this.ServerNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ServerCapabilitiesColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.EndpointUrlColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ApplicationDescriptionPanel = new System.Windows.Forms.TableLayoutPanel();
            this.ApplicationNameLabel = new System.Windows.Forms.Label();
            this.ProductUriTextBox = new System.Windows.Forms.Label();
            this.ProductUriLabel = new System.Windows.Forms.Label();
            this.ApplicationTypeTextBox = new System.Windows.Forms.Label();
            this.ApplicationTypeLabel = new System.Windows.Forms.Label();
            this.ApplicationUriTextBox = new System.Windows.Forms.Label();
            this.ApplicationUriLabel = new System.Windows.Forms.Label();
            this.ApplicationNameTextBox = new System.Windows.Forms.Label();
            this.FilterPanel = new System.Windows.Forms.Panel();
            this.FilterTextBox = new System.Windows.Forms.TextBox();
            this.FilterLabel = new System.Windows.Forms.Label();
            this.EndpointsGridView = new System.Windows.Forms.DataGridView();
            this.EndpointUrlColumn3 = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SecurityModeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SecurityProfileColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PopupMenuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MainPanel)).BeginInit();
            this.MainPanel.Panel1.SuspendLayout();
            this.MainPanel.Panel2.SuspendLayout();
            this.MainPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ServersGridView)).BeginInit();
            this.ApplicationDescriptionPanel.SuspendLayout();
            this.FilterPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.EndpointsGridView)).BeginInit();
            this.SuspendLayout();
            // 
            // DiscoveryTreeView
            // 
            this.DiscoveryTreeView.ContextMenuStrip = this.PopupMenuStrip;
            this.DiscoveryTreeView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryTreeView.Location = new System.Drawing.Point(0, 0);
            this.DiscoveryTreeView.Name = "DiscoveryTreeView";
            this.DiscoveryTreeView.Size = new System.Drawing.Size(296, 584);
            this.DiscoveryTreeView.TabIndex = 0;
            this.DiscoveryTreeView.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.DiscoveryTreeView_BeforeExpand);
            this.DiscoveryTreeView.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.DiscoveryTreeView_AfterSelect);
            this.DiscoveryTreeView.DoubleClick += new System.EventHandler(this.DiscoveryTreeView_DoubleClick);
            this.DiscoveryTreeView.MouseDown += new System.Windows.Forms.MouseEventHandler(this.DiscoveryTreeView_MouseDown);
            // 
            // PopupMenuStrip
            // 
            this.PopupMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.RefreshMenuItem,
            this.RefreshWithParametersMenuItem,
            this.DeleteMenuItem,
            this.AddEndpointSeparatorMenuItem,
            this.AddEndpointMenuItem});
            this.PopupMenuStrip.Name = "PopupMenuStrip";
            this.PopupMenuStrip.Size = new System.Drawing.Size(211, 98);
            this.PopupMenuStrip.Opening += new System.ComponentModel.CancelEventHandler(this.PopupMenuStrip_Opening);
            // 
            // RefreshMenuItem
            // 
            this.RefreshMenuItem.Name = "RefreshMenuItem";
            this.RefreshMenuItem.Size = new System.Drawing.Size(210, 22);
            this.RefreshMenuItem.Text = "Refresh";
            this.RefreshMenuItem.Click += new System.EventHandler(this.RefreshMenuItem_Click);
            // 
            // RefreshWithParametersMenuItem
            // 
            this.RefreshWithParametersMenuItem.Name = "RefreshWithParametersMenuItem";
            this.RefreshWithParametersMenuItem.Size = new System.Drawing.Size(210, 22);
            this.RefreshWithParametersMenuItem.Text = "Refresh with Parameters...";
            this.RefreshWithParametersMenuItem.Click += new System.EventHandler(this.RefreshWithParametersMenuItem_Click);
            // 
            // DeleteMenuItem
            // 
            this.DeleteMenuItem.Name = "DeleteMenuItem";
            this.DeleteMenuItem.Size = new System.Drawing.Size(210, 22);
            this.DeleteMenuItem.Text = "Delete...";
            this.DeleteMenuItem.Click += new System.EventHandler(this.DeleteMenuItem_Click);
            // 
            // AddEndpointSeparatorMenuItem
            // 
            this.AddEndpointSeparatorMenuItem.Name = "AddEndpointSeparatorMenuItem";
            this.AddEndpointSeparatorMenuItem.Size = new System.Drawing.Size(207, 6);
            // 
            // AddEndpointMenuItem
            // 
            this.AddEndpointMenuItem.Name = "AddEndpointMenuItem";
            this.AddEndpointMenuItem.Size = new System.Drawing.Size(210, 22);
            this.AddEndpointMenuItem.Text = "Add Endpoint...";
            this.AddEndpointMenuItem.Click += new System.EventHandler(this.DiscoveryTreeView_DoubleClick);
            // 
            // MainPanel
            // 
            this.MainPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPanel.Location = new System.Drawing.Point(0, 0);
            this.MainPanel.Name = "MainPanel";
            // 
            // MainPanel.Panel1
            // 
            this.MainPanel.Panel1.Controls.Add(this.DiscoveryTreeView);
            // 
            // MainPanel.Panel2
            // 
            this.MainPanel.Panel2.Controls.Add(this.EndpointsGridView);
            this.MainPanel.Panel2.Controls.Add(this.ServersGridView);
            this.MainPanel.Panel2.Controls.Add(this.ApplicationDescriptionPanel);
            this.MainPanel.Panel2.Controls.Add(this.FilterPanel);
            this.MainPanel.Size = new System.Drawing.Size(888, 584);
            this.MainPanel.SplitterDistance = 296;
            this.MainPanel.TabIndex = 3;
            // 
            // ServersGridView
            // 
            this.ServersGridView.AllowUserToAddRows = false;
            this.ServersGridView.AllowUserToDeleteRows = false;
            this.ServersGridView.AllowUserToResizeRows = false;
            this.ServersGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.ServersGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.ServersGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ServerNameColumn,
            this.ServerCapabilitiesColumn,
            this.EndpointUrlColumn});
            this.ServersGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServersGridView.Location = new System.Drawing.Point(0, 138);
            this.ServersGridView.Name = "ServersGridView";
            this.ServersGridView.ReadOnly = true;
            this.ServersGridView.RowHeadersVisible = false;
            this.ServersGridView.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.ServersGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.ServersGridView.Size = new System.Drawing.Size(588, 446);
            this.ServersGridView.TabIndex = 3;
            this.ServersGridView.DoubleClick += new System.EventHandler(this.ServersGridView_DoubleClick);
            // 
            // ServerNameColumn
            // 
            this.ServerNameColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ServerNameColumn.DataPropertyName = "ServerName";
            this.ServerNameColumn.HeaderText = "Server Name";
            this.ServerNameColumn.Name = "ServerNameColumn";
            this.ServerNameColumn.ReadOnly = true;
            this.ServerNameColumn.Width = 94;
            // 
            // ServerCapabilitiesColumn
            // 
            this.ServerCapabilitiesColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ServerCapabilitiesColumn.DataPropertyName = "ServerCapabilities";
            this.ServerCapabilitiesColumn.HeaderText = "Server Capabilities";
            this.ServerCapabilitiesColumn.Name = "ServerCapabilitiesColumn";
            this.ServerCapabilitiesColumn.ReadOnly = true;
            this.ServerCapabilitiesColumn.Width = 119;
            // 
            // EndpointUrlColumn
            // 
            this.EndpointUrlColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.EndpointUrlColumn.DataPropertyName = "EndpointUrl";
            this.EndpointUrlColumn.HeaderText = "Endpoint URL";
            this.EndpointUrlColumn.Name = "EndpointUrlColumn";
            this.EndpointUrlColumn.ReadOnly = true;
            // 
            // ApplicationDescriptionPanel
            // 
            this.ApplicationDescriptionPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ApplicationDescriptionPanel.CellBorderStyle = System.Windows.Forms.TableLayoutPanelCellBorderStyle.Inset;
            this.ApplicationDescriptionPanel.ColumnCount = 2;
            this.ApplicationDescriptionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ApplicationDescriptionPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationNameLabel, 0, 0);
            this.ApplicationDescriptionPanel.Controls.Add(this.ProductUriTextBox, 1, 3);
            this.ApplicationDescriptionPanel.Controls.Add(this.ProductUriLabel, 0, 3);
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationTypeTextBox, 1, 2);
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationTypeLabel, 0, 2);
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationUriTextBox, 1, 1);
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationUriLabel, 0, 1);
            this.ApplicationDescriptionPanel.Controls.Add(this.ApplicationNameTextBox, 1, 0);
            this.ApplicationDescriptionPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ApplicationDescriptionPanel.Location = new System.Drawing.Point(0, 32);
            this.ApplicationDescriptionPanel.Name = "ApplicationDescriptionPanel";
            this.ApplicationDescriptionPanel.RowCount = 4;
            this.ApplicationDescriptionPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.ApplicationDescriptionPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.ApplicationDescriptionPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.ApplicationDescriptionPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.ApplicationDescriptionPanel.Size = new System.Drawing.Size(588, 106);
            this.ApplicationDescriptionPanel.TabIndex = 1;
            // 
            // ApplicationNameLabel
            // 
            this.ApplicationNameLabel.AllowDrop = true;
            this.ApplicationNameLabel.AutoSize = true;
            this.ApplicationNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameLabel.Location = new System.Drawing.Point(5, 5);
            this.ApplicationNameLabel.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationNameLabel.Name = "ApplicationNameLabel";
            this.ApplicationNameLabel.Size = new System.Drawing.Size(90, 18);
            this.ApplicationNameLabel.TabIndex = 0;
            this.ApplicationNameLabel.Text = "Application Name";
            this.ApplicationNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriTextBox
            // 
            this.ProductUriTextBox.AllowDrop = true;
            this.ProductUriTextBox.AutoSize = true;
            this.ProductUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriTextBox.Location = new System.Drawing.Point(103, 83);
            this.ProductUriTextBox.Margin = new System.Windows.Forms.Padding(3);
            this.ProductUriTextBox.Name = "ProductUriTextBox";
            this.ProductUriTextBox.Size = new System.Drawing.Size(480, 18);
            this.ProductUriTextBox.TabIndex = 7;
            this.ProductUriTextBox.Text = "---";
            this.ProductUriTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriLabel
            // 
            this.ProductUriLabel.AllowDrop = true;
            this.ProductUriLabel.AutoSize = true;
            this.ProductUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriLabel.Location = new System.Drawing.Point(5, 83);
            this.ProductUriLabel.Margin = new System.Windows.Forms.Padding(3);
            this.ProductUriLabel.Name = "ProductUriLabel";
            this.ProductUriLabel.Size = new System.Drawing.Size(90, 18);
            this.ProductUriLabel.TabIndex = 6;
            this.ProductUriLabel.Text = "Product URI";
            this.ProductUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationTypeTextBox
            // 
            this.ApplicationTypeTextBox.AllowDrop = true;
            this.ApplicationTypeTextBox.AutoSize = true;
            this.ApplicationTypeTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationTypeTextBox.Location = new System.Drawing.Point(103, 57);
            this.ApplicationTypeTextBox.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationTypeTextBox.Name = "ApplicationTypeTextBox";
            this.ApplicationTypeTextBox.Size = new System.Drawing.Size(480, 18);
            this.ApplicationTypeTextBox.TabIndex = 5;
            this.ApplicationTypeTextBox.Text = "---";
            this.ApplicationTypeTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationTypeLabel
            // 
            this.ApplicationTypeLabel.AllowDrop = true;
            this.ApplicationTypeLabel.AutoSize = true;
            this.ApplicationTypeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationTypeLabel.Location = new System.Drawing.Point(5, 57);
            this.ApplicationTypeLabel.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationTypeLabel.Name = "ApplicationTypeLabel";
            this.ApplicationTypeLabel.Size = new System.Drawing.Size(90, 18);
            this.ApplicationTypeLabel.TabIndex = 4;
            this.ApplicationTypeLabel.Text = "Application Type";
            this.ApplicationTypeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriTextBox
            // 
            this.ApplicationUriTextBox.AllowDrop = true;
            this.ApplicationUriTextBox.AutoSize = true;
            this.ApplicationUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriTextBox.Location = new System.Drawing.Point(103, 31);
            this.ApplicationUriTextBox.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationUriTextBox.Name = "ApplicationUriTextBox";
            this.ApplicationUriTextBox.Size = new System.Drawing.Size(480, 18);
            this.ApplicationUriTextBox.TabIndex = 3;
            this.ApplicationUriTextBox.Text = "---";
            this.ApplicationUriTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationUriLabel
            // 
            this.ApplicationUriLabel.AllowDrop = true;
            this.ApplicationUriLabel.AutoSize = true;
            this.ApplicationUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationUriLabel.Location = new System.Drawing.Point(5, 31);
            this.ApplicationUriLabel.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationUriLabel.Name = "ApplicationUriLabel";
            this.ApplicationUriLabel.Size = new System.Drawing.Size(90, 18);
            this.ApplicationUriLabel.TabIndex = 2;
            this.ApplicationUriLabel.Text = "Application URI";
            this.ApplicationUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ApplicationNameTextBox
            // 
            this.ApplicationNameTextBox.AllowDrop = true;
            this.ApplicationNameTextBox.AutoSize = true;
            this.ApplicationNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ApplicationNameTextBox.Location = new System.Drawing.Point(103, 5);
            this.ApplicationNameTextBox.Margin = new System.Windows.Forms.Padding(3);
            this.ApplicationNameTextBox.Name = "ApplicationNameTextBox";
            this.ApplicationNameTextBox.Size = new System.Drawing.Size(480, 18);
            this.ApplicationNameTextBox.TabIndex = 1;
            this.ApplicationNameTextBox.Text = "---";
            this.ApplicationNameTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // FilterPanel
            // 
            this.FilterPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.FilterPanel.Controls.Add(this.FilterTextBox);
            this.FilterPanel.Controls.Add(this.FilterLabel);
            this.FilterPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.FilterPanel.Location = new System.Drawing.Point(0, 0);
            this.FilterPanel.Margin = new System.Windows.Forms.Padding(0);
            this.FilterPanel.Name = "FilterPanel";
            this.FilterPanel.Padding = new System.Windows.Forms.Padding(2, 6, 6, 6);
            this.FilterPanel.Size = new System.Drawing.Size(588, 32);
            this.FilterPanel.TabIndex = 0;
            // 
            // FilterTextBox
            // 
            this.FilterTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.FilterTextBox.Location = new System.Drawing.Point(77, 6);
            this.FilterTextBox.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
            this.FilterTextBox.Name = "FilterTextBox";
            this.FilterTextBox.Size = new System.Drawing.Size(505, 20);
            this.FilterTextBox.TabIndex = 1;
            this.FilterTextBox.TextChanged += new System.EventHandler(this.FilterTextBox_TextChanged);
            // 
            // FilterLabel
            // 
            this.FilterLabel.AllowDrop = true;
            this.FilterLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.FilterLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FilterLabel.ForeColor = System.Drawing.Color.White;
            this.FilterLabel.Location = new System.Drawing.Point(2, 6);
            this.FilterLabel.Margin = new System.Windows.Forms.Padding(3);
            this.FilterLabel.Name = "FilterLabel";
            this.FilterLabel.Size = new System.Drawing.Size(75, 20);
            this.FilterLabel.TabIndex = 0;
            this.FilterLabel.Text = "Text Filter";
            this.FilterLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // EndpointsGridView
            // 
            this.EndpointsGridView.AllowUserToAddRows = false;
            this.EndpointsGridView.AllowUserToDeleteRows = false;
            this.EndpointsGridView.AllowUserToResizeRows = false;
            this.EndpointsGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.EndpointsGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.EndpointsGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.EndpointUrlColumn3,
            this.SecurityModeColumn,
            this.SecurityProfileColumn});
            this.EndpointsGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EndpointsGridView.Location = new System.Drawing.Point(0, 138);
            this.EndpointsGridView.MultiSelect = false;
            this.EndpointsGridView.Name = "EndpointsGridView";
            this.EndpointsGridView.ReadOnly = true;
            this.EndpointsGridView.RowHeadersVisible = false;
            this.EndpointsGridView.RowHeadersWidthSizeMode = System.Windows.Forms.DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            this.EndpointsGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.EndpointsGridView.Size = new System.Drawing.Size(588, 446);
            this.EndpointsGridView.TabIndex = 2;
            this.EndpointsGridView.VisibleChanged += new System.EventHandler(this.EndpointsGridView_VisibleChanged);
            this.EndpointsGridView.DoubleClick += new System.EventHandler(this.EndpointsGridView_DoubleClick);
            // 
            // EndpointUrlColumn3
            // 
            this.EndpointUrlColumn3.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.EndpointUrlColumn3.DataPropertyName = "EndpointUrl";
            this.EndpointUrlColumn3.HeaderText = "Endpoint URL";
            this.EndpointUrlColumn3.Name = "EndpointUrlColumn3";
            this.EndpointUrlColumn3.ReadOnly = true;
            // 
            // SecurityModeColumn
            // 
            this.SecurityModeColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.SecurityModeColumn.DataPropertyName = "SecurityMode";
            this.SecurityModeColumn.HeaderText = "Security Mode";
            this.SecurityModeColumn.Name = "SecurityModeColumn";
            this.SecurityModeColumn.ReadOnly = true;
            // 
            // SecurityProfileColumn
            // 
            this.SecurityProfileColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.SecurityProfileColumn.DataPropertyName = "SecurityProfile";
            this.SecurityProfileColumn.HeaderText = "Security Profile";
            this.SecurityProfileColumn.Name = "SecurityProfileColumn";
            this.SecurityProfileColumn.ReadOnly = true;
            this.SecurityProfileColumn.Width = 102;
            // 
            // DiscoveryControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.MainPanel);
            this.Name = "DiscoveryControl";
            this.Size = new System.Drawing.Size(888, 584);
            this.PopupMenuStrip.ResumeLayout(false);
            this.MainPanel.Panel1.ResumeLayout(false);
            this.MainPanel.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.MainPanel)).EndInit();
            this.MainPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ServersGridView)).EndInit();
            this.ApplicationDescriptionPanel.ResumeLayout(false);
            this.ApplicationDescriptionPanel.PerformLayout();
            this.FilterPanel.ResumeLayout(false);
            this.FilterPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.EndpointsGridView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem RefreshMenuItem;
        private System.Windows.Forms.ToolStripMenuItem RefreshWithParametersMenuItem;
        internal System.Windows.Forms.TreeView DiscoveryTreeView;
        private System.Windows.Forms.ToolStripSeparator AddEndpointSeparatorMenuItem;
        private System.Windows.Forms.ToolStripMenuItem AddEndpointMenuItem;
        private System.Windows.Forms.ToolStripMenuItem DeleteMenuItem;
        private System.Windows.Forms.SplitContainer MainPanel;
        private System.Windows.Forms.TableLayoutPanel ApplicationDescriptionPanel;
        private System.Windows.Forms.Label ProductUriTextBox;
        private System.Windows.Forms.Label ProductUriLabel;
        private System.Windows.Forms.Label ApplicationTypeTextBox;
        private System.Windows.Forms.Label ApplicationTypeLabel;
        private System.Windows.Forms.Label ApplicationUriTextBox;
        private System.Windows.Forms.Label ApplicationUriLabel;
        private System.Windows.Forms.Label ApplicationNameTextBox;
        private System.Windows.Forms.Label ApplicationNameLabel;
        private System.Windows.Forms.DataGridView ServersGridView;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ServerCapabilitiesColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn EndpointUrlColumn;
        private System.Windows.Forms.DataGridView EndpointsGridView;
        private System.Windows.Forms.Panel FilterPanel;
        private System.Windows.Forms.TextBox FilterTextBox;
        private System.Windows.Forms.Label FilterLabel;
        private System.Windows.Forms.DataGridViewTextBoxColumn EndpointUrlColumn3;
        private System.Windows.Forms.DataGridViewTextBoxColumn SecurityModeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn SecurityProfileColumn;
    }
}
