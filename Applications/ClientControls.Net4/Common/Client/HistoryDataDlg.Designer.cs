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
    partial class HistoryDataDlg
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
            this.HistoryDataCTRL = new Opc.Ua.Client.Controls.HistoryDataListView();
            this.SuspendLayout();
            // 
            // HistoryDataCTRL
            // 
            this.HistoryDataCTRL.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HistoryDataCTRL.EndTime = new System.DateTime(((long)(0)));
            this.HistoryDataCTRL.Location = new System.Drawing.Point(0, 0);
            this.HistoryDataCTRL.MaxReturnValues = ((uint)(0u));
            this.HistoryDataCTRL.Name = "HistoryDataCTRL";
            this.HistoryDataCTRL.NodeId = null;
            this.HistoryDataCTRL.ProcessingInterval = 10000D;
            this.HistoryDataCTRL.ReadType = Opc.Ua.Client.Controls.HistoryDataListView.HistoryReadType.Raw;
            this.HistoryDataCTRL.ReturnBounds = false;
            this.HistoryDataCTRL.Size = new System.Drawing.Size(784, 362);
            this.HistoryDataCTRL.StartTime = new System.DateTime(2015, 5, 25, 16, 46, 5, 399);
            this.HistoryDataCTRL.TabIndex = 13;
            // 
            // HistoryDataDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 362);
            this.Controls.Add(this.HistoryDataCTRL);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "HistoryDataDlg";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "History Data";
            this.ResumeLayout(false);

        }

        #endregion

        private HistoryDataListView HistoryDataCTRL;
    }
}
