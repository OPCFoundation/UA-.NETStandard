namespace Opc.Ua.Gds.Client.Controls
{
    partial class CertificateStoreControl
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
            this.CertificateListGridView = new System.Windows.Forms.DataGridView();
            this.IconColumn = new System.Windows.Forms.DataGridViewImageColumn();
            this.StatusColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SubjectColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IssuerColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.IsCAColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.HasCrlColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ValidToColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ThumbprintColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.PopupMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.ViewMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.TrustMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.RejectMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.UntrustMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Seperator01MenuItem = new System.Windows.Forms.ToolStripSeparator();
            this.AddCrlMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteCrlMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.Seperator02MenuItem = new System.Windows.Forms.ToolStripSeparator();
            this.ImportMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ExportMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ImageList = new System.Windows.Forms.ImageList(this.components);
            this.NoDataWarningLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.CertificateListGridView)).BeginInit();
            this.PopupMenuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // CertificateListGridView
            // 
            this.CertificateListGridView.AllowUserToAddRows = false;
            this.CertificateListGridView.AllowUserToDeleteRows = false;
            this.CertificateListGridView.BackgroundColor = System.Drawing.SystemColors.Window;
            this.CertificateListGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.CertificateListGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.IconColumn,
            this.StatusColumn,
            this.SubjectColumn,
            this.IssuerColumn,
            this.IsCAColumn,
            this.HasCrlColumn,
            this.ValidToColumn,
            this.ThumbprintColumn});
            this.CertificateListGridView.ContextMenuStrip = this.PopupMenuStrip;
            this.CertificateListGridView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CertificateListGridView.Location = new System.Drawing.Point(0, 0);
            this.CertificateListGridView.Name = "CertificateListGridView";
            this.CertificateListGridView.ReadOnly = true;
            this.CertificateListGridView.RowHeadersVisible = false;
            this.CertificateListGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.CertificateListGridView.Size = new System.Drawing.Size(669, 394);
            this.CertificateListGridView.TabIndex = 1;
            this.CertificateListGridView.DoubleClick += new System.EventHandler(this.CertificateListGridView_DoubleClick);
            // 
            // IconColumn
            // 
            this.IconColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.IconColumn.DataPropertyName = "Icon";
            this.IconColumn.HeaderText = "";
            this.IconColumn.Name = "IconColumn";
            this.IconColumn.ReadOnly = true;
            this.IconColumn.Width = 5;
            // 
            // StatusColumn
            // 
            this.StatusColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.StatusColumn.DataPropertyName = "Status";
            this.StatusColumn.HeaderText = "Status";
            this.StatusColumn.Name = "StatusColumn";
            this.StatusColumn.ReadOnly = true;
            this.StatusColumn.Width = 62;
            // 
            // SubjectColumn
            // 
            this.SubjectColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.SubjectColumn.DataPropertyName = "Subject";
            this.SubjectColumn.FillWeight = 75F;
            this.SubjectColumn.HeaderText = "Subject";
            this.SubjectColumn.Name = "SubjectColumn";
            this.SubjectColumn.ReadOnly = true;
            // 
            // IssuerColumn
            // 
            this.IssuerColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.IssuerColumn.DataPropertyName = "Issuer";
            this.IssuerColumn.FillWeight = 25F;
            this.IssuerColumn.HeaderText = "Issuer";
            this.IssuerColumn.Name = "IssuerColumn";
            this.IssuerColumn.ReadOnly = true;
            // 
            // IsCAColumn
            // 
            this.IsCAColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.IsCAColumn.DataPropertyName = "IsCA";
            this.IsCAColumn.HeaderText = "Is CA";
            this.IsCAColumn.Name = "IsCAColumn";
            this.IsCAColumn.ReadOnly = true;
            this.IsCAColumn.Width = 57;
            // 
            // HasCrlColumn
            // 
            this.HasCrlColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.HasCrlColumn.DataPropertyName = "HasCrl";
            this.HasCrlColumn.HeaderText = "Has CRL";
            this.HasCrlColumn.Name = "HasCrlColumn";
            this.HasCrlColumn.ReadOnly = true;
            this.HasCrlColumn.Width = 75;
            // 
            // ValidToColumn
            // 
            this.ValidToColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.DisplayedCells;
            this.ValidToColumn.DataPropertyName = "ValidTo";
            this.ValidToColumn.HeaderText = "Expiry Date";
            this.ValidToColumn.Name = "ValidToColumn";
            this.ValidToColumn.ReadOnly = true;
            this.ValidToColumn.Width = 86;
            // 
            // ThumbprintColumn
            // 
            this.ThumbprintColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.ThumbprintColumn.DataPropertyName = "Thumbprint";
            this.ThumbprintColumn.HeaderText = "Thumbprint";
            this.ThumbprintColumn.Name = "ThumbprintColumn";
            this.ThumbprintColumn.ReadOnly = true;
            this.ThumbprintColumn.Width = 181;
            // 
            // PopupMenuStrip
            // 
            this.PopupMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ViewMenuItem,
            this.DeleteMenuItem,
            this.TrustMenuItem,
            this.RejectMenuItem,
            this.UntrustMenuItem,
            this.Seperator01MenuItem,
            this.AddCrlMenuItem,
            this.DeleteCrlMenuItem,
            this.Seperator02MenuItem,
            this.ImportMenuItem,
            this.ExportMenuItem});
            this.PopupMenuStrip.Name = "PopupMenuStrip";
            this.PopupMenuStrip.Size = new System.Drawing.Size(132, 214);
            this.PopupMenuStrip.Opening += new System.ComponentModel.CancelEventHandler(this.PopupMenuStrip_Opening);
            // 
            // ViewMenuItem
            // 
            this.ViewMenuItem.Name = "ViewMenuItem";
            this.ViewMenuItem.Size = new System.Drawing.Size(131, 22);
            this.ViewMenuItem.Text = "View...";
            this.ViewMenuItem.Click += new System.EventHandler(this.ViewMenuItem_Click);
            // 
            // DeleteMenuItem
            // 
            this.DeleteMenuItem.Name = "DeleteMenuItem";
            this.DeleteMenuItem.Size = new System.Drawing.Size(131, 22);
            this.DeleteMenuItem.Text = "Delete";
            this.DeleteMenuItem.Click += new System.EventHandler(this.DeleteMenuItem_Click);
            // 
            // TrustMenuItem
            // 
            this.TrustMenuItem.Name = "TrustMenuItem";
            this.TrustMenuItem.Size = new System.Drawing.Size(131, 22);
            this.TrustMenuItem.Text = "Trust";
            this.TrustMenuItem.Click += new System.EventHandler(this.TrustMenuItem_Click);
            // 
            // RejectMenuItem
            // 
            this.RejectMenuItem.Name = "RejectMenuItem";
            this.RejectMenuItem.Size = new System.Drawing.Size(131, 22);
            this.RejectMenuItem.Text = "Reject";
            this.RejectMenuItem.Click += new System.EventHandler(this.Reject_MenuItem_Click);
            // 
            // UntrustMenuItem
            // 
            this.UntrustMenuItem.Name = "UntrustMenuItem";
            this.UntrustMenuItem.Size = new System.Drawing.Size(131, 22);
            this.UntrustMenuItem.Text = "Untrust";
            this.UntrustMenuItem.Click += new System.EventHandler(this.UntrustMenuItem_Click);
            // 
            // Seperator01MenuItem
            // 
            this.Seperator01MenuItem.Name = "Seperator01MenuItem";
            this.Seperator01MenuItem.Size = new System.Drawing.Size(128, 6);
            // 
            // AddCrlMenuItem
            // 
            this.AddCrlMenuItem.Name = "AddCrlMenuItem";
            this.AddCrlMenuItem.Size = new System.Drawing.Size(131, 22);
            this.AddCrlMenuItem.Text = "Add CRL...";
            // 
            // DeleteCrlMenuItem
            // 
            this.DeleteCrlMenuItem.Name = "DeleteCrlMenuItem";
            this.DeleteCrlMenuItem.Size = new System.Drawing.Size(131, 22);
            this.DeleteCrlMenuItem.Text = "Delete CRL";
            // 
            // Seperator02MenuItem
            // 
            this.Seperator02MenuItem.Name = "Seperator02MenuItem";
            this.Seperator02MenuItem.Size = new System.Drawing.Size(128, 6);
            // 
            // ImportMenuItem
            // 
            this.ImportMenuItem.Name = "ImportMenuItem";
            this.ImportMenuItem.Size = new System.Drawing.Size(131, 22);
            this.ImportMenuItem.Text = "Import...";
            this.ImportMenuItem.Click += new System.EventHandler(this.ImportMenuItem_Click);
            // 
            // ExportMenuItem
            // 
            this.ExportMenuItem.Name = "ExportMenuItem";
            this.ExportMenuItem.Size = new System.Drawing.Size(131, 22);
            this.ExportMenuItem.Text = "Export...";
            this.ExportMenuItem.Click += new System.EventHandler(this.ExportMenuItem_Click);
            // 
            // ImageList
            // 
            this.ImageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            this.ImageList.ImageSize = new System.Drawing.Size(16, 16);
            this.ImageList.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // NoDataWarningLabel
            // 
            this.NoDataWarningLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.NoDataWarningLabel.AutoSize = true;
            this.NoDataWarningLabel.BackColor = System.Drawing.Color.Red;
            this.NoDataWarningLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.NoDataWarningLabel.ForeColor = System.Drawing.Color.White;
            this.NoDataWarningLabel.Location = new System.Drawing.Point(7, 361);
            this.NoDataWarningLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.NoDataWarningLabel.Name = "NoDataWarningLabel";
            this.NoDataWarningLabel.Padding = new System.Windows.Forms.Padding(3);
            this.NoDataWarningLabel.Size = new System.Drawing.Size(247, 26);
            this.NoDataWarningLabel.TabIndex = 2;
            this.NoDataWarningLabel.Text = "No certificates found in store";
            // 
            // CertificateStoreControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.NoDataWarningLabel);
            this.Controls.Add(this.CertificateListGridView);
            this.Name = "CertificateStoreControl";
            this.Size = new System.Drawing.Size(669, 394);
            ((System.ComponentModel.ISupportInitialize)(this.CertificateListGridView)).EndInit();
            this.PopupMenuStrip.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView CertificateListGridView;
        private System.Windows.Forms.ContextMenuStrip PopupMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem ViewMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ImportMenuItem;
        private System.Windows.Forms.ToolStripMenuItem ExportMenuItem;
        private System.Windows.Forms.ToolStripMenuItem DeleteMenuItem;
        private System.Windows.Forms.ToolStripSeparator Seperator02MenuItem;
        private System.Windows.Forms.ToolStripMenuItem TrustMenuItem;
        private System.Windows.Forms.ToolStripMenuItem UntrustMenuItem;
        private System.Windows.Forms.ToolStripMenuItem AddCrlMenuItem;
        private System.Windows.Forms.ToolStripMenuItem DeleteCrlMenuItem;
        private System.Windows.Forms.ToolStripSeparator Seperator01MenuItem;
        private System.Windows.Forms.ToolStripMenuItem RejectMenuItem;
        private System.Windows.Forms.ImageList ImageList;
        private System.Windows.Forms.Label NoDataWarningLabel;
        private System.Windows.Forms.DataGridViewImageColumn IconColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn StatusColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn SubjectColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn IssuerColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn IsCAColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn HasCrlColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ValidToColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn ThumbprintColumn;
    }
}
