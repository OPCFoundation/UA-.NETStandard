namespace Quickstarts.ReferenceClient
{
    partial class WriteOutput
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
            this.writeRequestListViewCtrl1 = new Opc.Ua.Client.Controls.WriteRequestListViewCtrl();
            this.button1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // writeRequestListViewCtrl1
            // 
            this.writeRequestListViewCtrl1.Location = new System.Drawing.Point(0, 0);
            this.writeRequestListViewCtrl1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.writeRequestListViewCtrl1.Name = "writeRequestListViewCtrl1";
            this.writeRequestListViewCtrl1.Size = new System.Drawing.Size(798, 426);
            this.writeRequestListViewCtrl1.TabIndex = 0;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(342, 445);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 1;
            this.button1.Text = "Write";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // WriteOutput
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(805, 509);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.writeRequestListViewCtrl1);
            this.Name = "WriteOutput";
            this.Text = "WriteOutput";
            this.ResumeLayout(false);

        }

        #endregion

        private Opc.Ua.Client.Controls.WriteRequestListViewCtrl writeRequestListViewCtrl1;
        private System.Windows.Forms.Button button1;
    }
}
