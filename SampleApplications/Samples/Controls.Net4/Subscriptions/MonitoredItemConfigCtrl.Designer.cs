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
    partial class MonitoredItemConfigCtrl
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
            this.PopupMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.NewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.EditMI = new System.Windows.Forms.ToolStripMenuItem();
            this.DeleteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator1 = new System.Windows.Forms.ToolStripSeparator();
            this.SetSamplingIntervalMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetFilterMI = new System.Windows.Forms.ToolStripMenuItem();
            this.SetMonitoringModeMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Separator2 = new System.Windows.Forms.ToolStripSeparator();
            this.MonitorMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PopupMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // ItemsLV
            // 
            this.ItemsLV.ContextMenuStrip = this.PopupMenu;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.NewMI,
            this.EditMI,
            this.DeleteMI,
            this.Separator1,
            this.SetSamplingIntervalMI,
            this.SetFilterMI,
            this.SetMonitoringModeMI,
            this.Separator2,
            this.MonitorMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(197, 192);
            // 
            // NewMI
            // 
            this.NewMI.Name = "NewMI";
            this.NewMI.Size = new System.Drawing.Size(196, 22);
            this.NewMI.Text = "New...";
            this.NewMI.Click += new System.EventHandler(this.NewMI_Click);
            // 
            // EditMI
            // 
            this.EditMI.Name = "EditMI";
            this.EditMI.Size = new System.Drawing.Size(196, 22);
            this.EditMI.Text = "Edit...";
            this.EditMI.Click += new System.EventHandler(this.EditMI_Click);
            // 
            // DeleteMI
            // 
            this.DeleteMI.Name = "DeleteMI";
            this.DeleteMI.Size = new System.Drawing.Size(196, 22);
            this.DeleteMI.Text = "Delete";
            this.DeleteMI.Click += new System.EventHandler(this.DeleteMI_Click);
            // 
            // Separator1
            // 
            this.Separator1.Name = "Separator1";
            this.Separator1.Size = new System.Drawing.Size(193, 6);
            // 
            // SetSamplingIntervalMI
            // 
            this.SetSamplingIntervalMI.Name = "SetSamplingIntervalMI";
            this.SetSamplingIntervalMI.Size = new System.Drawing.Size(196, 22);
            this.SetSamplingIntervalMI.Text = "Set Sampling Interval...";
            this.SetSamplingIntervalMI.Visible = false;
            // 
            // SetFilterMI
            // 
            this.SetFilterMI.Name = "SetFilterMI";
            this.SetFilterMI.Size = new System.Drawing.Size(196, 22);
            this.SetFilterMI.Text = "Set Filter...";
            this.SetFilterMI.Click += new System.EventHandler(this.SetFilterMI_Click);
            // 
            // SetMonitoringModeMI
            // 
            this.SetMonitoringModeMI.Name = "SetMonitoringModeMI";
            this.SetMonitoringModeMI.Size = new System.Drawing.Size(196, 22);
            this.SetMonitoringModeMI.Text = "Set Monitoring Mode...";
            this.SetMonitoringModeMI.Click += new System.EventHandler(this.SetMonitoringModeMI_Click);
            // 
            // Separator2
            // 
            this.Separator2.Name = "Separator2";
            this.Separator2.Size = new System.Drawing.Size(193, 6);
            // 
            // MonitorMI
            // 
            this.MonitorMI.Name = "MonitorMI";
            this.MonitorMI.Size = new System.Drawing.Size(196, 22);
            this.MonitorMI.Text = "Monitor...";
            this.MonitorMI.Click += new System.EventHandler(this.MonitorMI_Click);
            // 
            // MonitoredItemConfigCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.Name = "MonitoredItemConfigCtrl";
            this.Controls.SetChildIndex(this.ItemsLV, 0);
            this.PopupMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem NewMI;
        private System.Windows.Forms.ToolStripMenuItem EditMI;
        private System.Windows.Forms.ToolStripMenuItem SetMonitoringModeMI;
        private System.Windows.Forms.ToolStripMenuItem DeleteMI;
        private System.Windows.Forms.ToolStripSeparator Separator1;
        private System.Windows.Forms.ToolStripMenuItem SetFilterMI;
        private System.Windows.Forms.ToolStripMenuItem SetSamplingIntervalMI;
        private System.Windows.Forms.ToolStripSeparator Separator2;
        private System.Windows.Forms.ToolStripMenuItem MonitorMI;
    }
}
