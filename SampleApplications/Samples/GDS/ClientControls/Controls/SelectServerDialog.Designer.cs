namespace Opc.Ua.Gds.Client.Controls
{
    partial class SelectServerDialog
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.TheCancelButton = new System.Windows.Forms.Button();
            this.TheOkButton = new System.Windows.Forms.Button();
            this.DiscoveryControl = new Opc.Ua.Gds.Client.Controls.DiscoveryControl();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.TheCancelButton);
            this.panel1.Controls.Add(this.TheOkButton);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 532);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(784, 30);
            this.panel1.TabIndex = 6;
            // 
            // TheCancelButton
            // 
            this.TheCancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.TheCancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.TheCancelButton.Location = new System.Drawing.Point(706, 3);
            this.TheCancelButton.Name = "TheCancelButton";
            this.TheCancelButton.Size = new System.Drawing.Size(75, 24);
            this.TheCancelButton.TabIndex = 1;
            this.TheCancelButton.Text = "Cancel";
            this.TheCancelButton.UseVisualStyleBackColor = true;
            // 
            // TheOkButton
            // 
            this.TheOkButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.TheOkButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.TheOkButton.Location = new System.Drawing.Point(3, 3);
            this.TheOkButton.Name = "TheOkButton";
            this.TheOkButton.Size = new System.Drawing.Size(75, 24);
            this.TheOkButton.TabIndex = 0;
            this.TheOkButton.Text = "OK";
            this.TheOkButton.UseVisualStyleBackColor = true;
            // 
            // DiscoveryControl
            // 
            this.DiscoveryControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.DiscoveryControl.Location = new System.Drawing.Point(0, 0);
            this.DiscoveryControl.Name = "DiscoveryControl";
            this.DiscoveryControl.Padding = new System.Windows.Forms.Padding(3);
            this.DiscoveryControl.Size = new System.Drawing.Size(784, 532);
            this.DiscoveryControl.SplitterDistance = 259;
            this.DiscoveryControl.TabIndex = 5;
            // 
            // SelectServerDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 562);
            this.Controls.Add(this.DiscoveryControl);
            this.Controls.Add(this.panel1);
            this.Name = "SelectServerDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Select Server";
            this.panel1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private DiscoveryControl DiscoveryControl;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button TheCancelButton;
        private System.Windows.Forms.Button TheOkButton;
    }
}