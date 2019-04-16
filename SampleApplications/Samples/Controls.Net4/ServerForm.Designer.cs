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
    partial class ServerForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ServerForm));
            this.TrayIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.ShowMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator01 = new System.Windows.Forms.ToolStripSeparator();
            this.ExitMI = new System.Windows.Forms.ToolStripMenuItem();
            this.ServerStateLB = new System.Windows.Forms.Label();
            this.CumulatedSessionCountLB = new System.Windows.Forms.Label();
            this.CurrentSessionCountLB = new System.Windows.Forms.Label();
            this.StartTimeTB = new System.Windows.Forms.TextBox();
            this.CurrentTimeLB = new System.Windows.Forms.Label();
            this.StartTimeLB = new System.Windows.Forms.Label();
            this.RejectedSessionCountLB = new System.Windows.Forms.Label();
            this.SessionTimeoutCountLB = new System.Windows.Forms.Label();
            this.CurrentSubscriptionCountLB = new System.Windows.Forms.Label();
            this.CumulatedSubscriptionCountLB = new System.Windows.Forms.Label();
            this.CurrentTimeTB = new System.Windows.Forms.TextBox();
            this.ServerStateTB = new System.Windows.Forms.TextBox();
            this.CurrentSessionCountTB = new System.Windows.Forms.TextBox();
            this.CumulatedSessionCountTB = new System.Windows.Forms.TextBox();
            this.RejectedSessionCountTB = new System.Windows.Forms.TextBox();
            this.SessionTimeoutCountTB = new System.Windows.Forms.TextBox();
            this.CurrentSubscriptionCountTB = new System.Windows.Forms.TextBox();
            this.CumulatedSubscriptionCountTB = new System.Windows.Forms.TextBox();
            this.Timer = new System.Windows.Forms.Timer(this.components);
            this.CurrentStatusGB = new System.Windows.Forms.GroupBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.EndpointsTB = new System.Windows.Forms.TextBox();
            this.PopupMenu.SuspendLayout();
            this.CurrentStatusGB.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // TrayIcon
            // 
            this.TrayIcon.ContextMenuStrip = this.PopupMenu;
            this.TrayIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("TrayIcon.Icon")));
            this.TrayIcon.Text = "UA Server";
            this.TrayIcon.Visible = true;
            this.TrayIcon.DoubleClick += new System.EventHandler(this.TrayIcon_DoubleClick);
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ShowMI,
            this.Separator01,
            this.ExitMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(104, 54);
            // 
            // ShowMI
            // 
            this.ShowMI.Name = "ShowMI";
            this.ShowMI.Size = new System.Drawing.Size(103, 22);
            this.ShowMI.Text = "Show";
            this.ShowMI.Click += new System.EventHandler(this.ShowMI_Click);
            // 
            // Separator01
            // 
            this.Separator01.Name = "Separator01";
            this.Separator01.Size = new System.Drawing.Size(100, 6);
            // 
            // ExitMI
            // 
            this.ExitMI.Name = "ExitMI";
            this.ExitMI.Size = new System.Drawing.Size(103, 22);
            this.ExitMI.Text = "Exit";
            this.ExitMI.Click += new System.EventHandler(this.ExitMI_Click);
            // 
            // ServerStateLB
            // 
            this.ServerStateLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerStateLB.AutoSize = true;
            this.ServerStateLB.Location = new System.Drawing.Point(7, 64);
            this.ServerStateLB.Name = "ServerStateLB";
            this.ServerStateLB.Size = new System.Drawing.Size(66, 13);
            this.ServerStateLB.TabIndex = 4;
            this.ServerStateLB.Text = "Server State";
            this.ServerStateLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CumulatedSessionCountLB
            // 
            this.CumulatedSessionCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CumulatedSessionCountLB.AutoSize = true;
            this.CumulatedSessionCountLB.Location = new System.Drawing.Point(7, 112);
            this.CumulatedSessionCountLB.Name = "CumulatedSessionCountLB";
            this.CumulatedSessionCountLB.Size = new System.Drawing.Size(130, 13);
            this.CumulatedSessionCountLB.TabIndex = 8;
            this.CumulatedSessionCountLB.Text = "Cumulative Session Count";
            this.CumulatedSessionCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CurrentSessionCountLB
            // 
            this.CurrentSessionCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentSessionCountLB.AutoSize = true;
            this.CurrentSessionCountLB.Location = new System.Drawing.Point(7, 88);
            this.CurrentSessionCountLB.Name = "CurrentSessionCountLB";
            this.CurrentSessionCountLB.Size = new System.Drawing.Size(112, 13);
            this.CurrentSessionCountLB.TabIndex = 6;
            this.CurrentSessionCountLB.Text = "Current Session Count";
            this.CurrentSessionCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeTB
            // 
            this.StartTimeTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartTimeTB.BackColor = System.Drawing.SystemColors.Info;
            this.StartTimeTB.Location = new System.Drawing.Point(163, 12);
            this.StartTimeTB.Name = "StartTimeTB";
            this.StartTimeTB.ReadOnly = true;
            this.StartTimeTB.Size = new System.Drawing.Size(125, 20);
            this.StartTimeTB.TabIndex = 1;
            // 
            // CurrentTimeLB
            // 
            this.CurrentTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentTimeLB.AutoSize = true;
            this.CurrentTimeLB.Location = new System.Drawing.Point(7, 40);
            this.CurrentTimeLB.Name = "CurrentTimeLB";
            this.CurrentTimeLB.Size = new System.Drawing.Size(67, 13);
            this.CurrentTimeLB.TabIndex = 2;
            this.CurrentTimeLB.Text = "Current Time";
            this.CurrentTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // StartTimeLB
            // 
            this.StartTimeLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.StartTimeLB.AutoSize = true;
            this.StartTimeLB.Location = new System.Drawing.Point(7, 16);
            this.StartTimeLB.Name = "StartTimeLB";
            this.StartTimeLB.Size = new System.Drawing.Size(55, 13);
            this.StartTimeLB.TabIndex = 0;
            this.StartTimeLB.Text = "Start Time";
            this.StartTimeLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // RejectedSessionCountLB
            // 
            this.RejectedSessionCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RejectedSessionCountLB.AutoSize = true;
            this.RejectedSessionCountLB.Location = new System.Drawing.Point(7, 136);
            this.RejectedSessionCountLB.Name = "RejectedSessionCountLB";
            this.RejectedSessionCountLB.Size = new System.Drawing.Size(121, 13);
            this.RejectedSessionCountLB.TabIndex = 10;
            this.RejectedSessionCountLB.Text = "Rejected Session Count";
            this.RejectedSessionCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SessionTimeoutCountLB
            // 
            this.SessionTimeoutCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SessionTimeoutCountLB.AutoSize = true;
            this.SessionTimeoutCountLB.Location = new System.Drawing.Point(7, 160);
            this.SessionTimeoutCountLB.Name = "SessionTimeoutCountLB";
            this.SessionTimeoutCountLB.Size = new System.Drawing.Size(116, 13);
            this.SessionTimeoutCountLB.TabIndex = 12;
            this.SessionTimeoutCountLB.Text = "Session Timeout Count";
            this.SessionTimeoutCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CurrentSubscriptionCountLB
            // 
            this.CurrentSubscriptionCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentSubscriptionCountLB.AutoSize = true;
            this.CurrentSubscriptionCountLB.Location = new System.Drawing.Point(7, 184);
            this.CurrentSubscriptionCountLB.Name = "CurrentSubscriptionCountLB";
            this.CurrentSubscriptionCountLB.Size = new System.Drawing.Size(136, 13);
            this.CurrentSubscriptionCountLB.TabIndex = 14;
            this.CurrentSubscriptionCountLB.Text = "Current Subscription  Count";
            this.CurrentSubscriptionCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CumulatedSubscriptionCountLB
            // 
            this.CumulatedSubscriptionCountLB.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CumulatedSubscriptionCountLB.AutoSize = true;
            this.CumulatedSubscriptionCountLB.Location = new System.Drawing.Point(7, 208);
            this.CumulatedSubscriptionCountLB.Name = "CumulatedSubscriptionCountLB";
            this.CumulatedSubscriptionCountLB.Size = new System.Drawing.Size(154, 13);
            this.CumulatedSubscriptionCountLB.TabIndex = 16;
            this.CumulatedSubscriptionCountLB.Text = "Cumulative Subscription  Count";
            this.CumulatedSubscriptionCountLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CurrentTimeTB
            // 
            this.CurrentTimeTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentTimeTB.BackColor = System.Drawing.SystemColors.Info;
            this.CurrentTimeTB.Location = new System.Drawing.Point(163, 36);
            this.CurrentTimeTB.Name = "CurrentTimeTB";
            this.CurrentTimeTB.ReadOnly = true;
            this.CurrentTimeTB.Size = new System.Drawing.Size(125, 20);
            this.CurrentTimeTB.TabIndex = 3;
            // 
            // ServerStateTB
            // 
            this.ServerStateTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ServerStateTB.BackColor = System.Drawing.SystemColors.Info;
            this.ServerStateTB.Location = new System.Drawing.Point(163, 60);
            this.ServerStateTB.Name = "ServerStateTB";
            this.ServerStateTB.ReadOnly = true;
            this.ServerStateTB.Size = new System.Drawing.Size(125, 20);
            this.ServerStateTB.TabIndex = 5;
            // 
            // CurrentSessionCountTB
            // 
            this.CurrentSessionCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentSessionCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.CurrentSessionCountTB.Location = new System.Drawing.Point(163, 84);
            this.CurrentSessionCountTB.Name = "CurrentSessionCountTB";
            this.CurrentSessionCountTB.ReadOnly = true;
            this.CurrentSessionCountTB.Size = new System.Drawing.Size(125, 20);
            this.CurrentSessionCountTB.TabIndex = 7;
            // 
            // CumulatedSessionCountTB
            // 
            this.CumulatedSessionCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CumulatedSessionCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.CumulatedSessionCountTB.Location = new System.Drawing.Point(163, 108);
            this.CumulatedSessionCountTB.Name = "CumulatedSessionCountTB";
            this.CumulatedSessionCountTB.ReadOnly = true;
            this.CumulatedSessionCountTB.Size = new System.Drawing.Size(125, 20);
            this.CumulatedSessionCountTB.TabIndex = 9;
            // 
            // RejectedSessionCountTB
            // 
            this.RejectedSessionCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.RejectedSessionCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.RejectedSessionCountTB.Location = new System.Drawing.Point(163, 132);
            this.RejectedSessionCountTB.Name = "RejectedSessionCountTB";
            this.RejectedSessionCountTB.ReadOnly = true;
            this.RejectedSessionCountTB.Size = new System.Drawing.Size(125, 20);
            this.RejectedSessionCountTB.TabIndex = 11;
            // 
            // SessionTimeoutCountTB
            // 
            this.SessionTimeoutCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.SessionTimeoutCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.SessionTimeoutCountTB.Location = new System.Drawing.Point(163, 156);
            this.SessionTimeoutCountTB.Name = "SessionTimeoutCountTB";
            this.SessionTimeoutCountTB.ReadOnly = true;
            this.SessionTimeoutCountTB.Size = new System.Drawing.Size(125, 20);
            this.SessionTimeoutCountTB.TabIndex = 13;
            // 
            // CurrentSubscriptionCountTB
            // 
            this.CurrentSubscriptionCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CurrentSubscriptionCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.CurrentSubscriptionCountTB.Location = new System.Drawing.Point(163, 180);
            this.CurrentSubscriptionCountTB.Name = "CurrentSubscriptionCountTB";
            this.CurrentSubscriptionCountTB.ReadOnly = true;
            this.CurrentSubscriptionCountTB.Size = new System.Drawing.Size(125, 20);
            this.CurrentSubscriptionCountTB.TabIndex = 15;
            // 
            // CumulatedSubscriptionCountTB
            // 
            this.CumulatedSubscriptionCountTB.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CumulatedSubscriptionCountTB.BackColor = System.Drawing.SystemColors.Info;
            this.CumulatedSubscriptionCountTB.Location = new System.Drawing.Point(163, 204);
            this.CumulatedSubscriptionCountTB.Name = "CumulatedSubscriptionCountTB";
            this.CumulatedSubscriptionCountTB.ReadOnly = true;
            this.CumulatedSubscriptionCountTB.Size = new System.Drawing.Size(125, 20);
            this.CumulatedSubscriptionCountTB.TabIndex = 17;
            // 
            // Timer
            // 
            this.Timer.Interval = 1000;
            this.Timer.Tick += new System.EventHandler(this.Timer_Tick);
            // 
            // CurrentStatusGB
            // 
            this.CurrentStatusGB.Controls.Add(this.ServerStateTB);
            this.CurrentStatusGB.Controls.Add(this.StartTimeLB);
            this.CurrentStatusGB.Controls.Add(this.CumulatedSubscriptionCountTB);
            this.CurrentStatusGB.Controls.Add(this.CurrentTimeLB);
            this.CurrentStatusGB.Controls.Add(this.CurrentSubscriptionCountTB);
            this.CurrentStatusGB.Controls.Add(this.StartTimeTB);
            this.CurrentStatusGB.Controls.Add(this.SessionTimeoutCountTB);
            this.CurrentStatusGB.Controls.Add(this.CurrentSessionCountLB);
            this.CurrentStatusGB.Controls.Add(this.RejectedSessionCountTB);
            this.CurrentStatusGB.Controls.Add(this.CumulatedSessionCountLB);
            this.CurrentStatusGB.Controls.Add(this.CumulatedSessionCountTB);
            this.CurrentStatusGB.Controls.Add(this.ServerStateLB);
            this.CurrentStatusGB.Controls.Add(this.CurrentSessionCountTB);
            this.CurrentStatusGB.Controls.Add(this.RejectedSessionCountLB);
            this.CurrentStatusGB.Controls.Add(this.SessionTimeoutCountLB);
            this.CurrentStatusGB.Controls.Add(this.CurrentTimeTB);
            this.CurrentStatusGB.Controls.Add(this.CurrentSubscriptionCountLB);
            this.CurrentStatusGB.Controls.Add(this.CumulatedSubscriptionCountLB);
            this.CurrentStatusGB.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.CurrentStatusGB.ForeColor = System.Drawing.SystemColors.ControlText;
            this.CurrentStatusGB.Location = new System.Drawing.Point(0, 73);
            this.CurrentStatusGB.Name = "CurrentStatusGB";
            this.CurrentStatusGB.Size = new System.Drawing.Size(292, 228);
            this.CurrentStatusGB.TabIndex = 18;
            this.CurrentStatusGB.TabStop = false;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.EndpointsTB);
            this.groupBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.groupBox1.ForeColor = System.Drawing.SystemColors.ControlText;
            this.groupBox1.Location = new System.Drawing.Point(0, 0);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(292, 73);
            this.groupBox1.TabIndex = 19;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Endpoints";
            // 
            // EndpointsTB
            // 
            this.EndpointsTB.BackColor = System.Drawing.SystemColors.Info;
            this.EndpointsTB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EndpointsTB.Location = new System.Drawing.Point(3, 16);
            this.EndpointsTB.Multiline = true;
            this.EndpointsTB.Name = "EndpointsTB";
            this.EndpointsTB.ReadOnly = true;
            this.EndpointsTB.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.EndpointsTB.Size = new System.Drawing.Size(286, 54);
            this.EndpointsTB.TabIndex = 0;
            this.EndpointsTB.WordWrap = false;
            // 
            // ServerForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(292, 301);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.CurrentStatusGB);
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(300, 300);
            this.Name = "ServerForm";
            this.ShowInTaskbar = false;
            this.Text = "UA Sample Server";
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.PopupMenu.ResumeLayout(false);
            this.CurrentStatusGB.ResumeLayout(false);
            this.CurrentStatusGB.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.NotifyIcon TrayIcon;
        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem ExitMI;
        private System.Windows.Forms.Label ServerStateLB;
        private System.Windows.Forms.Label CumulatedSessionCountLB;
        private System.Windows.Forms.Label CurrentSessionCountLB;
        private System.Windows.Forms.TextBox StartTimeTB;
        private System.Windows.Forms.Label CurrentTimeLB;
        private System.Windows.Forms.Label StartTimeLB;
        private System.Windows.Forms.Label RejectedSessionCountLB;
        private System.Windows.Forms.Label SessionTimeoutCountLB;
        private System.Windows.Forms.Label CurrentSubscriptionCountLB;
        private System.Windows.Forms.Label CumulatedSubscriptionCountLB;
        private System.Windows.Forms.TextBox CurrentTimeTB;
        private System.Windows.Forms.TextBox ServerStateTB;
        private System.Windows.Forms.TextBox CurrentSessionCountTB;
        private System.Windows.Forms.TextBox CumulatedSessionCountTB;
        private System.Windows.Forms.TextBox RejectedSessionCountTB;
        private System.Windows.Forms.TextBox SessionTimeoutCountTB;
        private System.Windows.Forms.TextBox CurrentSubscriptionCountTB;
        private System.Windows.Forms.TextBox CumulatedSubscriptionCountTB;
        private System.Windows.Forms.ToolStripMenuItem ShowMI;
        private System.Windows.Forms.ToolStripSeparator Separator01;
        private System.Windows.Forms.Timer Timer;
        private System.Windows.Forms.GroupBox CurrentStatusGB;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox EndpointsTB;
    }
}
