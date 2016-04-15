/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Client;
using Opc.Ua.Client.Controls;
using Windows.UI.Xaml.Controls;

namespace Opc.Ua.Sample.Controls
{
    /// <summary>
    /// Prompts the user to create a new secure channel.
    /// </summary>
    public partial class ReadHistoryDlg : Page
    {
        public ReadHistoryDlg()
        {
            InitializeComponent();

            ReadTypeCB.Items.Add(ReadType.Raw);
            ReadTypeCB.Items.Add(ReadType.Processed);
            ReadTypeCB.Items.Add(ReadType.Modified);
            ReadTypeCB.Items.Add(ReadType.AtTime);

            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Interpolative);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Average);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_TimeAverage);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Count);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Maximum);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Minimum);
            AggregateCB.Items.Add(BrowseNames.AggregateFunction_Total);         
        }

        private enum ReadType
        {
            Raw,
            Modified,
            AtTime,
            Processed
        }

        private Session m_session;
        private NodeId m_nodeId;
        private HistoryReadResult m_result;
        private int m_index;
        uint MaxReturnValues = 10;
        int ResampleInterval = 0;
        
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public bool ShowDialog(Session session, NodeId nodeId)
        {
            m_session = session;
            m_nodeId = nodeId;

            // update the title.
            string displayText = session.NodeCache.GetDisplayText(nodeId);

            if (!String.IsNullOrEmpty(displayText))
            {
                this.Name = Utils.Format("{0} [{1}]", this.Name, displayText);
            }

            // get the beginning of data.
            DateTime startTime;

            try
            {
                startTime = ReadFirstDate().ToLocalTime(); 
            }
            catch (Exception)
            {
                startTime = new DateTime(2000, 1, 1);
            }

            ReadTypeCB.SelectedItem = ReadType.Raw;
            StartTimeDP.Time = startTime.TimeOfDay;
            StartTimeCK.IsChecked = true;
            EndTimeDP.Time = DateTime.Now.TimeOfDay;
            EndTimeCK.IsChecked = true;
            
            MaxReturnValuesCK.IsChecked = true;
            ReturnBoundsCK.IsChecked = true;
            AggregateCB.SelectedItem = BrowseNames.AggregateFunction_Average;
            
            GoBTN.Visibility  = Windows.UI.Xaml.Visibility.Visible;
            NextBTN.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            StopBTN.IsEnabled = false;

            return true;
        }

        private void ShowResults()
        {
            GoBTN.Visibility = (m_result == null || m_result.ContinuationPoint == null || m_result.ContinuationPoint.Length == 0)? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
            NextBTN.Visibility = (GoBTN.Visibility == Windows.UI.Xaml.Visibility.Collapsed)? Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;
            StopBTN.IsEnabled = (m_result != null && m_result.ContinuationPoint != null && m_result.ContinuationPoint.Length > 0);

            if (m_result == null)
            {
                return;
            }

            HistoryData results = ExtensionObject.ToEncodeable(m_result.HistoryData) as HistoryData;

            if (results == null)
            {
                return;
            }

            for (int ii = 0; ii < results.DataValues.Count; ii++)
            {
                StatusCode status = results.DataValues[ii].StatusCode;

                string index = Utils.Format("[{0}]", m_index++);
                string timestamp = results.DataValues[ii].SourceTimestamp.ToLocalTime().ToString("yyyy-MM-dd hh:mm:ss");
                string value = Utils.Format("{0}", results.DataValues[ii].WrappedValue);
                string quality = Utils.Format("{0}", (StatusCode)status.CodeBits);
                string historyInfo = Utils.Format("{0:X2}", (int)status.AggregateBits);

                ListViewItem item = new ListViewItem();
                item.Name = index;

                ResultsLV.Items.Add(item);
            }
        }
        
        private void ReleaseContinuationPoints()
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            if (m_result != null)
            {
                nodeToRead.ContinuationPoint = m_result.ContinuationPoint;
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                true,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            m_result = null;

            ShowResults();
        }

        private DateTime ReadFirstDate()
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime = new DateTime(1970, 1, 1);
            details.EndTime = DateTime.UtcNow.AddDays(1);
            details.IsReadModified = false;
            details.NumValuesPerNode = 1;
            details.ReturnBounds = false;

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                return DateTime.MinValue;
            }

            HistoryData data = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;

            if (results == null)
            {
                return DateTime.MinValue;
            }

            DateTime startTime = data.DataValues[0].SourceTimestamp;

            if (results[0].ContinuationPoint != null && results[0].ContinuationPoint.Length > 0)
            {
                nodeToRead.ContinuationPoint = results[0].ContinuationPoint;

                m_session.HistoryRead(
                    null,
                    new ExtensionObject(details),
                    TimestampsToReturn.Source,
                    true,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                Session.ValidateResponse(results, nodesToRead);
                Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }

            return startTime;
        }

        private void ReadRaw(bool isReadModified)
        {
            ReadRawModifiedDetails details = new ReadRawModifiedDetails();
            details.StartTime = DateTime.MinValue;
            details.EndTime = DateTime.MinValue;
            details.IsReadModified = isReadModified;
            details.NumValuesPerNode = 0;
            details.ReturnBounds = (ReturnBoundsCK.IsChecked == true);

            if (StartTimeCK.IsChecked == true)
            {
                details.StartTime = new DateTime(DateTime.Now.Year,
                                                 DateTime.Now.Month,
                                                 DateTime.Now.Day,
                                                 StartTimeDP.Time.Hours,
                                                 StartTimeDP.Time.Minutes,
                                                 StartTimeDP.Time.Seconds);
            }

            if (EndTimeCK.IsChecked == true)
            {
                details.EndTime = new DateTime(DateTime.Now.Year,
                                                 DateTime.Now.Month,
                                                 DateTime.Now.Day,
                                                 EndTimeDP.Time.Hours,
                                                 EndTimeDP.Time.Minutes,
                                                 EndTimeDP.Time.Seconds);
            }

            if (MaxReturnValuesCK.IsChecked == true)
            {
                details.NumValuesPerNode = MaxReturnValues;
            }

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            if (m_result != null)
            {
                nodeToRead.ContinuationPoint = m_result.ContinuationPoint;
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            
            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            m_result = results[0];
       
            ShowResults();
        }

        private void ReadAtTime()
        {
        }

        private void ReadProcessed()
        {
            ReadProcessedDetails details = new ReadProcessedDetails();
            details.StartTime = new DateTime(DateTime.Now.Year,
                                             DateTime.Now.Month,
                                             DateTime.Now.Day,
                                             StartTimeDP.Time.Hours,
                                             StartTimeDP.Time.Minutes,
                                             StartTimeDP.Time.Seconds);
            details.EndTime = new DateTime(DateTime.Now.Year,
                                             DateTime.Now.Month,
                                             DateTime.Now.Day,
                                             EndTimeDP.Time.Hours,
                                             EndTimeDP.Time.Minutes,
                                             EndTimeDP.Time.Seconds);
            details.ProcessingInterval = (double)ResampleInterval;

            NodeId aggregateId = null;

            switch ((string)AggregateCB.SelectedItem)
            {
                case BrowseNames.AggregateFunction_Interpolative: { aggregateId = ObjectIds.AggregateFunction_Interpolative; break; }
                case BrowseNames.AggregateFunction_TimeAverage: { aggregateId = ObjectIds.AggregateFunction_TimeAverage; break; }
                case BrowseNames.AggregateFunction_Average: { aggregateId = ObjectIds.AggregateFunction_Average; break; }
                case BrowseNames.AggregateFunction_Count: { aggregateId = ObjectIds.AggregateFunction_Count; break; }
                case BrowseNames.AggregateFunction_Maximum: { aggregateId = ObjectIds.AggregateFunction_Maximum; break; }
                case BrowseNames.AggregateFunction_Minimum: { aggregateId = ObjectIds.AggregateFunction_Minimum; break; }
                case BrowseNames.AggregateFunction_Total: { aggregateId = ObjectIds.AggregateFunction_Total; break; }
            }

            details.AggregateType.Add(aggregateId);

            HistoryReadValueId nodeToRead = new HistoryReadValueId();
            nodeToRead.NodeId = m_nodeId;

            if (m_result != null)
            {
                nodeToRead.ContinuationPoint = m_result.ContinuationPoint;
            }

            HistoryReadValueIdCollection nodesToRead = new HistoryReadValueIdCollection();
            nodesToRead.Add(nodeToRead);

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            m_session.HistoryRead(
                null,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                false,
                nodesToRead,
                out results,
                out diagnosticInfos);

            Session.ValidateResponse(results, nodesToRead);
            Session.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw new ServiceResultException(results[0].StatusCode);
            }

            m_result = results[0];

            ShowResults();
        }

        private void Read()
        {
            switch ((ReadType)ReadTypeCB.SelectedItem)
            {
                case ReadType.Raw:
                {
                    ReadRaw(false);
                    break;
                }

                case ReadType.Modified:
                {
                    ReadRaw(true);
                    break;
                }

                case ReadType.AtTime:
                {
                    ReadAtTime();
                    break;
                }

                case ReadType.Processed:
                {
                    ReadProcessed();
                    break;
                }
            }
        }

        private void GoBTN_Click(object sender, EventArgs e)
        {
            try
            {
                m_index = 0;
                ResultsLV.Items.Clear();
                m_result = null;

                Read();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void NextBTN_Click(object sender, EventArgs e)
        {
            try
            {
                Read();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void StopBTN_Click(object sender, EventArgs e)
        {
            try
            {
                ReleaseContinuationPoints();
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(String.Empty, GuiUtils.CallerName(), exception);
            }
        }

        private void ReadTypeCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ReleaseContinuationPoints();
            }
            catch (Exception)
            {
                // ignore is ok.
            }

            switch ((ReadType)ReadTypeCB.SelectedItem)
            {
                case ReadType.Raw:
                {
                    ReturnBoundsCK.IsEnabled = true;
                    AggregateCB.IsEnabled = false;
                    StartTimeCK.IsEnabled = true;
                    EndTimeCK.IsEnabled = true;
                    MaxReturnValuesCK.IsChecked = true;
                    MaxReturnValuesCK.IsEnabled = true;
                    break;
                }

                case ReadType.Modified:
                {
                    ReturnBoundsCK.IsEnabled = false;
                    AggregateCB.IsEnabled = false;
                    StartTimeCK.IsEnabled = true;
                    EndTimeCK.IsEnabled = true;
                    MaxReturnValuesCK.IsChecked = true;
                    MaxReturnValuesCK.IsEnabled = true;
                    break;
                }

                case ReadType.AtTime:
                {
                    ReturnBoundsCK.IsEnabled = false;
                    AggregateCB.IsEnabled = false;
                    StartTimeCK.IsEnabled = true;
                    EndTimeCK.IsChecked = false;
                    EndTimeCK.IsEnabled = false;
                    MaxReturnValuesCK.IsChecked = true;
                    MaxReturnValuesCK.IsEnabled = false;
                    break;
                }

                case ReadType.Processed:
                {
                    ReturnBoundsCK.IsEnabled = false;
                    AggregateCB.IsEnabled = true;
                    StartTimeCK.IsChecked = true;
                    StartTimeCK.IsEnabled = false;
                    EndTimeCK.IsChecked = true;
                    EndTimeCK.IsEnabled = false;
                    MaxReturnValuesCK.IsChecked = false;
                    MaxReturnValuesCK.IsEnabled = false;
                    break;
                }
            }
        }

        private void StartTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            StartTimeDP.IsEnabled = (StartTimeCK.IsChecked == true)? true : false;
        }

        private void EndTimeCK_CheckedChanged(object sender, EventArgs e)
        {
            EndTimeDP.IsEnabled = (EndTimeCK.IsChecked == true)? true : false;
        }
    }
}
