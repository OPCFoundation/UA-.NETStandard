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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Client.Controls;

namespace Quickstarts
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();

            m_dataset = new DataSet();
            m_dataset.Tables.Add("TestData");

            m_dataset.Tables[0].Columns.Add("Timestamp", typeof(string));
            m_dataset.Tables[0].Columns.Add("RawValue", typeof(string));
            m_dataset.Tables[0].Columns.Add("RawQuality", typeof(string));
            m_dataset.Tables[0].Columns.Add("ExpectedValue", typeof(string));
            m_dataset.Tables[0].Columns.Add("ExpectedQuality", typeof(string));
            m_dataset.Tables[0].Columns.Add("ActualValue", typeof(string));
            m_dataset.Tables[0].Columns.Add("ActualQuality", typeof(string));
            m_dataset.Tables[0].Columns.Add("RowState", typeof(string));
            m_dataset.Tables[0].Columns.Add("Comment", typeof(string));

            m_dataset.Tables[0].DefaultView.Sort = "Timestamp";

            TestDataDV.DataSource = m_dataset.Tables[0];

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Interpolative);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Average);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_TimeAverage);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_TimeAverage2);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Total);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Total2);

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Minimum);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Maximum);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_MinimumActualTime);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_MaximumActualTime);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Range);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Minimum2);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Maximum2);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_MinimumActualTime2);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_MaximumActualTime2);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Range2);

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Count);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_AnnotationCount);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_DurationInStateZero);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_DurationInStateNonZero);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_NumberOfTransitions);

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Start);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_End);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_Delta);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_StartBound);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_EndBound);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_DeltaBounds);

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_DurationGood);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_DurationBad);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_PercentGood);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_PercentBad);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_WorstQuality);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_WorstQuality2);

            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_StandardDeviationPopulation);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_VariancePopulation);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_StandardDeviationSample);
            AggregateCB.Items.Add(Opc.Ua.BrowseNames.AggregateFunction_VarianceSample);

            AggregateCB.Enabled = false;
        }

        private enum RowState
        {
            Raw,
            MissingExpected,
            OK,
            Success,
            Failed,
        }

        private DataSet m_dataset;
        private ProcessedDataSetType m_currentDataSet;
        private TestData m_testData;
        private bool m_loading;

        /// <summary>
        /// Adds a raw value to the grid.
        /// </summary>
        private void AddRawValue(TestData.DataValue value)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = TestData.FormatTimestamp(value.SourceTimestamp);
            row[1] = String.Empty;
            row[2] = String.Empty;
            row[3] = String.Empty;
            row[4] = String.Empty;
            row[5] = String.Empty;
            row[6] = String.Empty;
            row[7] = RowState.Raw.ToString();

            UpdateRawValue(row, value);

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates a raw value in the grid.
        /// </summary>
        private void UpdateRawValue(DataRow row, TestData.DataValue value)
        {
            row[1] = TestData.FormatValue(value.WrappedValue);
            row[2] = TestData.FormatQuality(value.StatusCode);
            row[8] = value.Comment;
        }

        /// <summary>
        /// Adds am expected value to the grid.
        /// </summary>
        private void AddExpectedValue(TestData.DataValue value, RowState type)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = TestData.FormatTimestamp(value.SourceTimestamp);
            row[1] = String.Empty;
            row[2] = String.Empty;
            row[3] = String.Empty;
            row[4] = String.Empty;
            row[5] = String.Empty;
            row[6] = String.Empty;
            
            UpdateExpectedValue(row, value, type);

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates an expected value in the grid.
        /// </summary>
        private void UpdateExpectedValue(DataRow row, TestData.DataValue value, RowState type)
        {
            if (type != RowState.MissingExpected)
            {
                row[3] = TestData.FormatValue(value.WrappedValue);
                row[4] = TestData.FormatQuality(value.StatusCode);
            }
            else
            {
                row[3] = String.Empty;
                row[4] = String.Empty;
            }

            row[7] = type.ToString();
            row[8] = value.Comment;
        }

        /// <summary>
        /// Adds an actual value to the grid.
        /// </summary>
        private void AddActualValue(TestData.DataValue value, RowState type)
        {
            DataRow row = m_dataset.Tables[0].NewRow();

            row[0] = TestData.FormatTimestamp(value.SourceTimestamp);
            row[1] = String.Empty;
            row[2] = String.Empty;
            row[3] = String.Empty;
            row[4] = String.Empty;
            row[5] = String.Empty;
            row[6] = String.Empty;
            row[8] = value.Comment;
                        
            UpdateActualValue(row, value, type);

            m_dataset.Tables[0].Rows.Add(row);
        }

        /// <summary>
        /// Updates an actual value in the grid.
        /// </summary>
        private void UpdateActualValue(DataRow row, TestData.DataValue value, RowState type)
        {
            if (value != null)
            {
                row[5] = TestData.FormatValue(value.WrappedValue);
                row[6] = TestData.FormatQuality(value.StatusCode);
            }
            else
            {
                row[5] = String.Empty;
                row[6] = String.Empty;
            }

            row[7] = type.ToString();
        }

        /// <summary>
        /// Checks the row state after changes to the contents.
        /// </summary>
        private void UpdatesComplete()
        {
            foreach (DataGridViewRow row in TestDataDV.Rows)
            {
                string state = row.Cells[7].FormattedValue as string;

                if (state == null || state == RowState.Raw.ToString())
                {
                    row.DefaultCellStyle.BackColor = Color.Empty;
                    continue;
                }

                if (state == RowState.MissingExpected.ToString())
                {
                    row.DefaultCellStyle.BackColor = Color.LightSteelBlue;
                    continue;
                }

                if (state == RowState.OK.ToString())
                {
                    row.DefaultCellStyle.BackColor = Color.Khaki;
                    continue;
                }

                if (state == RowState.Success.ToString())
                {
                    row.DefaultCellStyle.BackColor = Color.PaleGreen;
                    continue;
                }

                if (state == RowState.Failed.ToString())
                {
                    row.DefaultCellStyle.BackColor = Color.LightSalmon;
                    continue;
                }
            }
        }

        /// <summary>
        /// Loads the test data.
        /// </summary>
        private void LoadData(TestData testData)
        {
            m_loading = true;

            try
            {
                HistorianCB.Items.Clear();
                m_dataset.Tables[0].Clear();

                if (testData.RawDataSets != null)
                {
                    foreach (RawDataSetType dataset in testData.RawDataSets)
                    {
                        HistorianCB.Items.Add(dataset.Name);
                    }
                }

                if (HistorianCB.Items.Count > 0)
                {
                    HistorianCB.SelectedIndex = 0;
                }
            }
            finally
            {
                m_loading = false;
            }

            UpdatesComplete();
        }

        /// <summary>
        /// Saves the test data.
        /// </summary>
        private void SaveData(TestData testData)
        {
            m_currentDataSet.DataSetName = HistorianCB.SelectedItem as string;
            m_currentDataSet.AggregateName = AggregateCB.SelectedItem as string;
            m_currentDataSet.PercentBad = (byte)PercentBadNP.Value;
            m_currentDataSet.PercentGood = (byte)PercentGoodNP.Value;
            m_currentDataSet.ProcessingInterval = (uint)ProcessingIntervalNP.Value;
            m_currentDataSet.Stepped = SteppedCK.Checked;
            m_currentDataSet.TreatUncertainAsBad = TreatUncertainAsBadCK.Checked;
            m_currentDataSet.UseSlopedExtrapolation = UseSlopedExtrapolationCK.Checked;

            m_currentDataSet.Name = TestData.GetName(m_currentDataSet);

            ResetRowState();

            List<TestData.DataValue> values = new List<TestData.DataValue>();
            DataView view = new DataView(m_dataset.Tables[0], "RowState = 'OK'", "Timestamp", DataViewRowState.CurrentRows);

            foreach (DataRowView row in view)
            {
                TestData.DataValue dv = new TestData.DataValue();
                dv.WrappedValue = TestData.ValidateValue(row[3]);
                dv.StatusCode = TestData.ValidateQuality(row[4]);
                dv.SourceTimestamp = TestData.ValidateTimestamp(row[0]);

                string comment = row[8] as string;

                if (!String.IsNullOrEmpty(comment))
                {
                    dv.Comment = comment;
                }
               
                values.Add(dv);
            }

            testData.SetProcessedValues(m_currentDataSet, values);

            if (Object.ReferenceEquals(TestNameCB.SelectedItem, m_currentDataSet))
            {
                int index = TestNameCB.SelectedIndex;
                TestNameCB.Items.RemoveAt(index);
                TestNameCB.Items.Insert(index, m_currentDataSet);
                TestNameCB.SelectedIndex = index;
            }
        }
        
        /// <summary>
        /// Adds the dataset to the grid.
        /// </summary>
        private void AddDataSetToGrid(SortedDictionary<DateTime, TestData.DataValue> values, bool isRaw)
        {
            if (values == null)
            {
                return;
            }

            foreach (TestData.DataValue value in values.Values)
            {
                DataRowView row = FindRowByTimestamp(value.SourceTimestamp);

                if (row == null)
                {
                    if (isRaw)
                    {
                        AddRawValue(value);
                    }
                    else
                    {
                        AddExpectedValue(value, RowState.OK);
                    }
                }
                else
                {
                    if (isRaw)
                    {
                        UpdateRawValue(row.Row, value);
                    }
                    else
                    {
                        UpdateExpectedValue(row.Row, value, RowState.OK);
                    }
                }
            }

            m_dataset.AcceptChanges();
        }

        /// <summary>
        /// Finds the row with the specified timestamp.
        /// </summary>
        private DataRowView FindRowByTimestamp(DateTime timestamp)
        {
            string filter = String.Format("Timestamp = '{0}'", TestData.FormatTimestamp(timestamp));
            DataView view = new DataView(m_dataset.Tables[0], filter, null, DataViewRowState.CurrentRows);

            if (view.Count > 0)
            {
                return view[0];
            }

            return null;
        }

        private void ResetRowState()
        {
            DataView view = new DataView(m_dataset.Tables[0], "RowState = 'Success' OR RowState = 'Failed'", "Timestamp", DataViewRowState.CurrentRows);

            foreach (DataRowView row in view)
            {
                row[7] = RowState.OK.ToString();
            }
        }

        private void DeleteValuesBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.AcceptChanges();
                m_loading = true;

                try
                {
                    m_dataset.Tables[0].Clear();

                    // clear the expected values.
                    if (m_currentDataSet != null)
                    {
                        m_currentDataSet.Values = new ValueType[0];
                        AddDataSetToGrid(m_testData.GetRawValues(m_currentDataSet.DataSetName), true);
                        UpdatesComplete();
                    }
                }
                catch (Exception exception)
                {
                    ClientUtils.HandleException(this.Text, exception);
                }
                finally
                {
                    m_loading = false;
                }

                ResetRowState();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void GenerateReport()
        {
            StringBuilder buffer = new StringBuilder();

            GenerateReport(buffer, BrowseNames.AggregateFunction_Interpolative);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Average);
            GenerateReport(buffer, BrowseNames.AggregateFunction_TimeAverage);
            GenerateReport(buffer, BrowseNames.AggregateFunction_TimeAverage2);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Total);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Total2);

            GenerateReport(buffer, BrowseNames.AggregateFunction_Minimum);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Maximum);
            GenerateReport(buffer, BrowseNames.AggregateFunction_MinimumActualTime);
            GenerateReport(buffer, BrowseNames.AggregateFunction_MaximumActualTime);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Range);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Minimum2);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Maximum2);
            GenerateReport(buffer, BrowseNames.AggregateFunction_MinimumActualTime2);
            GenerateReport(buffer, BrowseNames.AggregateFunction_MaximumActualTime2);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Range2);

            GenerateReport(buffer, BrowseNames.AggregateFunction_Count);
            GenerateReport(buffer, BrowseNames.AggregateFunction_AnnotationCount);
            GenerateReport(buffer, BrowseNames.AggregateFunction_DurationInStateZero);
            GenerateReport(buffer, BrowseNames.AggregateFunction_DurationInStateNonZero);
            GenerateReport(buffer, BrowseNames.AggregateFunction_NumberOfTransitions);

            GenerateReport(buffer, BrowseNames.AggregateFunction_Start);
            GenerateReport(buffer, BrowseNames.AggregateFunction_End);
            GenerateReport(buffer, BrowseNames.AggregateFunction_Delta);
            GenerateReport(buffer, BrowseNames.AggregateFunction_StartBound);
            GenerateReport(buffer, BrowseNames.AggregateFunction_EndBound);
            GenerateReport(buffer, BrowseNames.AggregateFunction_DeltaBounds);

            GenerateReport(buffer, BrowseNames.AggregateFunction_DurationGood);
            GenerateReport(buffer, BrowseNames.AggregateFunction_DurationBad);
            GenerateReport(buffer, BrowseNames.AggregateFunction_PercentGood);
            GenerateReport(buffer, BrowseNames.AggregateFunction_PercentBad);
            GenerateReport(buffer, BrowseNames.AggregateFunction_WorstQuality);
            GenerateReport(buffer, BrowseNames.AggregateFunction_WorstQuality2);

            GenerateReport(buffer, BrowseNames.AggregateFunction_StandardDeviationPopulation);
            GenerateReport(buffer, BrowseNames.AggregateFunction_VariancePopulation);
            GenerateReport(buffer, BrowseNames.AggregateFunction_StandardDeviationSample);
            GenerateReport(buffer, BrowseNames.AggregateFunction_VarianceSample);

            CopyToClipboard(buffer);
        }

        private void GenerateReport(StringBuilder buffer, string aggregateName)
        {
            buffer.Append("<h1>");
            buffer.Append(aggregateName);
            buffer.Append("</h1>");
                        
            foreach (RawDataSetType dataset in m_testData.RawDataSets)
            { 
                ProcessedDataSetType dataSetToExport = null;

                foreach (ProcessedDataSetType processedDataSet in m_testData.ProcessedDataSets)
                {
                    if (processedDataSet.DataSetName == dataset.Name && processedDataSet.AggregateName == aggregateName)
                    {
                        dataSetToExport = processedDataSet;
                        break;
                    }
                }

                if (dataSetToExport != null)
                {
                    GenerateReportTable(buffer, dataSetToExport);
                }
            }
        }
        
        private void GenerateReportTable(StringBuilder buffer, ProcessedDataSetType dataset)
        {
            buffer.Append("<table border='1'>");

            buffer.Append("<tr>");
            buffer.Append("<th colspan='4'><b>");
            buffer.Append(dataset.DataSetName);
            buffer.Append("</b></th>");
            buffer.Append("</tr>");

            buffer.Append("<th><b>Timestamp</b></td>");
            buffer.Append("<th><b>Value</b></th>");
            buffer.Append("<th><b>StatusCode</b></th>");
            buffer.Append("<th><b>Notes</b></th>");
            buffer.Append("</tr>");

            m_dataset.AcceptChanges();
            ResetRowState();

            foreach (ValueType value in dataset.Values)
            {
                buffer.Append("<tr>");
                buffer.Append("<td>");
                buffer.Append(value.Timestamp);
                buffer.Append("</td>");
                buffer.Append("<td>");

                Variant result = TestData.ValidateValue(value.Value);

                if (result.TypeInfo != null)
                {
                    if (result.TypeInfo.BuiltInType == BuiltInType.Double)
                    {
                        if (Math.Truncate((double)result.Value) == (double)result.Value)
                        {
                            buffer.AppendFormat("{0}", (long)(double)result.Value);
                        }
                        else
                        {
                            buffer.AppendFormat("{0:F3}", result.Value);
                        }
                    }
                    else
                    {
                        buffer.Append(result);
                    }
                }

                buffer.Append("</td>");
                buffer.Append("<td>");
                buffer.Append(value.Quality);
                buffer.Append("</td>");
                buffer.Append("<td>");
                buffer.Append(value.Comment);
                buffer.Append("</td>");
                buffer.Append("</tr>");
            }

            buffer.Append("</table>");
            buffer.Append("<br/>");
            buffer.Append("<br/>");
        }

        private void GenerateData()
        {
            AggregateConfiguration configuration = new AggregateConfiguration();
            configuration.TreatUncertainAsBad = false;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = false;
            configuration.UseServerCapabilitiesDefaults = false;

            GenerateData("Historian1", configuration, false, false);

            configuration.TreatUncertainAsBad = true;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = true;
            configuration.UseServerCapabilitiesDefaults = false;

            GenerateData("Historian2", configuration, false, false);

            configuration.TreatUncertainAsBad = true;
            configuration.PercentDataBad = 50;
            configuration.PercentDataGood = 50;
            configuration.UseSlopedExtrapolation = false;
            configuration.UseServerCapabilitiesDefaults = false;

            GenerateData("Historian3", configuration, true, false);

            configuration.TreatUncertainAsBad = true;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = false;
            configuration.UseServerCapabilitiesDefaults = false;

            GenerateData("Historian4", configuration, true, true);

            configuration.TreatUncertainAsBad = false;
            configuration.PercentDataBad = 100;
            configuration.PercentDataGood = 100;
            configuration.UseSlopedExtrapolation = false;
            configuration.UseServerCapabilitiesDefaults = false;

            GenerateData("Historian5", configuration, false, false);
        }

        private void GenerateData(string historianName, AggregateConfiguration configuration, bool stepped, bool discrete)
        {
            if (!discrete)
            {
                GenerateData(historianName, BrowseNames.AggregateFunction_Interpolative, 5000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Average, 5000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_TimeAverage, 5000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_TimeAverage2, 5000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Total, 5000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Total2, 5000, configuration, stepped);

                GenerateData(historianName, BrowseNames.AggregateFunction_Minimum, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Maximum, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_MinimumActualTime, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_MaximumActualTime, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Range, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Minimum2, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Maximum2, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_MinimumActualTime2, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_MaximumActualTime2, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Range2, 16000, configuration, stepped);
            }

            GenerateData(historianName, BrowseNames.AggregateFunction_Count, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_AnnotationCount, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_DurationInStateZero, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_DurationInStateNonZero, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_NumberOfTransitions, 16000, configuration, stepped);

            if (!discrete)
            {
                GenerateData(historianName, BrowseNames.AggregateFunction_Start, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_End, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_Delta, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_StartBound, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_EndBound, 16000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_DeltaBounds, 16000, configuration, stepped);
            }

            GenerateData(historianName, BrowseNames.AggregateFunction_DurationGood, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_DurationBad, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_PercentGood, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_PercentBad, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_WorstQuality, 16000, configuration, stepped);
            GenerateData(historianName, BrowseNames.AggregateFunction_WorstQuality2, 16000, configuration, stepped);

            if (!discrete)
            {
                GenerateData(historianName, BrowseNames.AggregateFunction_StandardDeviationPopulation, 20000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_VariancePopulation, 20000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_StandardDeviationSample, 20000, configuration, stepped);
                GenerateData(historianName, BrowseNames.AggregateFunction_VarianceSample, 20000, configuration, stepped);
            }
        }

        private void GenerateData(string historianName, string aggregateName, double processingInterval, AggregateConfiguration configuration, bool stepped)
        {
            DateTime startTime = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                Aggregators.GetIdForStandardAggregate(aggregateName),
                startTime.AddSeconds(0),
                startTime.AddSeconds(100),
                processingInterval,
                stepped,
                configuration);

            SortedDictionary<DateTime, TestData.DataValue> rawValues = m_testData.GetRawValues(historianName);
            List<TestData.DataValue> processedValues = new List<TestData.DataValue>();

            foreach (TestData.DataValue rawValue in rawValues.Values)
            {
                if (!calculator.QueueRawValue((Opc.Ua.DataValue)rawValue))
                {
                    break;
                }

                DataValue processedValue = calculator.GetProcessedValue(false);

                if (processedValue != null)
                {
                    processedValues.Add(new TestData.DataValue(processedValue));
                }
            }

            for (DataValue processedValue = calculator.GetProcessedValue(true); processedValue != null; processedValue = calculator.GetProcessedValue(true))
            {
                processedValues.Add(new TestData.DataValue(processedValue));
            }

            ProcessedDataSetType dataset = new ProcessedDataSetType();

            dataset.DataSetName = historianName;
            dataset.AggregateName = aggregateName;
            dataset.Stepped = stepped;
            dataset.UseSlopedExtrapolation = configuration.UseSlopedExtrapolation;
            dataset.TreatUncertainAsBad = configuration.TreatUncertainAsBad;
            dataset.ProcessingInterval = (uint)processingInterval;
            dataset.PercentBad = configuration.PercentDataBad;
            dataset.PercentGood = configuration.PercentDataGood;
            
            m_testData.AddDataSet(dataset);
            m_testData.UpdateProcessedValues(dataset, processedValues.ToArray());
        }

        private void DoTest()
        {
            if (HistorianCB.SelectedItem == null)
            {
                return;
            }

            m_dataset.AcceptChanges();

            // reset row state.
            ResetRowState();
            
            AggregateConfiguration configuration = new AggregateConfiguration();
            configuration.TreatUncertainAsBad = TreatUncertainAsBadCK.Checked;
            configuration.PercentDataGood = (byte)PercentGoodNP.Value;
            configuration.PercentDataBad = (byte)PercentBadNP.Value;
            configuration.UseSlopedExtrapolation = UseSlopedExtrapolationCK.Checked;
            configuration.UseServerCapabilitiesDefaults = false;

            DateTime startTime = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            IAggregateCalculator calculator = Aggregators.CreateStandardCalculator(
                Aggregators.GetIdForStandardAggregate(AggregateCB.SelectedItem as string),
                (!this.TimeFlowsBackwardsCK.Checked)?startTime:startTime.AddSeconds(100),
                (this.TimeFlowsBackwardsCK.Checked)?startTime:startTime.AddSeconds(100),
                (double)ProcessingIntervalNP.Value,
                SteppedCK.Checked,
                configuration);

            SortedDictionary<DateTime, TestData.DataValue> rawValues = m_testData.GetRawValues(HistorianCB.SelectedItem as string);
            List<DataValue> processedValues = new List<DataValue>();

            List<Opc.Ua.DataValue> valuesToProcess = new List<DataValue>();

            foreach (TestData.DataValue rawValue in rawValues.Values)
            {
                valuesToProcess.Add((Opc.Ua.DataValue)rawValue);
            }

            if (TimeFlowsBackwardsCK.Checked)
            {
                valuesToProcess.Reverse();
            }

            foreach (Opc.Ua.DataValue rawValue in valuesToProcess)
            {
                if (!calculator.QueueRawValue(rawValue))
                {
                    break;
                }

                DataValue processedValue = calculator.GetProcessedValue(false);

                if (processedValue != null)
                {
                    processedValues.Add(processedValue);
                }
            }

            for (DataValue processedValue = calculator.GetProcessedValue(true); processedValue != null; processedValue = calculator.GetProcessedValue(true))
            {
                processedValues.Add(processedValue);
            }

            string sort = "Timestamp";

            if (TimeFlowsBackwardsCK.Checked)
            {
                sort += " DESC";
            }

            DataView view = new DataView(m_dataset.Tables[0], "RowState = 'OK'", sort, DataViewRowState.CurrentRows);

            int index = 0;

            foreach (DataRowView row in view)
            {
                if (index >= processedValues.Count)
                {
                    UpdateActualValue(row.Row, null, RowState.Failed);
                    continue;
                }

                TestData.DataValue actualValue = new TestData.DataValue(processedValues[index++]);
                DateTime expectedTimestamp = TestData.ValidateTimestamp(row[0]);

                if (expectedTimestamp != actualValue.SourceTimestamp)
                {
                    AddActualValue(actualValue, RowState.Failed);

                    bool found = false;

                    while (TimeFlowsBackwardsCK.Checked && expectedTimestamp < actualValue.SourceTimestamp)
                    {
                        actualValue = new TestData.DataValue(processedValues[index++]);

                        if (expectedTimestamp == actualValue.SourceTimestamp)
                        {
                            found = true;
                            break;
                        }

                        AddActualValue(actualValue, RowState.Failed);
                    }

                    if (!found)
                    {
                        continue;
                    }
                }

                StatusCode expectedQuality = TestData.ValidateQuality(row[4]);

                if (expectedQuality != actualValue.StatusCode)
                {
                    UpdateActualValue(row.Row, actualValue, RowState.Failed);
                    continue;
                }

                if (StatusCode.IsNotBad(expectedQuality))
                {
                    Variant expectedValue = TestData.ValidateValue(row[3]);

                    StatusCode? statusValue1 = expectedValue.Value as StatusCode?;

                    if (statusValue1 != null)
                    {
                        StatusCode? statusValue2 = actualValue.Value as StatusCode?;

                        if (statusValue2 == null || statusValue2.Value != statusValue1.Value)
                        {
                            UpdateActualValue(row.Row, actualValue, RowState.Failed);
                            continue;
                        }
                    }

                    else
                    {
                        double value1 = Math.Round(Convert.ToDouble(expectedValue.Value), 4);
                        double value2 = Math.Round(Convert.ToDouble(actualValue.Value), 4);

                        if (value1 != value2)
                        {
                            UpdateActualValue(row.Row, actualValue, RowState.Failed);
                            continue;
                        }
                    }
                }

                UpdateActualValue(row.Row, actualValue, RowState.Success);
            }

            // add any unexpected data at the end.
            while (index < processedValues.Count)
            {
                TestData.DataValue actualValue = new TestData.DataValue(processedValues[index++]);

                DataRowView row = FindRowByTimestamp(actualValue.SourceTimestamp);

                if (row == null)
                {
                    AddActualValue(actualValue, RowState.Failed);
                }
                else
                {
                    UpdateActualValue(row.Row, actualValue, RowState.Failed);
                }
            }

            m_dataset.AcceptChanges();
            UpdatesComplete();
        }

        private void File_LoadDefaultsMI_Click(object sender, EventArgs e)
        {
            try
            {
                Stream istrm = Assembly.GetExecutingAssembly().GetManifestResourceStream("Quickstarts.DefaultData.xml");
                XmlSerializer serializer = new XmlSerializer(typeof(TestData));
                m_testData = (TestData)serializer.Deserialize(istrm);
                m_testData.ProcessedDataSets = new ProcessedDataSetType[0];
                GenerateData();
                LoadData(m_testData);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }

        }

        private void TestDataDV_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                DataGridViewRow row = TestDataDV.Rows[e.RowIndex];
                DataGridViewCell cell = TestDataDV.Rows[e.RowIndex].Cells[e.ColumnIndex];

                switch (e.ColumnIndex)
                {
                    case 0:
                    {
                        cell.Value = TestData.FormatTimestamp(TestData.ValidateTimestamp(cell.FormattedValue));
                        break;
                    }

                    case 1:
                    case 3:
                    {
                        cell.Value = TestData.FormatValue(TestData.ValidateValue(cell.FormattedValue));
                        break;
                    }

                    case 2:
                    case 4:
                    {
                        cell.Value = TestData.FormatQuality(TestData.ValidateQuality(cell.FormattedValue));
                        break;
                    }
                }

                TestDataDV.Rows[e.RowIndex].ErrorText = String.Empty;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void TestDataDV_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            try
            {
                switch (e.ColumnIndex)
                {
                    case 0:
                    {
                        TestData.ValidateTimestamp(e.FormattedValue);
                        break;
                    }

                    case 1:
                    case 3:
                    {
                        TestData.ValidateValue(e.FormattedValue);
                        break;
                    }

                    case 2:
                    case 4:
                    {
                        TestData.ValidateQuality(e.FormattedValue);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                TestDataDV.Rows[e.RowIndex].ErrorText = exception.Message;
                e.Cancel = true;
            }
        }

        private void TestDataDV_RowValidated(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (m_loading)
                {
                    return;
                }

                TestData.DataValue dv = new TestData.DataValue();

                dv.Value = TestData.ValidateValue(TestDataDV.Rows[e.RowIndex].Cells[3].FormattedValue);
                dv.StatusCode = TestData.ValidateQuality(TestDataDV.Rows[e.RowIndex].Cells[4].FormattedValue);
                dv.SourceTimestamp = TestData.ValidateTimestamp(TestDataDV.Rows[e.RowIndex].Cells[0].FormattedValue);
                dv.Comment = TestDataDV.Rows[e.RowIndex].Cells[8].FormattedValue as string;
                
                // update the row state.
                string rowState = TestDataDV.Rows[e.RowIndex].Cells[7].FormattedValue as string;

                if (rowState == RowState.OK.ToString() || rowState == RowState.MissingExpected.ToString())
                {
                    rowState = RowState.OK.ToString();
 
                    string quality = TestDataDV.Rows[e.RowIndex].Cells[4].FormattedValue as string;

                    if (String.IsNullOrEmpty(quality))
                    {
                        rowState = RowState.MissingExpected.ToString();
                    }
                    else
                    {
                        if (StatusCode.IsNotBad(dv.StatusCode))
                        {
                            string value = TestDataDV.Rows[e.RowIndex].Cells[3].FormattedValue as string;

                            if (String.IsNullOrEmpty(value))
                            {
                                rowState = RowState.MissingExpected.ToString();
                            }
                        }
                    }

                    TestDataDV.Rows[e.RowIndex].Cells[7].Value = rowState;
                }

                UpdatesComplete();
            }
            catch (Exception exception)
            {
                TestDataDV.Rows[e.RowIndex].ErrorText = exception.Message;
            }
        }

        private void RunTestBTN_Click(object sender, EventArgs e)
        {
            try
            {
                DoTest();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void HistorianCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                m_dataset.AcceptChanges();
                string rawDataSetName = HistorianCB.SelectedItem as string;

                m_currentDataSet = m_testData.FindBestMatch(rawDataSetName, m_currentDataSet);

                if (m_currentDataSet == null)
                {
                    if (AggregateCB.SelectedIndex == -1)
                    {
                        ProcessedDataSetType[] datasets = m_testData.GetProcessedDataSets(rawDataSetName);

                        if (datasets != null && datasets.Length > 0)
                        {
                            m_currentDataSet = datasets[0];
                        }
                    }

                    if (m_currentDataSet == null)
                    {
                        m_currentDataSet = new ProcessedDataSetType();
                        SaveData(m_testData);
                        m_currentDataSet.DataSetName = rawDataSetName;
                        m_testData.AddDataSet(m_currentDataSet);
                    }

                }

                TestNameCB.Items.Clear();
                TestNameCB.Items.AddRange(m_testData.GetProcessedDataSets(rawDataSetName));
                TestNameCB.SelectedItem = m_currentDataSet;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void File_LoadMI_Click(object sender, EventArgs e)
        {
            try
            {
                XmlReader reader = XmlReader.Create("TestData.xml");
                XmlSerializer serializer = new XmlSerializer(typeof(TestData));
                m_testData = (TestData)serializer.Deserialize(reader);
                reader.Close();
                LoadData(m_testData);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void File_SaveMI_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.AcceptChanges();
                SaveData(m_testData);

                XmlWriter writer = XmlWriter.Create("TestData.xml", new XmlWriterSettings() { Indent = true, CloseOutput = true });
                XmlSerializer serializer = new XmlSerializer(typeof(TestData));
                serializer.Serialize(writer, m_testData);
                writer.Close();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void DeleteTestBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.Tables[0].Clear();

                if (TestNameCB.SelectedItem is ProcessedDataSetType)
                {
                    m_testData.RemoveDataSet(TestNameCB.SelectedItem as ProcessedDataSetType);
                    HistorianCB_SelectedIndexChanged(sender, e);
                }
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void TestNameCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            m_loading = true;

            try
            {
                m_dataset.Tables[0].Clear();
                m_currentDataSet = TestNameCB.SelectedItem as ProcessedDataSetType;

                if (m_currentDataSet == null)
                {
                    return;
                }

                AggregateCB.SelectedItem = m_currentDataSet.AggregateName;
                ProcessingIntervalNP.Value = m_currentDataSet.ProcessingInterval;
                SteppedCK.Checked = m_currentDataSet.Stepped;
                TreatUncertainAsBadCK.Checked = m_currentDataSet.TreatUncertainAsBad;
                PercentGoodNP.Value = m_currentDataSet.PercentGood;
                PercentBadNP.Value = m_currentDataSet.PercentBad;
                UseSlopedExtrapolationCK.Checked = m_currentDataSet.UseSlopedExtrapolation;

                AddDataSetToGrid(m_testData.GetRawValues(m_currentDataSet.DataSetName), true);
                AddDataSetToGrid(m_testData.GetProcessedValues(m_currentDataSet), false);

                UpdatesComplete();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
            finally
            {
                m_loading = false;
            }
        }

        private void SaveTestBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.AcceptChanges();
                SaveData(m_testData);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void CopyTestBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_currentDataSet = new ProcessedDataSetType();
                m_dataset.AcceptChanges();
                SaveData(m_testData);

                m_testData.AddDataSet(m_currentDataSet);
                TestNameCB.Items.Add(m_currentDataSet);
                TestNameCB.SelectedItem = m_currentDataSet;
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void CopyActualValuesBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_dataset.AcceptChanges();

                DataView view = new DataView(m_dataset.Tables[0], "RowState = 'Success' OR RowState = 'Failed'", "Timestamp", DataViewRowState.CurrentRows);

                foreach (DataRowView row in view)
                {
                    row[3] = row[5];
                    row[4] = row[6];
                    row[7] = RowState.OK.ToString();
                }

                m_dataset.AcceptChanges();
                UpdatesComplete();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void CopyToClipboard(StringBuilder buffer)
        {
            const string Version = "Version:1.0\r\n";
            const string StartHTML = "StartHTML:";
            const string EndHTML = "EndHTML:";
            const string StartFragment = "StartFragment:";
            const string EndFragment = "EndFragment:";
            const string DocType = "<!DOCTYPE>";
            const string HTMLIntro = "<HTML><BODY><!--StartFragment-->";
            const string HTMLExtro = "<!--EndFragment--></BODY></HTML>";
            const int NumberLengthAndCR = 10;

            int DescriptionLength = Version.Length + StartHTML.Length + EndHTML.Length + StartFragment.Length + EndFragment.Length + 4 * NumberLengthAndCR;
            int StartHTMLIndex = DescriptionLength;
            int StartFragmentIndex = StartHTMLIndex + DocType.Length + HTMLIntro.Length;
            int EndFragmentIndex = StartFragmentIndex + buffer.Length;
            int EndHTMLIndex = EndFragmentIndex + HTMLExtro.Length;

            StringBuilder buffer2 = new StringBuilder();

            buffer2.Append(Version);
            buffer2.Append(StartHTML);
            buffer2.AppendFormat("{0:D8}", StartHTMLIndex);
            buffer2.AppendFormat("\r\n");
            buffer2.Append(EndHTML);
            buffer2.AppendFormat("{0:D8}", EndHTMLIndex);
            buffer2.Append("\r\n");
            buffer2.Append(StartFragment);
            buffer2.AppendFormat("{0:D8}", StartFragmentIndex);
            buffer2.Append("\r\n");
            buffer2.Append(EndFragment);
            buffer2.AppendFormat("{0:D8}", EndFragmentIndex);
            buffer2.Append("\r\n");
            buffer2.Append(DocType);
            buffer2.Append(HTMLIntro);
            buffer2.Append(buffer.ToString());
            buffer2.Append(HTMLExtro);

            Clipboard.SetText(buffer2.ToString(), TextDataFormat.Html);
        }

        private void CopyToClipboardBTN_Click(object sender, EventArgs e)
        {
            try
            {
                StringBuilder buffer = new StringBuilder();
                buffer.Append("<table><tr>");
                buffer.Append("<td>Timestamp</td>");
                buffer.Append("<td>Value</td>");
                buffer.Append("<td>StatusCode</td>");
                buffer.Append("<td>Notes</td>");
                buffer.Append("</tr>");

                m_dataset.AcceptChanges();
                ResetRowState();

                DataView view = new DataView(m_dataset.Tables[0], "RowState = 'OK'", "Timestamp", DataViewRowState.CurrentRows);

                foreach (DataRowView row in view)
                {
                    buffer.Append("<tr>");
                    buffer.Append("<td>");
                    buffer.Append(row[0]);
                    buffer.Append("</td>");
                    buffer.Append("<td>");

                    Variant value = TestData.ValidateValue(row[3]);

                    if (value.TypeInfo != null)
                    {
                        if (value.TypeInfo.BuiltInType == BuiltInType.Double)
                        {
                            if (Math.Truncate((double)value.Value) == (double)value.Value)
                            {
                                buffer.AppendFormat("{0}", (long)(double)value.Value);
                            }
                            else
                            {
                                buffer.AppendFormat("{0:F3}", value.Value);
                            }
                        }
                        else
                        {
                            buffer.Append(value);
                        }
                    }

                    buffer.Append("</td>");
                    buffer.Append("<td>");
                    buffer.Append(row[4]);
                    buffer.Append("</td>");
                    buffer.Append("<td>");
                    buffer.Append(row[8]);
                    buffer.Append("</td>");
                    buffer.Append("</tr>");
                }

                buffer.Append("</table>");
                CopyToClipboard(buffer);
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void GenerateReportBTN_Click(object sender, EventArgs e)
        {
            try
            {
                GenerateReport();
            }
            catch (Exception exception)
            {
                ClientUtils.HandleException(this.Text, exception);
            }
        }

        private void ProcessingIntervalNP_ValueChanged(object sender, EventArgs e)
        {
            if (!m_loading)
            {
                DeleteValuesBTN_Click(sender, e);
            }
        }

        private void TimeFlowsBackwardsCK_CheckedChanged(object sender, EventArgs e)
        {
            if (!m_loading)
            {
                DeleteValuesBTN_Click(sender, e);
            }
        }
    }
}
