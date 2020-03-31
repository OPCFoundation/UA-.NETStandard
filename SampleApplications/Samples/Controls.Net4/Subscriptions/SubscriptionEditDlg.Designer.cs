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

namespace Opc.Ua.Sample.Controls
{
    partial class SubscriptionEditDlg
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
            this.ButtonsPN = new System.Windows.Forms.Panel();
            this.OkBTN = new System.Windows.Forms.Button();
            this.CancelBTN = new System.Windows.Forms.Button();
            this.MainPN = new System.Windows.Forms.Panel();
            this.MaxNotificationsCTRL = new System.Windows.Forms.NumericUpDown();
            this.MaxNotificationsLB = new System.Windows.Forms.Label();
            this.LifetimeCountCTRL = new System.Windows.Forms.NumericUpDown();
            this.LifetimeCountLB = new System.Windows.Forms.Label();
            this.DisplayNameTB = new System.Windows.Forms.TextBox();
            this.KeepAliveCountNC = new System.Windows.Forms.NumericUpDown();
            this.PublishingIntervalNC = new System.Windows.Forms.NumericUpDown();
            this.PriorityNC = new System.Windows.Forms.NumericUpDown();
            this.PriorityLB = new System.Windows.Forms.Label();
            this.PublishingEnabledCK = new System.Windows.Forms.CheckBox();
            this.PublishingIntervalLB = new System.Windows.Forms.Label();
            this.PublishingEnabledLB = new System.Windows.Forms.Label();
            this.KeepAliveCountLB = new System.Windows.Forms.Label();
            this.DisplayNameLB = new System.Windows.Forms.Label();
            this.ButtonsPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxNotificationsCTRL)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.LifetimeCountCTRL)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.KeepAliveCountNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PublishingIntervalNC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PriorityNC)).BeginInit();
            this.SuspendLayout();
            // 
            // ButtonsPN
            // 
            this.ButtonsPN.Controls.Add(this.OkBTN);
            this.ButtonsPN.Controls.Add(this.CancelBTN);
            this.ButtonsPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ButtonsPN.Location = new System.Drawing.Point(0, 176);
            this.ButtonsPN.Name = "ButtonsPN";
            this.ButtonsPN.Size = new System.Drawing.Size(345, 31);
            this.ButtonsPN.TabIndex = 1;
            // 
            // OkBTN
            // 
            this.OkBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.OkBTN.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkBTN.Location = new System.Drawing.Point(4, 4);
            this.OkBTN.Name = "OkBTN";
            this.OkBTN.Size = new System.Drawing.Size(75, 23);
            this.OkBTN.TabIndex = 1;
            this.OkBTN.Text = "OK";
            this.OkBTN.UseVisualStyleBackColor = true;
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(266, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.MaxNotificationsCTRL);
            this.MainPN.Controls.Add(this.MaxNotificationsLB);
            this.MainPN.Controls.Add(this.LifetimeCountCTRL);
            this.MainPN.Controls.Add(this.LifetimeCountLB);
            this.MainPN.Controls.Add(this.DisplayNameTB);
            this.MainPN.Controls.Add(this.KeepAliveCountNC);
            this.MainPN.Controls.Add(this.PublishingIntervalNC);
            this.MainPN.Controls.Add(this.PriorityNC);
            this.MainPN.Controls.Add(this.PriorityLB);
            this.MainPN.Controls.Add(this.PublishingEnabledCK);
            this.MainPN.Controls.Add(this.PublishingIntervalLB);
            this.MainPN.Controls.Add(this.PublishingEnabledLB);
            this.MainPN.Controls.Add(this.KeepAliveCountLB);
            this.MainPN.Controls.Add(this.DisplayNameLB);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(345, 176);
            this.MainPN.TabIndex = 0;
            // 
            // MaxNotificationsCTRL
            // 
            this.MaxNotificationsCTRL.Location = new System.Drawing.Point(153, 108);
            this.MaxNotificationsCTRL.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.MaxNotificationsCTRL.Name = "MaxNotificationsCTRL";
            this.MaxNotificationsCTRL.Size = new System.Drawing.Size(88, 20);
            this.MaxNotificationsCTRL.TabIndex = 13;
            this.MaxNotificationsCTRL.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // MaxNotificationsLB
            // 
            this.MaxNotificationsLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.MaxNotificationsLB.AutoSize = true;
            this.MaxNotificationsLB.Location = new System.Drawing.Point(3, 110);
            this.MaxNotificationsLB.Name = "MaxNotificationsLB";
            this.MaxNotificationsLB.Size = new System.Drawing.Size(144, 13);
            this.MaxNotificationsLB.TabIndex = 12;
            this.MaxNotificationsLB.Text = "Max Notifications Per Publish";
            this.MaxNotificationsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // LifetimeCountCTRL
            // 
            this.LifetimeCountCTRL.Location = new System.Drawing.Point(153, 82);
            this.LifetimeCountCTRL.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.LifetimeCountCTRL.Name = "LifetimeCountCTRL";
            this.LifetimeCountCTRL.Size = new System.Drawing.Size(88, 20);
            this.LifetimeCountCTRL.TabIndex = 11;
            this.LifetimeCountCTRL.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // LifetimeCountLB
            // 
            this.LifetimeCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.LifetimeCountLB.AutoSize = true;
            this.LifetimeCountLB.Location = new System.Drawing.Point(3, 84);
            this.LifetimeCountLB.Name = "LifetimeCountLB";
            this.LifetimeCountLB.Size = new System.Drawing.Size(74, 13);
            this.LifetimeCountLB.TabIndex = 10;
            this.LifetimeCountLB.Text = "Lifetime Count";
            this.LifetimeCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameTB
            // 
            this.DisplayNameTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameTB.Location = new System.Drawing.Point(153, 4);
            this.DisplayNameTB.Name = "DisplayNameTB";
            this.DisplayNameTB.Size = new System.Drawing.Size(188, 20);
            this.DisplayNameTB.TabIndex = 1;
            // 
            // KeepAliveCountNC
            // 
            this.KeepAliveCountNC.Location = new System.Drawing.Point(153, 56);
            this.KeepAliveCountNC.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.KeepAliveCountNC.Name = "KeepAliveCountNC";
            this.KeepAliveCountNC.Size = new System.Drawing.Size(88, 20);
            this.KeepAliveCountNC.TabIndex = 5;
            this.KeepAliveCountNC.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // PublishingIntervalNC
            // 
            this.PublishingIntervalNC.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.PublishingIntervalNC.Location = new System.Drawing.Point(153, 30);
            this.PublishingIntervalNC.Maximum = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            this.PublishingIntervalNC.Name = "PublishingIntervalNC";
            this.PublishingIntervalNC.Size = new System.Drawing.Size(88, 20);
            this.PublishingIntervalNC.TabIndex = 3;
            this.PublishingIntervalNC.Value = new decimal(new int[] {
            1000000000,
            0,
            0,
            0});
            // 
            // PriorityNC
            // 
            this.PriorityNC.Location = new System.Drawing.Point(153, 134);
            this.PriorityNC.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.PriorityNC.Name = "PriorityNC";
            this.PriorityNC.Size = new System.Drawing.Size(88, 20);
            this.PriorityNC.TabIndex = 7;
            this.PriorityNC.Value = new decimal(new int[] {
            255,
            0,
            0,
            0});
            // 
            // PriorityLB
            // 
            this.PriorityLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PriorityLB.AutoSize = true;
            this.PriorityLB.Location = new System.Drawing.Point(4, 136);
            this.PriorityLB.Name = "PriorityLB";
            this.PriorityLB.Size = new System.Drawing.Size(38, 13);
            this.PriorityLB.TabIndex = 6;
            this.PriorityLB.Text = "Priority";
            this.PriorityLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PublishingEnabledCK
            // 
            this.PublishingEnabledCK.AutoSize = true;
            this.PublishingEnabledCK.Location = new System.Drawing.Point(153, 160);
            this.PublishingEnabledCK.Name = "PublishingEnabledCK";
            this.PublishingEnabledCK.Size = new System.Drawing.Size(15, 14);
            this.PublishingEnabledCK.TabIndex = 9;
            this.PublishingEnabledCK.UseVisualStyleBackColor = true;
            // 
            // PublishingIntervalLB
            // 
            this.PublishingIntervalLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PublishingIntervalLB.AutoSize = true;
            this.PublishingIntervalLB.Location = new System.Drawing.Point(4, 32);
            this.PublishingIntervalLB.Name = "PublishingIntervalLB";
            this.PublishingIntervalLB.Size = new System.Drawing.Size(93, 13);
            this.PublishingIntervalLB.TabIndex = 2;
            this.PublishingIntervalLB.Text = "Publishing Interval";
            this.PublishingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PublishingEnabledLB
            // 
            this.PublishingEnabledLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.PublishingEnabledLB.AutoSize = true;
            this.PublishingEnabledLB.Location = new System.Drawing.Point(4, 160);
            this.PublishingEnabledLB.Name = "PublishingEnabledLB";
            this.PublishingEnabledLB.Size = new System.Drawing.Size(97, 13);
            this.PublishingEnabledLB.TabIndex = 8;
            this.PublishingEnabledLB.Text = "Publishing Enabled";
            this.PublishingEnabledLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // KeepAliveCountLB
            // 
            this.KeepAliveCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.KeepAliveCountLB.AutoSize = true;
            this.KeepAliveCountLB.Location = new System.Drawing.Point(3, 58);
            this.KeepAliveCountLB.Name = "KeepAliveCountLB";
            this.KeepAliveCountLB.Size = new System.Drawing.Size(89, 13);
            this.KeepAliveCountLB.TabIndex = 4;
            this.KeepAliveCountLB.Text = "Keep Alive Count";
            this.KeepAliveCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DisplayNameLB
            // 
            this.DisplayNameLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.DisplayNameLB.AutoSize = true;
            this.DisplayNameLB.Location = new System.Drawing.Point(4, 8);
            this.DisplayNameLB.Name = "DisplayNameLB";
            this.DisplayNameLB.Size = new System.Drawing.Size(72, 13);
            this.DisplayNameLB.TabIndex = 0;
            this.DisplayNameLB.Text = "Display Name";
            this.DisplayNameLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SubscriptionEditDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(345, 207);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.ButtonsPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "SubscriptionEditDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Subscription Parameters";
            this.ButtonsPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MaxNotificationsCTRL)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.LifetimeCountCTRL)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.KeepAliveCountNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PublishingIntervalNC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PriorityNC)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel ButtonsPN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.Label DisplayNameLB;
        private System.Windows.Forms.Label PublishingIntervalLB;
        private System.Windows.Forms.Label PublishingEnabledLB;
        private System.Windows.Forms.Label KeepAliveCountLB;
        private System.Windows.Forms.CheckBox PublishingEnabledCK;
        private System.Windows.Forms.Label PriorityLB;
        private System.Windows.Forms.TextBox DisplayNameTB;
        private System.Windows.Forms.NumericUpDown KeepAliveCountNC;
        private System.Windows.Forms.NumericUpDown PublishingIntervalNC;
        private System.Windows.Forms.NumericUpDown PriorityNC;
        private System.Windows.Forms.NumericUpDown MaxNotificationsCTRL;
        private System.Windows.Forms.Label MaxNotificationsLB;
        private System.Windows.Forms.NumericUpDown LifetimeCountCTRL;
        private System.Windows.Forms.Label LifetimeCountLB;
    }
}
