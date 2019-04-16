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

namespace Quickstarts
{
    partial class MainForm
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
            this.TestDataDV = new System.Windows.Forms.DataGridView();
            this.TimestampCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RawValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RawQualityCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ExpectedValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ExpectedQualityCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ActualValueCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ActualQualityCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.RowStateCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.CommentCH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.MenuBar = new System.Windows.Forms.MenuStrip();
            this.FileMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_LoadMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_LoadDefaultsMI = new System.Windows.Forms.ToolStripMenuItem();
            this.File_SaveMI = new System.Windows.Forms.ToolStripMenuItem();
            this.PropertiesPN = new System.Windows.Forms.TableLayoutPanel();
            this.TestNameCB = new System.Windows.Forms.ComboBox();
            this.TestNumberLB = new System.Windows.Forms.Label();
            this.UseSlopedExtrapolationCK = new System.Windows.Forms.CheckBox();
            this.TreatUncertainAsBadCK = new System.Windows.Forms.CheckBox();
            this.PercentGoodNP = new System.Windows.Forms.NumericUpDown();
            this.PercentBadNP = new System.Windows.Forms.NumericUpDown();
            this.AggregateCB = new System.Windows.Forms.ComboBox();
            this.UseSlopedExtrapolationLB = new System.Windows.Forms.Label();
            this.PecentGoodLB = new System.Windows.Forms.Label();
            this.PercentBadLB = new System.Windows.Forms.Label();
            this.SteppedLB = new System.Windows.Forms.Label();
            this.TreatUncertainAsBadLB = new System.Windows.Forms.Label();
            this.ProcessingIntervalLB = new System.Windows.Forms.Label();
            this.AggregateLB = new System.Windows.Forms.Label();
            this.HistorianLB = new System.Windows.Forms.Label();
            this.HistorianCB = new System.Windows.Forms.ComboBox();
            this.ProcessingIntervalNP = new System.Windows.Forms.NumericUpDown();
            this.SteppedCK = new System.Windows.Forms.CheckBox();
            this.TopPN = new System.Windows.Forms.Panel();
            this.TimeFlowsBackwardsCK = new System.Windows.Forms.CheckBox();
            this.TimeFlowsBackwardsLB = new System.Windows.Forms.Label();
            this.GenerateReportBTN = new System.Windows.Forms.Button();
            this.DeleteValuesBTN = new System.Windows.Forms.Button();
            this.CopyTestBTN = new System.Windows.Forms.Button();
            this.DeleteTestBTN = new System.Windows.Forms.Button();
            this.CopyToClipboardBTN = new System.Windows.Forms.Button();
            this.CopyActualValuesBTN = new System.Windows.Forms.Button();
            this.SaveTestBTN = new System.Windows.Forms.Button();
            this.RunTestBTN = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.TestDataDV)).BeginInit();
            this.MenuBar.SuspendLayout();
            this.PropertiesPN.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PercentGoodNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.PercentBadNP)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ProcessingIntervalNP)).BeginInit();
            this.TopPN.SuspendLayout();
            this.SuspendLayout();
            // 
            // TestDataDV
            // 
            this.TestDataDV.AllowUserToAddRows = false;
            this.TestDataDV.AllowUserToDeleteRows = false;
            this.TestDataDV.AllowUserToResizeRows = false;
            this.TestDataDV.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.TestDataDV.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.TimestampCH,
            this.RawValueCH,
            this.RawQualityCH,
            this.ExpectedValueCH,
            this.ExpectedQualityCH,
            this.ActualValueCH,
            this.ActualQualityCH,
            this.RowStateCH,
            this.CommentCH});
            this.TestDataDV.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TestDataDV.Location = new System.Drawing.Point(0, 277);
            this.TestDataDV.Name = "TestDataDV";
            this.TestDataDV.Size = new System.Drawing.Size(957, 317);
            this.TestDataDV.TabIndex = 0;
            this.TestDataDV.CellValidating += new System.Windows.Forms.DataGridViewCellValidatingEventHandler(this.TestDataDV_CellValidating);
            this.TestDataDV.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.TestDataDV_CellEndEdit);
            this.TestDataDV.RowValidated += new System.Windows.Forms.DataGridViewCellEventHandler(this.TestDataDV_RowValidated);
            // 
            // TimestampCH
            // 
            this.TimestampCH.DataPropertyName = "Timestamp";
            this.TimestampCH.HeaderText = "Timestamp";
            this.TimestampCH.Name = "TimestampCH";
            // 
            // RawValueCH
            // 
            this.RawValueCH.DataPropertyName = "RawValue";
            this.RawValueCH.HeaderText = "RawValue";
            this.RawValueCH.Name = "RawValueCH";
            // 
            // RawQualityCH
            // 
            this.RawQualityCH.DataPropertyName = "RawQuality";
            this.RawQualityCH.HeaderText = "RawQuality";
            this.RawQualityCH.Name = "RawQualityCH";
            this.RawQualityCH.Resizable = System.Windows.Forms.DataGridViewTriState.True;
            // 
            // ExpectedValueCH
            // 
            this.ExpectedValueCH.DataPropertyName = "ExpectedValue";
            this.ExpectedValueCH.HeaderText = "ExpectedValue";
            this.ExpectedValueCH.Name = "ExpectedValueCH";
            // 
            // ExpectedQualityCH
            // 
            this.ExpectedQualityCH.DataPropertyName = "ExpectedQuality";
            this.ExpectedQualityCH.HeaderText = "ExpectedQuality";
            this.ExpectedQualityCH.Name = "ExpectedQualityCH";
            // 
            // ActualValueCH
            // 
            this.ActualValueCH.DataPropertyName = "ActualValue";
            this.ActualValueCH.HeaderText = "ActualValue";
            this.ActualValueCH.Name = "ActualValueCH";
            // 
            // ActualQualityCH
            // 
            this.ActualQualityCH.DataPropertyName = "ActualQuality";
            this.ActualQualityCH.HeaderText = "ActualQuality";
            this.ActualQualityCH.Name = "ActualQualityCH";
            // 
            // RowStateCH
            // 
            this.RowStateCH.DataPropertyName = "RowState";
            this.RowStateCH.HeaderText = "RowState";
            this.RowStateCH.Name = "RowStateCH";
            this.RowStateCH.ReadOnly = true;
            // 
            // CommentCH
            // 
            this.CommentCH.DataPropertyName = "Comment";
            this.CommentCH.HeaderText = "Comment";
            this.CommentCH.Name = "CommentCH";
            this.CommentCH.Width = 500;
            // 
            // MenuBar
            // 
            this.MenuBar.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.FileMI});
            this.MenuBar.Location = new System.Drawing.Point(0, 0);
            this.MenuBar.Name = "MenuBar";
            this.MenuBar.Size = new System.Drawing.Size(957, 24);
            this.MenuBar.TabIndex = 1;
            this.MenuBar.Text = "menuStrip1";
            // 
            // FileMI
            // 
            this.FileMI.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.File_LoadMI,
            this.File_LoadDefaultsMI,
            this.File_SaveMI});
            this.FileMI.Name = "FileMI";
            this.FileMI.Size = new System.Drawing.Size(37, 20);
            this.FileMI.Text = "File";
            // 
            // File_LoadMI
            // 
            this.File_LoadMI.Name = "File_LoadMI";
            this.File_LoadMI.Size = new System.Drawing.Size(146, 22);
            this.File_LoadMI.Text = "Load";
            this.File_LoadMI.Click += new System.EventHandler(this.File_LoadMI_Click);
            // 
            // File_LoadDefaultsMI
            // 
            this.File_LoadDefaultsMI.Name = "File_LoadDefaultsMI";
            this.File_LoadDefaultsMI.Size = new System.Drawing.Size(146, 22);
            this.File_LoadDefaultsMI.Text = "Load Defaults";
            this.File_LoadDefaultsMI.Click += new System.EventHandler(this.File_LoadDefaultsMI_Click);
            // 
            // File_SaveMI
            // 
            this.File_SaveMI.Name = "File_SaveMI";
            this.File_SaveMI.Size = new System.Drawing.Size(146, 22);
            this.File_SaveMI.Text = "Save";
            this.File_SaveMI.Click += new System.EventHandler(this.File_SaveMI_Click);
            // 
            // PropertiesPN
            // 
            this.PropertiesPN.ColumnCount = 2;
            this.PropertiesPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.PropertiesPN.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.PropertiesPN.Controls.Add(this.TestNameCB, 1, 1);
            this.PropertiesPN.Controls.Add(this.TestNumberLB, 0, 1);
            this.PropertiesPN.Controls.Add(this.UseSlopedExtrapolationCK, 1, 8);
            this.PropertiesPN.Controls.Add(this.TreatUncertainAsBadCK, 1, 5);
            this.PropertiesPN.Controls.Add(this.PercentGoodNP, 1, 7);
            this.PropertiesPN.Controls.Add(this.PercentBadNP, 1, 6);
            this.PropertiesPN.Controls.Add(this.AggregateCB, 1, 2);
            this.PropertiesPN.Controls.Add(this.UseSlopedExtrapolationLB, 0, 8);
            this.PropertiesPN.Controls.Add(this.PecentGoodLB, 0, 7);
            this.PropertiesPN.Controls.Add(this.PercentBadLB, 0, 6);
            this.PropertiesPN.Controls.Add(this.SteppedLB, 0, 4);
            this.PropertiesPN.Controls.Add(this.TreatUncertainAsBadLB, 0, 5);
            this.PropertiesPN.Controls.Add(this.ProcessingIntervalLB, 0, 3);
            this.PropertiesPN.Controls.Add(this.AggregateLB, 0, 2);
            this.PropertiesPN.Controls.Add(this.HistorianLB, 0, 0);
            this.PropertiesPN.Controls.Add(this.HistorianCB, 1, 0);
            this.PropertiesPN.Controls.Add(this.ProcessingIntervalNP, 1, 3);
            this.PropertiesPN.Controls.Add(this.SteppedCK, 1, 4);
            this.PropertiesPN.Dock = System.Windows.Forms.DockStyle.Left;
            this.PropertiesPN.Location = new System.Drawing.Point(3, 3);
            this.PropertiesPN.MinimumSize = new System.Drawing.Size(260, 192);
            this.PropertiesPN.Name = "PropertiesPN";
            this.PropertiesPN.RowCount = 11;
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.PropertiesPN.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
            this.PropertiesPN.Size = new System.Drawing.Size(497, 243);
            this.PropertiesPN.TabIndex = 2;
            // 
            // TestNameCB
            // 
            this.TestNameCB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TestNameCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TestNameCB.FormattingEnabled = true;
            this.TestNameCB.Location = new System.Drawing.Point(153, 30);
            this.TestNameCB.Name = "TestNameCB";
            this.TestNameCB.Size = new System.Drawing.Size(341, 21);
            this.TestNameCB.TabIndex = 21;
            this.TestNameCB.SelectedIndexChanged += new System.EventHandler(this.TestNameCB_SelectedIndexChanged);
            // 
            // TestNumberLB
            // 
            this.TestNumberLB.AutoSize = true;
            this.TestNumberLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TestNumberLB.Location = new System.Drawing.Point(3, 27);
            this.TestNumberLB.Name = "TestNumberLB";
            this.TestNumberLB.Size = new System.Drawing.Size(144, 27);
            this.TestNumberLB.TabIndex = 20;
            this.TestNumberLB.Text = "Test Name";
            this.TestNumberLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UseSlopedExtrapolationCK
            // 
            this.UseSlopedExtrapolationCK.AutoSize = true;
            this.UseSlopedExtrapolationCK.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UseSlopedExtrapolationCK.Location = new System.Drawing.Point(153, 202);
            this.UseSlopedExtrapolationCK.Name = "UseSlopedExtrapolationCK";
            this.UseSlopedExtrapolationCK.Size = new System.Drawing.Size(341, 14);
            this.UseSlopedExtrapolationCK.TabIndex = 19;
            this.UseSlopedExtrapolationCK.UseVisualStyleBackColor = true;
            // 
            // TreatUncertainAsBadCK
            // 
            this.TreatUncertainAsBadCK.AutoSize = true;
            this.TreatUncertainAsBadCK.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TreatUncertainAsBadCK.Location = new System.Drawing.Point(153, 130);
            this.TreatUncertainAsBadCK.Name = "TreatUncertainAsBadCK";
            this.TreatUncertainAsBadCK.Size = new System.Drawing.Size(341, 14);
            this.TreatUncertainAsBadCK.TabIndex = 18;
            this.TreatUncertainAsBadCK.UseVisualStyleBackColor = true;
            // 
            // PercentGoodNP
            // 
            this.PercentGoodNP.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PercentGoodNP.Location = new System.Drawing.Point(153, 176);
            this.PercentGoodNP.Name = "PercentGoodNP";
            this.PercentGoodNP.Size = new System.Drawing.Size(341, 20);
            this.PercentGoodNP.TabIndex = 16;
            // 
            // PercentBadNP
            // 
            this.PercentBadNP.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PercentBadNP.Location = new System.Drawing.Point(153, 150);
            this.PercentBadNP.Name = "PercentBadNP";
            this.PercentBadNP.Size = new System.Drawing.Size(341, 20);
            this.PercentBadNP.TabIndex = 15;
            // 
            // AggregateCB
            // 
            this.AggregateCB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AggregateCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.AggregateCB.FormattingEnabled = true;
            this.AggregateCB.Location = new System.Drawing.Point(153, 57);
            this.AggregateCB.Name = "AggregateCB";
            this.AggregateCB.Size = new System.Drawing.Size(341, 21);
            this.AggregateCB.TabIndex = 12;
            // 
            // UseSlopedExtrapolationLB
            // 
            this.UseSlopedExtrapolationLB.AutoSize = true;
            this.UseSlopedExtrapolationLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.UseSlopedExtrapolationLB.Location = new System.Drawing.Point(3, 199);
            this.UseSlopedExtrapolationLB.Name = "UseSlopedExtrapolationLB";
            this.UseSlopedExtrapolationLB.Size = new System.Drawing.Size(144, 20);
            this.UseSlopedExtrapolationLB.TabIndex = 10;
            this.UseSlopedExtrapolationLB.Text = "Use Sloped Extrapolation";
            this.UseSlopedExtrapolationLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PecentGoodLB
            // 
            this.PecentGoodLB.AutoSize = true;
            this.PecentGoodLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PecentGoodLB.Location = new System.Drawing.Point(3, 173);
            this.PecentGoodLB.Name = "PecentGoodLB";
            this.PecentGoodLB.Size = new System.Drawing.Size(144, 26);
            this.PecentGoodLB.TabIndex = 9;
            this.PecentGoodLB.Text = "Percent Good";
            this.PecentGoodLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // PercentBadLB
            // 
            this.PercentBadLB.AutoSize = true;
            this.PercentBadLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PercentBadLB.Location = new System.Drawing.Point(3, 147);
            this.PercentBadLB.Name = "PercentBadLB";
            this.PercentBadLB.Size = new System.Drawing.Size(144, 26);
            this.PercentBadLB.TabIndex = 8;
            this.PercentBadLB.Text = "Percent Bad";
            this.PercentBadLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // SteppedLB
            // 
            this.SteppedLB.AutoSize = true;
            this.SteppedLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SteppedLB.Location = new System.Drawing.Point(3, 107);
            this.SteppedLB.Name = "SteppedLB";
            this.SteppedLB.Size = new System.Drawing.Size(144, 20);
            this.SteppedLB.TabIndex = 7;
            this.SteppedLB.Text = "Stepped";
            this.SteppedLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // TreatUncertainAsBadLB
            // 
            this.TreatUncertainAsBadLB.AutoSize = true;
            this.TreatUncertainAsBadLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TreatUncertainAsBadLB.Location = new System.Drawing.Point(3, 127);
            this.TreatUncertainAsBadLB.Name = "TreatUncertainAsBadLB";
            this.TreatUncertainAsBadLB.Size = new System.Drawing.Size(144, 20);
            this.TreatUncertainAsBadLB.TabIndex = 6;
            this.TreatUncertainAsBadLB.Text = "Treat Uncertain As Bad";
            this.TreatUncertainAsBadLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // ProcessingIntervalLB
            // 
            this.ProcessingIntervalLB.AutoSize = true;
            this.ProcessingIntervalLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProcessingIntervalLB.Location = new System.Drawing.Point(3, 81);
            this.ProcessingIntervalLB.Name = "ProcessingIntervalLB";
            this.ProcessingIntervalLB.Size = new System.Drawing.Size(144, 26);
            this.ProcessingIntervalLB.TabIndex = 5;
            this.ProcessingIntervalLB.Text = "Processing Interval";
            this.ProcessingIntervalLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AggregateLB
            // 
            this.AggregateLB.AutoSize = true;
            this.AggregateLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AggregateLB.Location = new System.Drawing.Point(3, 54);
            this.AggregateLB.Name = "AggregateLB";
            this.AggregateLB.Size = new System.Drawing.Size(144, 27);
            this.AggregateLB.TabIndex = 3;
            this.AggregateLB.Text = "Aggregate";
            this.AggregateLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // HistorianLB
            // 
            this.HistorianLB.AutoSize = true;
            this.HistorianLB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HistorianLB.Location = new System.Drawing.Point(3, 0);
            this.HistorianLB.Name = "HistorianLB";
            this.HistorianLB.Size = new System.Drawing.Size(144, 27);
            this.HistorianLB.TabIndex = 0;
            this.HistorianLB.Text = "Historian";
            this.HistorianLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // HistorianCB
            // 
            this.HistorianCB.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HistorianCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.HistorianCB.FormattingEnabled = true;
            this.HistorianCB.Location = new System.Drawing.Point(153, 3);
            this.HistorianCB.Name = "HistorianCB";
            this.HistorianCB.Size = new System.Drawing.Size(341, 21);
            this.HistorianCB.TabIndex = 11;
            this.HistorianCB.SelectedIndexChanged += new System.EventHandler(this.HistorianCB_SelectedIndexChanged);
            // 
            // ProcessingIntervalNP
            // 
            this.ProcessingIntervalNP.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ProcessingIntervalNP.Location = new System.Drawing.Point(153, 84);
            this.ProcessingIntervalNP.Maximum = new decimal(new int[] {
            1000000,
            0,
            0,
            0});
            this.ProcessingIntervalNP.Name = "ProcessingIntervalNP";
            this.ProcessingIntervalNP.Size = new System.Drawing.Size(341, 20);
            this.ProcessingIntervalNP.TabIndex = 14;
            this.ProcessingIntervalNP.ValueChanged += new System.EventHandler(this.ProcessingIntervalNP_ValueChanged);
            // 
            // SteppedCK
            // 
            this.SteppedCK.AutoSize = true;
            this.SteppedCK.Dock = System.Windows.Forms.DockStyle.Fill;
            this.SteppedCK.Location = new System.Drawing.Point(153, 110);
            this.SteppedCK.Name = "SteppedCK";
            this.SteppedCK.Size = new System.Drawing.Size(341, 14);
            this.SteppedCK.TabIndex = 17;
            this.SteppedCK.UseVisualStyleBackColor = true;
            // 
            // TopPN
            // 
            this.TopPN.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.TopPN.Controls.Add(this.TimeFlowsBackwardsCK);
            this.TopPN.Controls.Add(this.TimeFlowsBackwardsLB);
            this.TopPN.Controls.Add(this.GenerateReportBTN);
            this.TopPN.Controls.Add(this.DeleteValuesBTN);
            this.TopPN.Controls.Add(this.CopyTestBTN);
            this.TopPN.Controls.Add(this.DeleteTestBTN);
            this.TopPN.Controls.Add(this.CopyToClipboardBTN);
            this.TopPN.Controls.Add(this.CopyActualValuesBTN);
            this.TopPN.Controls.Add(this.SaveTestBTN);
            this.TopPN.Controls.Add(this.RunTestBTN);
            this.TopPN.Controls.Add(this.PropertiesPN);
            this.TopPN.Dock = System.Windows.Forms.DockStyle.Top;
            this.TopPN.Location = new System.Drawing.Point(0, 24);
            this.TopPN.Name = "TopPN";
            this.TopPN.Padding = new System.Windows.Forms.Padding(3);
            this.TopPN.Size = new System.Drawing.Size(957, 253);
            this.TopPN.TabIndex = 3;
            // 
            // TimeFlowsBackwardsCK
            // 
            this.TimeFlowsBackwardsCK.AutoSize = true;
            this.TimeFlowsBackwardsCK.Location = new System.Drawing.Point(634, 156);
            this.TimeFlowsBackwardsCK.Name = "TimeFlowsBackwardsCK";
            this.TimeFlowsBackwardsCK.Size = new System.Drawing.Size(15, 14);
            this.TimeFlowsBackwardsCK.TabIndex = 24;
            this.TimeFlowsBackwardsCK.UseVisualStyleBackColor = true;
            this.TimeFlowsBackwardsCK.CheckedChanged += new System.EventHandler(this.TimeFlowsBackwardsCK_CheckedChanged);
            // 
            // TimeFlowsBackwardsLB
            // 
            this.TimeFlowsBackwardsLB.AutoSize = true;
            this.TimeFlowsBackwardsLB.Location = new System.Drawing.Point(514, 155);
            this.TimeFlowsBackwardsLB.Name = "TimeFlowsBackwardsLB";
            this.TimeFlowsBackwardsLB.Size = new System.Drawing.Size(116, 13);
            this.TimeFlowsBackwardsLB.TabIndex = 23;
            this.TimeFlowsBackwardsLB.Text = "Time Flows Backwards";
            this.TimeFlowsBackwardsLB.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // GenerateReportBTN
            // 
            this.GenerateReportBTN.Location = new System.Drawing.Point(655, 103);
            this.GenerateReportBTN.Name = "GenerateReportBTN";
            this.GenerateReportBTN.Size = new System.Drawing.Size(132, 23);
            this.GenerateReportBTN.TabIndex = 10;
            this.GenerateReportBTN.Text = "Generate Report";
            this.GenerateReportBTN.UseVisualStyleBackColor = true;
            this.GenerateReportBTN.Click += new System.EventHandler(this.GenerateReportBTN_Click);
            // 
            // DeleteValuesBTN
            // 
            this.DeleteValuesBTN.Location = new System.Drawing.Point(655, 16);
            this.DeleteValuesBTN.Name = "DeleteValuesBTN";
            this.DeleteValuesBTN.Size = new System.Drawing.Size(132, 23);
            this.DeleteValuesBTN.TabIndex = 9;
            this.DeleteValuesBTN.Text = "Delete Values";
            this.DeleteValuesBTN.UseVisualStyleBackColor = true;
            this.DeleteValuesBTN.Click += new System.EventHandler(this.DeleteValuesBTN_Click);
            // 
            // CopyTestBTN
            // 
            this.CopyTestBTN.Enabled = false;
            this.CopyTestBTN.Location = new System.Drawing.Point(517, 45);
            this.CopyTestBTN.Name = "CopyTestBTN";
            this.CopyTestBTN.Size = new System.Drawing.Size(132, 23);
            this.CopyTestBTN.TabIndex = 8;
            this.CopyTestBTN.Text = "Copy Test";
            this.CopyTestBTN.UseVisualStyleBackColor = true;
            this.CopyTestBTN.Click += new System.EventHandler(this.CopyTestBTN_Click);
            // 
            // DeleteTestBTN
            // 
            this.DeleteTestBTN.Enabled = false;
            this.DeleteTestBTN.Location = new System.Drawing.Point(517, 74);
            this.DeleteTestBTN.Name = "DeleteTestBTN";
            this.DeleteTestBTN.Size = new System.Drawing.Size(132, 23);
            this.DeleteTestBTN.TabIndex = 7;
            this.DeleteTestBTN.Text = "Delete Test";
            this.DeleteTestBTN.UseVisualStyleBackColor = true;
            this.DeleteTestBTN.Click += new System.EventHandler(this.DeleteTestBTN_Click);
            // 
            // CopyToClipboardBTN
            // 
            this.CopyToClipboardBTN.Location = new System.Drawing.Point(655, 74);
            this.CopyToClipboardBTN.Name = "CopyToClipboardBTN";
            this.CopyToClipboardBTN.Size = new System.Drawing.Size(132, 23);
            this.CopyToClipboardBTN.TabIndex = 6;
            this.CopyToClipboardBTN.Text = "Copy To Clipboard";
            this.CopyToClipboardBTN.UseVisualStyleBackColor = true;
            this.CopyToClipboardBTN.Click += new System.EventHandler(this.CopyToClipboardBTN_Click);
            // 
            // CopyActualValuesBTN
            // 
            this.CopyActualValuesBTN.Location = new System.Drawing.Point(655, 45);
            this.CopyActualValuesBTN.Name = "CopyActualValuesBTN";
            this.CopyActualValuesBTN.Size = new System.Drawing.Size(132, 23);
            this.CopyActualValuesBTN.TabIndex = 5;
            this.CopyActualValuesBTN.Text = "Copy Actual Values";
            this.CopyActualValuesBTN.UseVisualStyleBackColor = true;
            this.CopyActualValuesBTN.Click += new System.EventHandler(this.CopyActualValuesBTN_Click);
            // 
            // SaveTestBTN
            // 
            this.SaveTestBTN.Enabled = false;
            this.SaveTestBTN.Location = new System.Drawing.Point(517, 16);
            this.SaveTestBTN.Name = "SaveTestBTN";
            this.SaveTestBTN.Size = new System.Drawing.Size(132, 23);
            this.SaveTestBTN.TabIndex = 4;
            this.SaveTestBTN.Text = "Save Test";
            this.SaveTestBTN.UseVisualStyleBackColor = true;
            this.SaveTestBTN.Click += new System.EventHandler(this.SaveTestBTN_Click);
            // 
            // RunTestBTN
            // 
            this.RunTestBTN.Location = new System.Drawing.Point(517, 103);
            this.RunTestBTN.Name = "RunTestBTN";
            this.RunTestBTN.Size = new System.Drawing.Size(132, 23);
            this.RunTestBTN.TabIndex = 3;
            this.RunTestBTN.Text = "Run Test";
            this.RunTestBTN.UseVisualStyleBackColor = true;
            this.RunTestBTN.Click += new System.EventHandler(this.RunTestBTN_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(957, 594);
            this.Controls.Add(this.TestDataDV);
            this.Controls.Add(this.TopPN);
            this.Controls.Add(this.MenuBar);
            this.MainMenuStrip = this.MenuBar;
            this.Name = "MainForm";
            this.Text = "HA Aggregate Tester";
            ((System.ComponentModel.ISupportInitialize)(this.TestDataDV)).EndInit();
            this.MenuBar.ResumeLayout(false);
            this.MenuBar.PerformLayout();
            this.PropertiesPN.ResumeLayout(false);
            this.PropertiesPN.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PercentGoodNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.PercentBadNP)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ProcessingIntervalNP)).EndInit();
            this.TopPN.ResumeLayout(false);
            this.TopPN.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.DataGridView TestDataDV;
        private System.Windows.Forms.MenuStrip MenuBar;
        private System.Windows.Forms.ToolStripMenuItem FileMI;
        private System.Windows.Forms.ToolStripMenuItem File_LoadDefaultsMI;
        private System.Windows.Forms.TableLayoutPanel PropertiesPN;
        private System.Windows.Forms.Label HistorianLB;
        private System.Windows.Forms.Label ProcessingIntervalLB;
        private System.Windows.Forms.Label AggregateLB;
        private System.Windows.Forms.Label PercentBadLB;
        private System.Windows.Forms.Label SteppedLB;
        private System.Windows.Forms.Label TreatUncertainAsBadLB;
        private System.Windows.Forms.Label PecentGoodLB;
        private System.Windows.Forms.Label UseSlopedExtrapolationLB;
        private System.Windows.Forms.ComboBox HistorianCB;
        private System.Windows.Forms.CheckBox TreatUncertainAsBadCK;
        private System.Windows.Forms.NumericUpDown PercentGoodNP;
        private System.Windows.Forms.NumericUpDown PercentBadNP;
        private System.Windows.Forms.ComboBox AggregateCB;
        private System.Windows.Forms.NumericUpDown ProcessingIntervalNP;
        private System.Windows.Forms.CheckBox SteppedCK;
        private System.Windows.Forms.CheckBox UseSlopedExtrapolationCK;
        private System.Windows.Forms.Panel TopPN;
        private System.Windows.Forms.ToolStripMenuItem File_SaveMI;
        private System.Windows.Forms.Button RunTestBTN;
        private System.Windows.Forms.ComboBox TestNameCB;
        private System.Windows.Forms.Label TestNumberLB;
        private System.Windows.Forms.Button CopyActualValuesBTN;
        private System.Windows.Forms.Button SaveTestBTN;
        private System.Windows.Forms.Button CopyToClipboardBTN;
        private System.Windows.Forms.ToolStripMenuItem File_LoadMI;
        private System.Windows.Forms.DataGridViewTextBoxColumn TimestampCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn RawValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn RawQualityCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ExpectedValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ExpectedQualityCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ActualValueCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn ActualQualityCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn RowStateCH;
        private System.Windows.Forms.DataGridViewTextBoxColumn CommentCH;
        private System.Windows.Forms.Button DeleteTestBTN;
        private System.Windows.Forms.Button CopyTestBTN;
        private System.Windows.Forms.Button DeleteValuesBTN;
        private System.Windows.Forms.Button GenerateReportBTN;
        private System.Windows.Forms.CheckBox TimeFlowsBackwardsCK;
        private System.Windows.Forms.Label TimeFlowsBackwardsLB;
    }
}
