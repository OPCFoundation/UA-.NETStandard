/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.Gds.Client.Controls
{
    partial class UserIdentityDialog
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
            this.CloseButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN = new System.Windows.Forms.TableLayoutPanel();
            this.RememberPasswordLabel = new System.Windows.Forms.Label();
            this.PasswordLabel = new System.Windows.Forms.Label();
            this.UserNameLabel = new System.Windows.Forms.Label();
            this.PasswordTextBox = new System.Windows.Forms.TextBox();
            this.UserNameTextBox = new System.Windows.Forms.TextBox();
            this.RememberPasswordCheckBox = new System.Windows.Forms.CheckBox();
            this.InstructuctionsLabel = new System.Windows.Forms.Label();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // CloseButton
            // 
            this.CloseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CloseButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CloseButton.Location = new System.Drawing.Point(329, 4);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(75, 23);
            this.CloseButton.TabIndex = 1;
            this.CloseButton.Text = "Cancel";
            this.CloseButton.UseVisualStyleBackColor = true;
            // 
            // OkButton
            // 
            this.OkButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkButton.Location = new System.Drawing.Point(3, 4);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(75, 23);
            this.OkButton.TabIndex = 0;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            this.OkButton.Click += new System.EventHandler(this.OkButton_Click);
            // 
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkButton);
            this.BottomPN.Controls.Add(this.CloseButton);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 95);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(406, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // MainPN
            // 
            this.MainPN.AutoSize = true;
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.ColumnCount = 2;
            this.MainPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.MainPN.Controls.Add(this.InstructuctionsLabel, 0, 0);
            this.MainPN.Controls.Add(this.RememberPasswordLabel, 0, 4);
            this.MainPN.Controls.Add(this.PasswordLabel, 0, 3);
            this.MainPN.Controls.Add(this.UserNameLabel, 0, 2);
            this.MainPN.Controls.Add(this.PasswordTextBox, 1, 3);
            this.MainPN.Controls.Add(this.UserNameTextBox, 1, 2);
            this.MainPN.Controls.Add(this.RememberPasswordCheckBox, 1, 4);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.RowCount = 6;
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.MainPN.Size = new System.Drawing.Size(406, 95);
            this.MainPN.TabIndex = 1;
            // 
            // RememberPasswordLabel
            // 
            this.RememberPasswordLabel.AutoSize = true;
            this.RememberPasswordLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RememberPasswordLabel.Location = new System.Drawing.Point(3, 72);
            this.RememberPasswordLabel.Name = "RememberPasswordLabel";
            this.RememberPasswordLabel.Size = new System.Drawing.Size(107, 25);
            this.RememberPasswordLabel.TabIndex = 5;
            this.RememberPasswordLabel.Text = "Remember Password";
            this.RememberPasswordLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.RememberPasswordLabel.Visible = false;
            // 
            // PasswordLabel
            // 
            this.PasswordLabel.AutoSize = true;
            this.PasswordLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PasswordLabel.Location = new System.Drawing.Point(3, 48);
            this.PasswordLabel.Name = "PasswordLabel";
            this.PasswordLabel.Size = new System.Drawing.Size(107, 24);
            this.PasswordLabel.TabIndex = 3;
            this.PasswordLabel.Text = "Password";
            this.PasswordLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UserNameLabel
            // 
            this.UserNameLabel.AutoSize = true;
            this.UserNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UserNameLabel.Location = new System.Drawing.Point(3, 24);
            this.UserNameLabel.Name = "UserNameLabel";
            this.UserNameLabel.Size = new System.Drawing.Size(107, 24);
            this.UserNameLabel.TabIndex = 1;
            this.UserNameLabel.Text = "User Name";
            this.UserNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PasswordTextBox
            // 
            this.PasswordTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PasswordTextBox.Location = new System.Drawing.Point(115, 50);
            this.PasswordTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.PasswordTextBox.Name = "PasswordTextBox";
            this.PasswordTextBox.PasswordChar = '●';
            this.PasswordTextBox.Size = new System.Drawing.Size(289, 20);
            this.PasswordTextBox.TabIndex = 4;
            this.PasswordTextBox.Text = "---";
            // 
            // UserNameTextBox
            // 
            this.UserNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UserNameTextBox.Location = new System.Drawing.Point(115, 26);
            this.UserNameTextBox.Margin = new System.Windows.Forms.Padding(2);
            this.UserNameTextBox.Name = "UserNameTextBox";
            this.UserNameTextBox.Size = new System.Drawing.Size(289, 20);
            this.UserNameTextBox.TabIndex = 2;
            // 
            // RememberPasswordCheckBox
            // 
            this.RememberPasswordCheckBox.AutoSize = true;
            this.RememberPasswordCheckBox.Location = new System.Drawing.Point(115, 78);
            this.RememberPasswordCheckBox.Margin = new System.Windows.Forms.Padding(2, 6, 2, 5);
            this.RememberPasswordCheckBox.Name = "RememberPasswordCheckBox";
            this.RememberPasswordCheckBox.Size = new System.Drawing.Size(15, 14);
            this.RememberPasswordCheckBox.TabIndex = 6;
            this.RememberPasswordCheckBox.UseVisualStyleBackColor = true;
            this.RememberPasswordCheckBox.Visible = false;
            // 
            // InstructuctionsLabel
            // 
            this.InstructuctionsLabel.AutoSize = true;
            this.MainPN.SetColumnSpan(this.InstructuctionsLabel, 2);
            this.InstructuctionsLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.InstructuctionsLabel.Location = new System.Drawing.Point(3, 0);
            this.InstructuctionsLabel.Name = "InstructuctionsLabel";
            this.InstructuctionsLabel.Size = new System.Drawing.Size(400, 24);
            this.InstructuctionsLabel.TabIndex = 0;
            this.InstructuctionsLabel.Text = "---";
            this.InstructuctionsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.InstructuctionsLabel.Visible = false;
            // 
            // UserIdentityDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.CloseButton;
            this.ClientSize = new System.Drawing.Size(406, 125);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.MaximizeBox = false;
            this.MaximumSize = new System.Drawing.Size(1000, 166);
            this.MinimumSize = new System.Drawing.Size(417, 100);
            this.Name = "UserIdentityDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "User Identity";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CloseButton;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.TextBox PasswordTextBox;
        private System.Windows.Forms.TableLayoutPanel MainPN;
        private System.Windows.Forms.Label RememberPasswordLabel;
        private System.Windows.Forms.Label PasswordLabel;
        private System.Windows.Forms.Label UserNameLabel;
        private System.Windows.Forms.TextBox UserNameTextBox;
        private System.Windows.Forms.CheckBox RememberPasswordCheckBox;
        private System.Windows.Forms.Label InstructuctionsLabel;
    }
}
