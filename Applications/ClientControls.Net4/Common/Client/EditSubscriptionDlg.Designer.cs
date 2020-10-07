/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.Controls
{
    partial class EditSubscriptionDlg
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
            this.CancelBTN = new System.Windows.Forms.Button();
            this.OkBTN = new System.Windows.Forms.Button();
            this.BottomPN = new System.Windows.Forms.Panel();
            this.MainPN = new System.Windows.Forms.Panel();
            this.ControlsPN = new System.Windows.Forms.TableLayoutPanel();
            this.PriorityTB = new System.Windows.Forms.NumericUpDown();
            this.PriorityLB = new System.Windows.Forms.Label();
            this.MaxNotificationsPerPublishUP = new System.Windows.Forms.NumericUpDown();
            this.MaxNotificationsPerPublishLB = new System.Windows.Forms.Label();
            this.LifetimeCountLB = new System.Windows.Forms.Label();
            this.LifetimeCountUP = new System.Windows.Forms.NumericUpDown();
            this.PublishingEnabledLB = new System.Windows.Forms.Label();
            this.KeepAliveCountLB = new System.Windows.Forms.Label();
            this.PublishingIntervalLB = new System.Windows.Forms.Label();
            this.PublishingIntervalUP = new System.Windows.Forms.NumericUpDown();
            this.PublishingEnabledCK = new System.Windows.Forms.CheckBox();
            this.KeepAliveCountUP = new System.Windows.Forms.NumericUpDown();
            this.BottomPN.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.ControlsPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PriorityTB)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxNotificationsPerPublishUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.LifetimeCountUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PublishingIntervalUP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.KeepAliveCountUP)).BeginInit();
            this.SuspendLayout();
            // 
            // CancelBTN
            // 
            this.CancelBTN.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.CancelBTN.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelBTN.Location = new System.Drawing.Point(170, 4);
            this.CancelBTN.Name = "CancelBTN";
            this.CancelBTN.Size = new System.Drawing.Size(75, 23);
            this.CancelBTN.TabIndex = 0;
            this.CancelBTN.Text = "Cancel";
            this.CancelBTN.UseVisualStyleBackColor = true;
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
            // BottomPN
            // 
            this.BottomPN.Controls.Add(this.OkBTN);
            this.BottomPN.Controls.Add(this.CancelBTN);
            this.BottomPN.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.BottomPN.Location = new System.Drawing.Point(0, 149);
            this.BottomPN.Name = "BottomPN";
            this.BottomPN.Size = new System.Drawing.Size(248, 30);
            this.BottomPN.TabIndex = 0;
            // 
            // MainPN
            // 
            this.MainPN.AutoSize = true;
            this.MainPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.MainPN.Controls.Add(this.ControlsPN);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 0);
            this.MainPN.Name = "MainPN";
            this.MainPN.Size = new System.Drawing.Size(248, 149);
            this.MainPN.TabIndex = 1;
            // 
            // ControlsPN
            // 
            this.ControlsPN.AutoSize = true;
            this.ControlsPN.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ControlsPN.ColumnCount = 3;
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.ControlsPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.ControlsPN.Controls.Add(this.PriorityTB, 1, 4);
            this.ControlsPN.Controls.Add(this.PriorityLB, 0, 4);
            this.ControlsPN.Controls.Add(this.MaxNotificationsPerPublishUP, 1, 3);
            this.ControlsPN.Controls.Add(this.MaxNotificationsPerPublishLB, 0, 3);
            this.ControlsPN.Controls.Add(this.LifetimeCountLB, 0, 2);
            this.ControlsPN.Controls.Add(this.LifetimeCountUP, 1, 2);
            this.ControlsPN.Controls.Add(this.PublishingEnabledLB, 0, 5);
            this.ControlsPN.Controls.Add(this.KeepAliveCountLB, 0, 1);
            this.ControlsPN.Controls.Add(this.PublishingIntervalLB, 0, 0);
            this.ControlsPN.Controls.Add(this.PublishingIntervalUP, 1, 0);
            this.ControlsPN.Controls.Add(this.PublishingEnabledCK, 1, 5);
            this.ControlsPN.Controls.Add(this.KeepAliveCountUP, 1, 1);
            this.ControlsPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ControlsPN.Location = new System.Drawing.Point(0, 0);
            this.ControlsPN.Name = "ControlsPN";
            this.ControlsPN.RowCount = 7;
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.ControlsPN.Size = new System.Drawing.Size(248, 149);
            this.ControlsPN.TabIndex = 0;
            // 
            // PriorityTB
            // 
            this.PriorityTB.Location = new System.Drawing.Point(106, 107);
            this.PriorityTB.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.PriorityTB.Name = "PriorityTB";
            this.PriorityTB.Size = new System.Drawing.Size(138, 20);
            this.PriorityTB.TabIndex = 9;
            // 
            // PriorityLB
            // 
            this.PriorityLB.AutoSize = true;
            this.PriorityLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PriorityLB.Location = new System.Drawing.Point(3, 104);
            this.PriorityLB.Name = "PriorityLB";
            this.PriorityLB.Size = new System.Drawing.Size(97, 26);
            this.PriorityLB.TabIndex = 8;
            this.PriorityLB.Text = "Priority";
            this.PriorityLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // MaxNotificationsPerPublishUP
            // 
            this.MaxNotificationsPerPublishUP.Location = new System.Drawing.Point(106, 81);
            this.MaxNotificationsPerPublishUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.MaxNotificationsPerPublishUP.Name = "MaxNotificationsPerPublishUP";
            this.MaxNotificationsPerPublishUP.Size = new System.Drawing.Size(138, 20);
            this.MaxNotificationsPerPublishUP.TabIndex = 7;
            // 
            // MaxNotificationsPerPublishLB
            // 
            this.MaxNotificationsPerPublishLB.AutoSize = true;
            this.MaxNotificationsPerPublishLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MaxNotificationsPerPublishLB.Location = new System.Drawing.Point(3, 78);
            this.MaxNotificationsPerPublishLB.Name = "MaxNotificationsPerPublishLB";
            this.MaxNotificationsPerPublishLB.Size = new System.Drawing.Size(97, 26);
            this.MaxNotificationsPerPublishLB.TabIndex = 6;
            this.MaxNotificationsPerPublishLB.Text = "Max Notifications";
            this.MaxNotificationsPerPublishLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // LifetimeCountLB
            // 
            this.LifetimeCountLB.AutoSize = true;
            this.LifetimeCountLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.LifetimeCountLB.Location = new System.Drawing.Point(3, 52);
            this.LifetimeCountLB.Name = "LifetimeCountLB";
            this.LifetimeCountLB.Size = new System.Drawing.Size(97, 26);
            this.LifetimeCountLB.TabIndex = 4;
            this.LifetimeCountLB.Text = "Lifetime Count";
            this.LifetimeCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // LifetimeCountUP
            // 
            this.LifetimeCountUP.Location = new System.Drawing.Point(106, 55);
            this.LifetimeCountUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.LifetimeCountUP.Name = "LifetimeCountUP";
            this.LifetimeCountUP.Size = new System.Drawing.Size(138, 20);
            this.LifetimeCountUP.TabIndex = 5;
            // 
            // PublishingEnabledLB
            // 
            this.PublishingEnabledLB.AutoSize = true;
            this.PublishingEnabledLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PublishingEnabledLB.Location = new System.Drawing.Point(3, 130);
            this.PublishingEnabledLB.Name = "PublishingEnabledLB";
            this.PublishingEnabledLB.Size = new System.Drawing.Size(97, 20);
            this.PublishingEnabledLB.TabIndex = 10;
            this.PublishingEnabledLB.Text = "Publishing Enabled";
            this.PublishingEnabledLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // KeepAliveCountLB
            // 
            this.KeepAliveCountLB.AutoSize = true;
            this.KeepAliveCountLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.KeepAliveCountLB.Location = new System.Drawing.Point(3, 26);
            this.KeepAliveCountLB.Name = "KeepAliveCountLB";
            this.KeepAliveCountLB.Size = new System.Drawing.Size(97, 26);
            this.KeepAliveCountLB.TabIndex = 2;
            this.KeepAliveCountLB.Text = "Keep Alive Count";
            this.KeepAliveCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PublishingIntervalLB
            // 
            this.PublishingIntervalLB.AutoSize = true;
            this.PublishingIntervalLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PublishingIntervalLB.Location = new System.Drawing.Point(3, 0);
            this.PublishingIntervalLB.Name = "PublishingIntervalLB";
            this.PublishingIntervalLB.Size = new System.Drawing.Size(97, 26);
            this.PublishingIntervalLB.TabIndex = 0;
            this.PublishingIntervalLB.Text = "Publishing Interval";
            this.PublishingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PublishingIntervalUP
            // 
            this.PublishingIntervalUP.Increment = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.PublishingIntervalUP.Location = new System.Drawing.Point(106, 3);
            this.PublishingIntervalUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.PublishingIntervalUP.Name = "PublishingIntervalUP";
            this.PublishingIntervalUP.Size = new System.Drawing.Size(138, 20);
            this.PublishingIntervalUP.TabIndex = 1;
            // 
            // PublishingEnabledCK
            // 
            this.PublishingEnabledCK.AutoSize = true;
            this.PublishingEnabledCK.Checked = true;
            this.PublishingEnabledCK.CheckState = System.Windows.Forms.CheckState.Checked;
            this.PublishingEnabledCK.Location = new System.Drawing.Point(106, 133);
            this.PublishingEnabledCK.Name = "PublishingEnabledCK";
            this.PublishingEnabledCK.Size = new System.Drawing.Size(15, 14);
            this.PublishingEnabledCK.TabIndex = 11;
            this.PublishingEnabledCK.UseVisualStyleBackColor = true;
            // 
            // KeepAliveCountUP
            // 
            this.KeepAliveCountUP.Location = new System.Drawing.Point(106, 29);
            this.KeepAliveCountUP.Maximum = new decimal(new int[] {
            0,
            1,
            0,
            0});
            this.KeepAliveCountUP.Name = "KeepAliveCountUP";
            this.KeepAliveCountUP.Size = new System.Drawing.Size(138, 20);
            this.KeepAliveCountUP.TabIndex = 3;
            // 
            // EditSubscriptionDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.CancelButton = this.CancelBTN;
            this.ClientSize = new System.Drawing.Size(248, 179);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.BottomPN);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditSubscriptionDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Subscription";
            this.BottomPN.ResumeLayout(false);
            this.MainPN.ResumeLayout(false);
            this.MainPN.PerformLayout();
            this.ControlsPN.ResumeLayout(false);
            this.ControlsPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PriorityTB)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.MaxNotificationsPerPublishUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.LifetimeCountUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PublishingIntervalUP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.KeepAliveCountUP)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button CancelBTN;
        private System.Windows.Forms.Button OkBTN;
        private System.Windows.Forms.Panel BottomPN;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.TableLayoutPanel ControlsPN;
        private System.Windows.Forms.Label PublishingEnabledLB;
        private System.Windows.Forms.Label KeepAliveCountLB;
        private System.Windows.Forms.Label PublishingIntervalLB;
        private System.Windows.Forms.NumericUpDown KeepAliveCountUP;
        private System.Windows.Forms.NumericUpDown PublishingIntervalUP;
        private System.Windows.Forms.CheckBox PublishingEnabledCK;
        private System.Windows.Forms.Label LifetimeCountLB;
        private System.Windows.Forms.NumericUpDown LifetimeCountUP;
        private System.Windows.Forms.NumericUpDown MaxNotificationsPerPublishUP;
        private System.Windows.Forms.Label MaxNotificationsPerPublishLB;
        private System.Windows.Forms.NumericUpDown PriorityTB;
        private System.Windows.Forms.Label PriorityLB;
    }
}
