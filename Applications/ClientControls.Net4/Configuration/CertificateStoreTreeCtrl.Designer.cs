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
    partial class CertificateStoreTreeCtrl
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
            this.CopyMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PasteMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PopupMenu.SuspendLayout();
            this.SuspendLayout();
            // 
            // NodesTV
            // 
            this.NodesTV.ContextMenuStrip = this.PopupMenu;
            this.NodesTV.LineColor = System.Drawing.Color.Black;
            this.NodesTV.ShowRootLines = false;
            // 
            // PopupMenu
            // 
            this.PopupMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CopyMI,
            this.PasteMI});
            this.PopupMenu.Name = "PopupMenu";
            this.PopupMenu.Size = new System.Drawing.Size(153, 70);
            // 
            // CopyMI
            // 
            this.CopyMI.Name = "CopyMI";
            this.CopyMI.Size = new System.Drawing.Size(152, 22);
            this.CopyMI.Text = "Copy";
            this.CopyMI.Click += new System.EventHandler(this.CopyMI_Click);
            // 
            // PasteMI
            // 
            this.PasteMI.Name = "PasteMI";
            this.PasteMI.Size = new System.Drawing.Size(152, 22);
            this.PasteMI.Text = "Paste";
            this.PasteMI.Click += new System.EventHandler(this.PasteMI_Click);
            // 
            // CertificateStoreTreeCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.EnableDragging = true;
            this.Name = "CertificateStoreTreeCtrl";
            this.Controls.SetChildIndex(this.NodesTV, 0);
            this.PopupMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ContextMenuStrip PopupMenu;
        private System.Windows.Forms.ToolStripMenuItem CopyMI;
        private System.Windows.Forms.ToolStripMenuItem PasteMI;

    }
}
