namespace Opc.Ua.Gds.Client.Controls
{
    partial class ServerStatusControl
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
            this.RegistrationButtonsPanel = new System.Windows.Forms.Panel();
            this.ApplyChangesButton = new System.Windows.Forms.Button();
            this.ServerStatusPanel = new System.Windows.Forms.Panel();
            this.ServerBrowseControl = new Opc.Ua.Client.Controls.BrowseNodeCtrl();
            this.ServerStatusFieldsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.ShutdownReasonTextBox = new System.Windows.Forms.Label();
            this.ShutdownReasonLabel = new System.Windows.Forms.Label();
            this.SecondsUntilShutdownTextBox = new System.Windows.Forms.Label();
            this.SecondsUntilShutdownLabel = new System.Windows.Forms.Label();
            this.ProductUriTextBox = new System.Windows.Forms.Label();
            this.ProductUriLabel = new System.Windows.Forms.Label();
            this.StateTextBox = new System.Windows.Forms.Label();
            this.StartTimeTextBox = new System.Windows.Forms.Label();
            this.CurrentTimeTextBox = new System.Windows.Forms.Label();
            this.StateLabel = new System.Windows.Forms.Label();
            this.CurrentTimeLabel = new System.Windows.Forms.Label();
            this.StartTimeLabel = new System.Windows.Forms.Label();
            this.BuildNumberTextBox = new System.Windows.Forms.Label();
            this.BuildDateTextBox = new System.Windows.Forms.Label();
            this.BuildDateLabel = new System.Windows.Forms.Label();
            this.BuildNumberLabel = new System.Windows.Forms.Label();
            this.SoftwareVersionTextBox = new System.Windows.Forms.Label();
            this.SoftwareVersionLabel = new System.Windows.Forms.Label();
            this.ManufacturerNameTextBox = new System.Windows.Forms.Label();
            this.ManufacturerNameLabel = new System.Windows.Forms.Label();
            this.ProductNameTextBox = new System.Windows.Forms.Label();
            this.ProductNameLabel = new System.Windows.Forms.Label();
            this.RegistrationButtonsPanel.SuspendLayout();
            this.ServerStatusPanel.SuspendLayout();
            this.ServerStatusFieldsPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // RegistrationButtonsPanel
            // 
            this.RegistrationButtonsPanel.BackColor = System.Drawing.Color.MidnightBlue;
            this.RegistrationButtonsPanel.Controls.Add(this.ApplyChangesButton);
            this.RegistrationButtonsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.RegistrationButtonsPanel.Location = new System.Drawing.Point(0, 612);
            this.RegistrationButtonsPanel.Name = "RegistrationButtonsPanel";
            this.RegistrationButtonsPanel.Size = new System.Drawing.Size(879, 32);
            this.RegistrationButtonsPanel.TabIndex = 14;
            // 
            // ApplyChangesButton
            // 
            this.ApplyChangesButton.BackColor = System.Drawing.Color.MidnightBlue;
            this.ApplyChangesButton.Dock = System.Windows.Forms.DockStyle.Left;
            this.ApplyChangesButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ApplyChangesButton.ForeColor = System.Drawing.Color.White;
            this.ApplyChangesButton.Location = new System.Drawing.Point(0, 0);
            this.ApplyChangesButton.Name = "ApplyChangesButton";
            this.ApplyChangesButton.Size = new System.Drawing.Size(129, 32);
            this.ApplyChangesButton.TabIndex = 3;
            this.ApplyChangesButton.Text = "Apply Changes";
            this.ApplyChangesButton.UseVisualStyleBackColor = false;
            this.ApplyChangesButton.Click += new System.EventHandler(this.ApplyChangesButton_Click);
            this.ApplyChangesButton.MouseEnter += new System.EventHandler(this.Button_MouseEnter);
            this.ApplyChangesButton.MouseLeave += new System.EventHandler(this.Button_MouseLeave);
            // 
            // ServerStatusPanel
            // 
            this.ServerStatusPanel.Controls.Add(this.ServerBrowseControl);
            this.ServerStatusPanel.Controls.Add(this.ServerStatusFieldsPanel);
            this.ServerStatusPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerStatusPanel.Location = new System.Drawing.Point(0, 0);
            this.ServerStatusPanel.Name = "ServerStatusPanel";
            this.ServerStatusPanel.Size = new System.Drawing.Size(879, 612);
            this.ServerStatusPanel.TabIndex = 17;
            // 
            // ServerBrowseControl
            // 
            this.ServerBrowseControl.AttributesListCollapsed = false;
            this.ServerBrowseControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ServerBrowseControl.Location = new System.Drawing.Point(0, 230);
            this.ServerBrowseControl.Name = "ServerBrowseControl";
            this.ServerBrowseControl.Size = new System.Drawing.Size(879, 382);
            this.ServerBrowseControl.SplitterDistance = 387;
            this.ServerBrowseControl.TabIndex = 1;
            this.ServerBrowseControl.View = null;
            // 
            // ServerStatusFieldsPanel
            // 
            this.ServerStatusFieldsPanel.ColumnCount = 2;
            this.ServerStatusFieldsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ServerStatusFieldsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ServerStatusFieldsPanel.Controls.Add(this.ShutdownReasonTextBox, 1, 10);
            this.ServerStatusFieldsPanel.Controls.Add(this.ShutdownReasonLabel, 0, 10);
            this.ServerStatusFieldsPanel.Controls.Add(this.SecondsUntilShutdownTextBox, 1, 9);
            this.ServerStatusFieldsPanel.Controls.Add(this.SecondsUntilShutdownLabel, 0, 9);
            this.ServerStatusFieldsPanel.Controls.Add(this.ProductUriTextBox, 1, 1);
            this.ServerStatusFieldsPanel.Controls.Add(this.ProductUriLabel, 0, 1);
            this.ServerStatusFieldsPanel.Controls.Add(this.StateTextBox, 1, 8);
            this.ServerStatusFieldsPanel.Controls.Add(this.StartTimeTextBox, 1, 6);
            this.ServerStatusFieldsPanel.Controls.Add(this.CurrentTimeTextBox, 1, 7);
            this.ServerStatusFieldsPanel.Controls.Add(this.StateLabel, 0, 8);
            this.ServerStatusFieldsPanel.Controls.Add(this.CurrentTimeLabel, 0, 7);
            this.ServerStatusFieldsPanel.Controls.Add(this.StartTimeLabel, 0, 6);
            this.ServerStatusFieldsPanel.Controls.Add(this.BuildNumberTextBox, 1, 4);
            this.ServerStatusFieldsPanel.Controls.Add(this.BuildDateTextBox, 1, 5);
            this.ServerStatusFieldsPanel.Controls.Add(this.BuildDateLabel, 0, 5);
            this.ServerStatusFieldsPanel.Controls.Add(this.BuildNumberLabel, 0, 4);
            this.ServerStatusFieldsPanel.Controls.Add(this.SoftwareVersionTextBox, 1, 3);
            this.ServerStatusFieldsPanel.Controls.Add(this.SoftwareVersionLabel, 0, 3);
            this.ServerStatusFieldsPanel.Controls.Add(this.ManufacturerNameTextBox, 1, 2);
            this.ServerStatusFieldsPanel.Controls.Add(this.ManufacturerNameLabel, 0, 2);
            this.ServerStatusFieldsPanel.Controls.Add(this.ProductNameTextBox, 1, 0);
            this.ServerStatusFieldsPanel.Controls.Add(this.ProductNameLabel, 0, 0);
            this.ServerStatusFieldsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ServerStatusFieldsPanel.Location = new System.Drawing.Point(0, 0);
            this.ServerStatusFieldsPanel.Margin = new System.Windows.Forms.Padding(2);
            this.ServerStatusFieldsPanel.Name = "ServerStatusFieldsPanel";
            this.ServerStatusFieldsPanel.Padding = new System.Windows.Forms.Padding(3);
            this.ServerStatusFieldsPanel.RowCount = 11;
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.ServerStatusFieldsPanel.Size = new System.Drawing.Size(879, 230);
            this.ServerStatusFieldsPanel.TabIndex = 0;
            // 
            // ShutdownReasonTextBox
            // 
            this.ShutdownReasonTextBox.AllowDrop = true;
            this.ShutdownReasonTextBox.AutoSize = true;
            this.ShutdownReasonTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ShutdownReasonTextBox.Location = new System.Drawing.Point(133, 205);
            this.ShutdownReasonTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.ShutdownReasonTextBox.Name = "ShutdownReasonTextBox";
            this.ShutdownReasonTextBox.Size = new System.Drawing.Size(741, 20);
            this.ShutdownReasonTextBox.TabIndex = 21;
            this.ShutdownReasonTextBox.Text = "---";
            this.ShutdownReasonTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ShutdownReasonLabel
            // 
            this.ShutdownReasonLabel.AllowDrop = true;
            this.ShutdownReasonLabel.AutoSize = true;
            this.ShutdownReasonLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ShutdownReasonLabel.Location = new System.Drawing.Point(5, 205);
            this.ShutdownReasonLabel.Margin = new System.Windows.Forms.Padding(2);
            this.ShutdownReasonLabel.Name = "ShutdownReasonLabel";
            this.ShutdownReasonLabel.Size = new System.Drawing.Size(124, 20);
            this.ShutdownReasonLabel.TabIndex = 20;
            this.ShutdownReasonLabel.Text = "Shutdown Reason";
            this.ShutdownReasonLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecondsUntilShutdownTextBox
            // 
            this.SecondsUntilShutdownTextBox.AllowDrop = true;
            this.SecondsUntilShutdownTextBox.AutoSize = true;
            this.SecondsUntilShutdownTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SecondsUntilShutdownTextBox.Location = new System.Drawing.Point(133, 185);
            this.SecondsUntilShutdownTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.SecondsUntilShutdownTextBox.Name = "SecondsUntilShutdownTextBox";
            this.SecondsUntilShutdownTextBox.Size = new System.Drawing.Size(741, 16);
            this.SecondsUntilShutdownTextBox.TabIndex = 19;
            this.SecondsUntilShutdownTextBox.Text = "---";
            this.SecondsUntilShutdownTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SecondsUntilShutdownLabel
            // 
            this.SecondsUntilShutdownLabel.AllowDrop = true;
            this.SecondsUntilShutdownLabel.AutoSize = true;
            this.SecondsUntilShutdownLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SecondsUntilShutdownLabel.Location = new System.Drawing.Point(5, 185);
            this.SecondsUntilShutdownLabel.Margin = new System.Windows.Forms.Padding(2);
            this.SecondsUntilShutdownLabel.Name = "SecondsUntilShutdownLabel";
            this.SecondsUntilShutdownLabel.Size = new System.Drawing.Size(124, 16);
            this.SecondsUntilShutdownLabel.TabIndex = 18;
            this.SecondsUntilShutdownLabel.Text = "Seconds Until Shutdown";
            this.SecondsUntilShutdownLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriTextBox
            // 
            this.ProductUriTextBox.AllowDrop = true;
            this.ProductUriTextBox.AutoSize = true;
            this.ProductUriTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriTextBox.Location = new System.Drawing.Point(133, 25);
            this.ProductUriTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.ProductUriTextBox.Name = "ProductUriTextBox";
            this.ProductUriTextBox.Size = new System.Drawing.Size(741, 16);
            this.ProductUriTextBox.TabIndex = 3;
            this.ProductUriTextBox.Text = "---";
            this.ProductUriTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductUriLabel
            // 
            this.ProductUriLabel.AllowDrop = true;
            this.ProductUriLabel.AutoSize = true;
            this.ProductUriLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductUriLabel.Location = new System.Drawing.Point(5, 25);
            this.ProductUriLabel.Margin = new System.Windows.Forms.Padding(2);
            this.ProductUriLabel.Name = "ProductUriLabel";
            this.ProductUriLabel.Size = new System.Drawing.Size(124, 16);
            this.ProductUriLabel.TabIndex = 2;
            this.ProductUriLabel.Text = "Product URI";
            this.ProductUriLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StateTextBox
            // 
            this.StateTextBox.AllowDrop = true;
            this.StateTextBox.AutoSize = true;
            this.StateTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StateTextBox.Location = new System.Drawing.Point(133, 165);
            this.StateTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.StateTextBox.Name = "StateTextBox";
            this.StateTextBox.Size = new System.Drawing.Size(741, 16);
            this.StateTextBox.TabIndex = 17;
            this.StateTextBox.Text = "---";
            this.StateTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeTextBox
            // 
            this.StartTimeTextBox.AllowDrop = true;
            this.StartTimeTextBox.AutoSize = true;
            this.StartTimeTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StartTimeTextBox.Location = new System.Drawing.Point(133, 125);
            this.StartTimeTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.StartTimeTextBox.Name = "StartTimeTextBox";
            this.StartTimeTextBox.Size = new System.Drawing.Size(741, 16);
            this.StartTimeTextBox.TabIndex = 13;
            this.StartTimeTextBox.Text = "---";
            this.StartTimeTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CurrentTimeTextBox
            // 
            this.CurrentTimeTextBox.AllowDrop = true;
            this.CurrentTimeTextBox.AutoSize = true;
            this.CurrentTimeTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CurrentTimeTextBox.Location = new System.Drawing.Point(133, 145);
            this.CurrentTimeTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.CurrentTimeTextBox.Name = "CurrentTimeTextBox";
            this.CurrentTimeTextBox.Size = new System.Drawing.Size(741, 16);
            this.CurrentTimeTextBox.TabIndex = 15;
            this.CurrentTimeTextBox.Text = "---";
            this.CurrentTimeTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StateLabel
            // 
            this.StateLabel.AllowDrop = true;
            this.StateLabel.AutoSize = true;
            this.StateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StateLabel.Location = new System.Drawing.Point(5, 165);
            this.StateLabel.Margin = new System.Windows.Forms.Padding(2);
            this.StateLabel.Name = "StateLabel";
            this.StateLabel.Size = new System.Drawing.Size(124, 16);
            this.StateLabel.TabIndex = 16;
            this.StateLabel.Text = "State";
            this.StateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CurrentTimeLabel
            // 
            this.CurrentTimeLabel.AllowDrop = true;
            this.CurrentTimeLabel.AutoSize = true;
            this.CurrentTimeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.CurrentTimeLabel.Location = new System.Drawing.Point(5, 145);
            this.CurrentTimeLabel.Margin = new System.Windows.Forms.Padding(2);
            this.CurrentTimeLabel.Name = "CurrentTimeLabel";
            this.CurrentTimeLabel.Size = new System.Drawing.Size(124, 16);
            this.CurrentTimeLabel.TabIndex = 14;
            this.CurrentTimeLabel.Text = "Current Time";
            this.CurrentTimeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeLabel
            // 
            this.StartTimeLabel.AllowDrop = true;
            this.StartTimeLabel.AutoSize = true;
            this.StartTimeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.StartTimeLabel.Location = new System.Drawing.Point(5, 125);
            this.StartTimeLabel.Margin = new System.Windows.Forms.Padding(2);
            this.StartTimeLabel.Name = "StartTimeLabel";
            this.StartTimeLabel.Size = new System.Drawing.Size(124, 16);
            this.StartTimeLabel.TabIndex = 12;
            this.StartTimeLabel.Text = "Start Time";
            this.StartTimeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BuildNumberTextBox
            // 
            this.BuildNumberTextBox.AllowDrop = true;
            this.BuildNumberTextBox.AutoSize = true;
            this.BuildNumberTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BuildNumberTextBox.Location = new System.Drawing.Point(133, 85);
            this.BuildNumberTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.BuildNumberTextBox.Name = "BuildNumberTextBox";
            this.BuildNumberTextBox.Size = new System.Drawing.Size(741, 16);
            this.BuildNumberTextBox.TabIndex = 9;
            this.BuildNumberTextBox.Text = "---";
            this.BuildNumberTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BuildDateTextBox
            // 
            this.BuildDateTextBox.AllowDrop = true;
            this.BuildDateTextBox.AutoSize = true;
            this.BuildDateTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BuildDateTextBox.Location = new System.Drawing.Point(133, 105);
            this.BuildDateTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.BuildDateTextBox.Name = "BuildDateTextBox";
            this.BuildDateTextBox.Size = new System.Drawing.Size(741, 16);
            this.BuildDateTextBox.TabIndex = 11;
            this.BuildDateTextBox.Text = "---";
            this.BuildDateTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BuildDateLabel
            // 
            this.BuildDateLabel.AllowDrop = true;
            this.BuildDateLabel.AutoSize = true;
            this.BuildDateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BuildDateLabel.Location = new System.Drawing.Point(5, 105);
            this.BuildDateLabel.Margin = new System.Windows.Forms.Padding(2);
            this.BuildDateLabel.Name = "BuildDateLabel";
            this.BuildDateLabel.Size = new System.Drawing.Size(124, 16);
            this.BuildDateLabel.TabIndex = 10;
            this.BuildDateLabel.Text = "Build Date";
            this.BuildDateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // BuildNumberLabel
            // 
            this.BuildNumberLabel.AllowDrop = true;
            this.BuildNumberLabel.AutoSize = true;
            this.BuildNumberLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.BuildNumberLabel.Location = new System.Drawing.Point(5, 85);
            this.BuildNumberLabel.Margin = new System.Windows.Forms.Padding(2);
            this.BuildNumberLabel.Name = "BuildNumberLabel";
            this.BuildNumberLabel.Size = new System.Drawing.Size(124, 16);
            this.BuildNumberLabel.TabIndex = 8;
            this.BuildNumberLabel.Text = "Build Number";
            this.BuildNumberLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SoftwareVersionTextBox
            // 
            this.SoftwareVersionTextBox.AllowDrop = true;
            this.SoftwareVersionTextBox.AutoSize = true;
            this.SoftwareVersionTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SoftwareVersionTextBox.Location = new System.Drawing.Point(133, 65);
            this.SoftwareVersionTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.SoftwareVersionTextBox.Name = "SoftwareVersionTextBox";
            this.SoftwareVersionTextBox.Size = new System.Drawing.Size(741, 16);
            this.SoftwareVersionTextBox.TabIndex = 7;
            this.SoftwareVersionTextBox.Text = "---";
            this.SoftwareVersionTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SoftwareVersionLabel
            // 
            this.SoftwareVersionLabel.AllowDrop = true;
            this.SoftwareVersionLabel.AutoSize = true;
            this.SoftwareVersionLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SoftwareVersionLabel.Location = new System.Drawing.Point(5, 65);
            this.SoftwareVersionLabel.Margin = new System.Windows.Forms.Padding(2);
            this.SoftwareVersionLabel.Name = "SoftwareVersionLabel";
            this.SoftwareVersionLabel.Size = new System.Drawing.Size(124, 16);
            this.SoftwareVersionLabel.TabIndex = 6;
            this.SoftwareVersionLabel.Text = "Software Version";
            this.SoftwareVersionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ManufacturerNameTextBox
            // 
            this.ManufacturerNameTextBox.AllowDrop = true;
            this.ManufacturerNameTextBox.AutoSize = true;
            this.ManufacturerNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ManufacturerNameTextBox.Location = new System.Drawing.Point(133, 45);
            this.ManufacturerNameTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.ManufacturerNameTextBox.Name = "ManufacturerNameTextBox";
            this.ManufacturerNameTextBox.Size = new System.Drawing.Size(741, 16);
            this.ManufacturerNameTextBox.TabIndex = 5;
            this.ManufacturerNameTextBox.Text = "---";
            this.ManufacturerNameTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ManufacturerNameLabel
            // 
            this.ManufacturerNameLabel.AllowDrop = true;
            this.ManufacturerNameLabel.AutoSize = true;
            this.ManufacturerNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ManufacturerNameLabel.Location = new System.Drawing.Point(5, 45);
            this.ManufacturerNameLabel.Margin = new System.Windows.Forms.Padding(2);
            this.ManufacturerNameLabel.Name = "ManufacturerNameLabel";
            this.ManufacturerNameLabel.Size = new System.Drawing.Size(124, 16);
            this.ManufacturerNameLabel.TabIndex = 4;
            this.ManufacturerNameLabel.Text = "Manufacturer Name";
            this.ManufacturerNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductNameTextBox
            // 
            this.ProductNameTextBox.AllowDrop = true;
            this.ProductNameTextBox.AutoSize = true;
            this.ProductNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductNameTextBox.Location = new System.Drawing.Point(133, 5);
            this.ProductNameTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.ProductNameTextBox.Name = "ProductNameTextBox";
            this.ProductNameTextBox.Size = new System.Drawing.Size(741, 16);
            this.ProductNameTextBox.TabIndex = 1;
            this.ProductNameTextBox.Text = "---";
            this.ProductNameTextBox.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProductNameLabel
            // 
            this.ProductNameLabel.AllowDrop = true;
            this.ProductNameLabel.AutoSize = true;
            this.ProductNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProductNameLabel.Location = new System.Drawing.Point(5, 5);
            this.ProductNameLabel.Margin = new System.Windows.Forms.Padding(2);
            this.ProductNameLabel.Name = "ProductNameLabel";
            this.ProductNameLabel.Size = new System.Drawing.Size(124, 16);
            this.ProductNameLabel.TabIndex = 0;
            this.ProductNameLabel.Text = "Product Name";
            this.ProductNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ServerStatusControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ServerStatusPanel);
            this.Controls.Add(this.RegistrationButtonsPanel);
            this.Name = "ServerStatusControl";
            this.Size = new System.Drawing.Size(879, 644);
            this.RegistrationButtonsPanel.ResumeLayout(false);
            this.ServerStatusPanel.ResumeLayout(false);
            this.ServerStatusFieldsPanel.ResumeLayout(false);
            this.ServerStatusFieldsPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel RegistrationButtonsPanel;
        private System.Windows.Forms.Panel ServerStatusPanel;
        private Opc.Ua.Client.Controls.BrowseNodeCtrl ServerBrowseControl;
        private System.Windows.Forms.TableLayoutPanel ServerStatusFieldsPanel;
        private System.Windows.Forms.Label ShutdownReasonTextBox;
        private System.Windows.Forms.Label ShutdownReasonLabel;
        private System.Windows.Forms.Label SecondsUntilShutdownTextBox;
        private System.Windows.Forms.Label SecondsUntilShutdownLabel;
        private System.Windows.Forms.Label ProductUriTextBox;
        private System.Windows.Forms.Label ProductUriLabel;
        private System.Windows.Forms.Label StateTextBox;
        private System.Windows.Forms.Label StartTimeTextBox;
        private System.Windows.Forms.Label CurrentTimeTextBox;
        private System.Windows.Forms.Label StateLabel;
        private System.Windows.Forms.Label CurrentTimeLabel;
        private System.Windows.Forms.Label StartTimeLabel;
        private System.Windows.Forms.Label BuildNumberTextBox;
        private System.Windows.Forms.Label BuildDateTextBox;
        private System.Windows.Forms.Label BuildDateLabel;
        private System.Windows.Forms.Label BuildNumberLabel;
        private System.Windows.Forms.Label SoftwareVersionTextBox;
        private System.Windows.Forms.Label SoftwareVersionLabel;
        private System.Windows.Forms.Label ManufacturerNameTextBox;
        private System.Windows.Forms.Label ManufacturerNameLabel;
        private System.Windows.Forms.Label ProductNameTextBox;
        private System.Windows.Forms.Label ProductNameLabel;
        private System.Windows.Forms.Button ApplyChangesButton;
    }
}
