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

namespace Opc.Ua.Sample
{
    partial class TypeNavigatorCtrl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TypeNavigatorCtrl));
            this.TypePathCTRL = new System.Windows.Forms.ToolStrip();
            this.RootBTN = new System.Windows.Forms.ToolStripDropDownButton();
            this.childToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.TypePathCTRL.SuspendLayout();
            this.SuspendLayout();
            // 
            // TypePathCTRL
            // 
            this.TypePathCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TypePathCTRL.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.TypePathCTRL.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.RootBTN});
            this.TypePathCTRL.Location = new System.Drawing.Point(0, 0);
            this.TypePathCTRL.Name = "TypePathCTRL";
            this.TypePathCTRL.Size = new System.Drawing.Size(679, 24);
            this.TypePathCTRL.TabIndex = 3;
            this.TypePathCTRL.Text = "toolStrip1";
            // 
            // RootBTN
            // 
            this.RootBTN.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.RootBTN.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.childToolStripMenuItem});
            this.RootBTN.Image = ((System.Drawing.Image)(resources.GetObject("RootBTN.Image")));
            this.RootBTN.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.RootBTN.Name = "RootBTN";
            this.RootBTN.Size = new System.Drawing.Size(45, 21);
            this.RootBTN.Text = "Root";
            this.RootBTN.ToolTipText = "Root";
            this.RootBTN.DropDownOpening += new System.EventHandler(this.RootBTN_DropDownOpening);
            this.RootBTN.Click += new System.EventHandler(this.RootBTN_Click);
            // 
            // childToolStripMenuItem
            // 
            this.childToolStripMenuItem.Name = "childToolStripMenuItem";
            this.childToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.childToolStripMenuItem.Text = "Child";
            // 
            // TypeNavigatorCtrl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.TypePathCTRL);
            this.MaximumSize = new System.Drawing.Size(3000, 24);
            this.MinimumSize = new System.Drawing.Size(0, 24);
            this.Name = "TypeNavigatorCtrl";
            this.Size = new System.Drawing.Size(679, 24);
            this.TypePathCTRL.ResumeLayout(false);
            this.TypePathCTRL.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip TypePathCTRL;
        private System.Windows.Forms.ToolStripDropDownButton RootBTN;
        private System.Windows.Forms.ToolStripMenuItem childToolStripMenuItem;

    }
}
