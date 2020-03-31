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

namespace Quickstarts.AlarmConditionClient
{
    partial class AuditEventForm
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
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.FormMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Form_CloseMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MainPN = new System.Windows.Forms.Panel();
            this.EventsLV = new System.Windows.Forms.ListView();
            this.SourceCH = new System.Windows.Forms.ColumnHeader();
            this.EventTypeCH = new System.Windows.Forms.ColumnHeader();
            this.MethodCH = new System.Windows.Forms.ColumnHeader();
            this.TimeCH = new System.Windows.Forms.ColumnHeader();
            this.StatusCH = new System.Windows.Forms.ColumnHeader();
            this.MessageCH = new System.Windows.Forms.ColumnHeader();
            this.ArgumentsCH = new System.Windows.Forms.ColumnHeader();
            this.EventsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Events_ViewMI = new System.Windows.Forms.ToolStripMenuItem();
            this.Events_ClearMI = new System.Windows.Forms.ToolStripMenuItem();
            this.MenuBar.SuspendLayout();
            this.MainPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FormMI,
            this.EventsMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(884, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // FormMI
            // 
            this.FormMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Form_CloseMI});
            this.FormMI.Name = "FormMI";
            this.FormMI.Size = new System.Drawing.Size(47, 20);
            this.FormMI.Text = "Form";
            // 
            // Form_CloseMI
            // 
            this.Form_CloseMI.Name = "Form_CloseMI";
            this.Form_CloseMI.Size = new System.Drawing.Size(152, 22);
            this.Form_CloseMI.Text = "Close";
            // 
            // MainPN
            // 
            this.MainPN.Controls.Add(this.EventsLV);
            this.MainPN.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MainPN.Location = new System.Drawing.Point(0, 24);
            this.MainPN.Name = "MainPN";
            this.MainPN.Padding = new System.Windows.Forms.Padding(2, 2, 2, 0);
            this.MainPN.Size = new System.Drawing.Size(884, 522);
            this.MainPN.TabIndex = 3;
            // 
            // EventsLV
            // 
            this.EventsLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.SourceCH,
            this.EventTypeCH,
            this.MethodCH,
            this.StatusCH,
            this.TimeCH,
            this.MessageCH,
            this.ArgumentsCH});
            this.EventsLV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.EventsLV.FullRowSelect = true;
            this.EventsLV.Location = new System.Drawing.Point(2, 2);
            this.EventsLV.Name = "EventsLV";
            this.EventsLV.Size = new System.Drawing.Size(880, 520);
            this.EventsLV.TabIndex = 0;
            this.EventsLV.UseCompatibleStateImageBehavior = false;
            this.EventsLV.View = System.Windows.Forms.View.Details;
            // 
            // SourceCH
            // 
            this.SourceCH.Text = "Source";
            // 
            // EventTypeCH
            // 
            this.EventTypeCH.Text = "Type";
            // 
            // MethodCH
            // 
            this.MethodCH.Text = "Method";
            this.MethodCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // TimeCH
            // 
            this.TimeCH.Text = "Time";
            this.TimeCH.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // StatusCH
            // 
            this.StatusCH.Text = "Status";
            // 
            // MessageCH
            // 
            this.MessageCH.Text = "Message";
            // 
            // ArgumentsCH
            // 
            this.ArgumentsCH.Text = "Arguments";
            this.ArgumentsCH.Width = 72;
            // 
            // EventsMI
            // 
            this.EventsMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.Events_ViewMI,
            this.Events_ClearMI});
            this.EventsMI.Name = "EventsMI";
            this.EventsMI.Size = new System.Drawing.Size(53, 20);
            this.EventsMI.Text = "Events";
            // 
            // Events_ViewMI
            // 
            this.Events_ViewMI.Name = "Events_ViewMI";
            this.Events_ViewMI.Size = new System.Drawing.Size(152, 22);
            this.Events_ViewMI.Text = "View...";
            this.Events_ViewMI.Click += new System.EventHandler(this.Events_ViewMI_Click);
            // 
            // Events_ClearMI
            // 
            this.Events_ClearMI.Name = "Events_ClearMI";
            this.Events_ClearMI.Size = new System.Drawing.Size(152, 22);
            this.Events_ClearMI.Text = "Clear";
            this.Events_ClearMI.Click += new System.EventHandler(this.Events_ClearMI_Click);
            // 
            // AuditEventForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(884, 546);
            this.Controls.Add(this.MainPN);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "AuditEventForm";
            this.Text = "Audit Events";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AuditEventForm_FormClosing);
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.MainPN.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.ToolStripMenuItem FormMI;
        private System.Windows.Forms.ToolStripMenuItem Form_CloseMI;
        private System.Windows.Forms.Panel MainPN;
        private System.Windows.Forms.ListView EventsLV;
        private System.Windows.Forms.ColumnHeader SourceCH;
        private System.Windows.Forms.ColumnHeader EventTypeCH;
        private System.Windows.Forms.ColumnHeader MethodCH;
        private System.Windows.Forms.ColumnHeader TimeCH;
        private System.Windows.Forms.ColumnHeader StatusCH;
        private System.Windows.Forms.ColumnHeader MessageCH;
        private System.Windows.Forms.ColumnHeader ArgumentsCH;
        private System.Windows.Forms.ToolStripMenuItem EventsMI;
        private System.Windows.Forms.ToolStripMenuItem Events_ViewMI;
        private System.Windows.Forms.ToolStripMenuItem Events_ClearMI;
    }
}
