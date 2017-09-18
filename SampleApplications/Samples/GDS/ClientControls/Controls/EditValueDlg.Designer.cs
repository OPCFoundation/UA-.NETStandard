
namespace Opc.Ua.Gds.Client.Controls
{
    partial class EditValueDlg
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
            this.MainPN = new System.Windows.Forms.Panel();
            this.ValueCTRL = new Opc.Ua.Gds.Client.Controls.EditValueCtrl();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.ButtonsPN = new System.Windows.Forms.FlowLayoutPanel();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.BackBTN = new System.Windows.Forms.Button();
            this.SetArraySizeBTN = new System.Windows.Forms.Button();
            this.SetTypeCB = new System.Windows.Forms.ComboBox();
            this.OkBTN = new System.Windows.Forms.Button();
            this.MainPN.SuspendLayout();
            this.BottomPN.SuspendLayout();
            this.ButtonsPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MainPN
            // 
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ValueCTRL);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(658, 219);
            this.MainPN.TabIndex = 1;
            // 
            // ValueCTRL
            // 
            this.ValueCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ValueCTRL.Location = new System.Drawing.Point(0, 0);
            this.ValueCTRL.Name = "ValueCTRL";
            this.ValueCTRL.Padding = new System.Windows.Forms.Padding(3);
            this.ValueCTRL.Size = new System.Drawing.Size(658, 219);
            this.ValueCTRL.TabIndex = 0;
            this.ValueCTRL.ValueChanged += new System.EventHandler(this.ValueCTRL_ValueChanged);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.ButtonsPN);
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 219);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(658, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Controls.Add(this.BackBTN);
            this.ButtonsPN.Controls.Add(this.SetArraySizeBTN);
            this.ButtonsPN.Controls.Add(this.SetTypeCB);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Right;
            this.ButtonsPN.Location = new System.Drawing.Point(174, 0);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.ButtonsPN.Size = new System.Drawing.Size(484, 30);
            this.ButtonsPN.TabIndex = 0;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(406, 3);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // BackBTN
            // 
            this.BackBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.BackBTN.Location = new System.Drawing.Point(325, 3);
            this.BackBTN.Name = "BackBTN";
            this.BackBTN.Size = new System.Drawing.Size(75, 23);
            this.BackBTN.TabIndex = 2;
            this.BackBTN.Text = "Back";
            this.BackBTN.UseVisualStyleBackColor = true;
            this.BackBTN.Click += new System.EventHandler(this.BackBTN_Click);
            // 
            // SetArraySizeBTN
            // 
            this.SetArraySizeBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.SetArraySizeBTN.Location = new System.Drawing.Point(229, 3);
            this.SetArraySizeBTN.Name = "SetArraySizeBTN";
            this.SetArraySizeBTN.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.SetArraySizeBTN.Size = new System.Drawing.Size(90, 23);
            this.SetArraySizeBTN.TabIndex = 3;
            this.SetArraySizeBTN.Text = "Set Array Size...";
            this.SetArraySizeBTN.UseVisualStyleBackColor = true;
            this.SetArraySizeBTN.Click += new System.EventHandler(this.SetTypeBTN_Click);
            // 
            // SetTypeCB
            // 
            this.SetTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.SetTypeCB.FormattingEnabled = true;
            this.SetTypeCB.Location = new System.Drawing.Point(108, 4);
            this.SetTypeCB.Margin = new System.Windows.Forms.Padding(3, 4, 3, 3);
            this.SetTypeCB.Name = "SetTypeCB";
            this.SetTypeCB.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.SetTypeCB.Size = new System.Drawing.Size(115, 21);
            this.SetTypeCB.TabIndex = 4;
            this.SetTypeCB.SelectedIndexChanged += new System.EventHandler(this.SetTypeCB_SelectedIndexChanged);
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.Location = new System.Drawing.Point(3, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            this.OkBTN.Click += new System.EventHandler(this.OkBTN_Click);
            // 
            // EditValueDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(658, 249);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.MaximumSize = new System.Drawing.Size(3000, 1000);
            this.MinimumSize = new System.Drawing.Size(50, 100);
            this.Name = "EditValueDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Value";
            this.MainPN.ResumeLayout(false);
            this.BottomPN.ResumeLayout(false);
            this.ButtonsPN.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel MainPN;
        private EditValueCtrl ValueCTRL;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.FlowLayoutPanel ButtonsPN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button BackBTN;
        private System.Windows.Forms.Button SetArraySizeBTN;
        private System.Windows.Forms.ComboBox SetTypeCB;
    }
}
