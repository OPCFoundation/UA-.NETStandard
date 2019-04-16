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
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Threading;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.IO;

using Opc.Ua.Client;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Sample.Controls
{
    /// <summary>
    /// Prompts the user to create a new secure channel.
    /// </summary>
    public partial class PerformanceTestDlg : Form
    {
        public PerformanceTestDlg()
        {
            InitializeComponent();
            this.Icon = ClientUtils.GetAppIcon();
        }

        private object m_lock = new object();
        private bool m_running;
        private ApplicationConfiguration m_configuration;
        private ConfiguredEndpointCollection m_endpoints;
        private ServiceMessageContext m_messageContext;
        private X509Certificate2 m_clientCertificate;
        private string m_filePath;
        
        /// <summary>
        /// Displays the dialog.
        /// </summary>
        public EndpointDescription ShowDialog(
            ApplicationConfiguration     configuration,
            ConfiguredEndpointCollection endpoints,
            X509Certificate2             clientCertificate)
        {
            m_configuration     = configuration;
            m_endpoints         = endpoints;
            m_messageContext    = configuration.CreateMessageContext();
            m_clientCertificate = clientCertificate;
            m_running           = false;
            m_filePath          = @".\perftest.csv";
                        
            EndpointSelectorCTRL.Initialize(m_endpoints, configuration);
            
            lock (m_lock)
            {
                OkBTN.Enabled = m_running = false;
            }

            // show dialog.
            if (ShowDialog() != DialogResult.OK)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Loads previously saved results.
        /// </summary>
        private void LoadResults(string filePath)
        {
            Stream istrm = File.OpenRead(filePath);
            DataContractSerializer serializer = new DataContractSerializer(typeof(PerformanceTestResult[]));
            PerformanceTestResult[] results = (PerformanceTestResult[])serializer.ReadObject(istrm);
            istrm.Close();
            
            ResultsCTRL.Clear();

            foreach (PerformanceTestResult result in results)
            {
                ResultsCTRL.Add(result);
            }

            m_filePath = filePath;
        }

        /// <summary>
        /// Saves the current results.
        /// </summary>
        private void SaveResults(string filePath)
        {
            PerformanceTestResult[] results = ResultsCTRL.GetResults();

            if (results.Length == 0)
            {
                return;
            }
                        
            Stream ostrm = File.Open(filePath, FileMode.Create);
            StreamWriter writer = new StreamWriter(ostrm);

            writer.Write("Url");
            writer.Write(",Protocol");
            writer.Write(",SecurityMode");
            writer.Write(",Algorithms");
            writer.Write(",Encoding");

            foreach (KeyValuePair<int,double> result in results[0].Results)
            {                    
                writer.Write(",{0} Values", result.Key);
            }

            writer.Write("\r\n");

            for (int ii = 0; ii < results.Length; ii++)
            {
                EndpointDescription endpoint = results[ii].Endpoint.Description;

                Uri uri = new Uri(endpoint.EndpointUrl);

                writer.Write("{0}", uri);
                writer.Write(",{0}", uri.Scheme);
                writer.Write(",{0}", endpoint.SecurityMode);
                writer.Write(",{0}", SecurityPolicies.GetDisplayName(endpoint.SecurityPolicyUri));
                writer.Write(",{0}", (results[ii].Endpoint.Configuration.UseBinaryEncoding)?"Binary":"XML");

                foreach (KeyValuePair<int,double> result in results[ii].Results)
                {                    
                    writer.Write(",{0}", result.Value);
                }

                writer.Write("\r\n");
            }

            writer.Close();            
            m_filePath = filePath;
        }

        /// <summary>
        /// Called when the test completes.
        /// </summary>
        public void TestComplete(object state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new WaitCallback(TestComplete), state);
                return;
            }

            try
            {                
                ProgressCTRL.Value = ProgressCTRL.Maximum;
                ResultsCTRL.Add((PerformanceTestResult)state);
            }                
            catch (Exception e)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), e);
            }
        }

        /// <summary>
        /// Called to indicate the test progress.
        /// </summary>
        private void TestProgress(object state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new WaitCallback(TestProgress), state);
                return;
            }

            try
            {
                ProgressCTRL.Value = (int)state;
            }                
            catch (Exception e)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), e);
            }
        }

        /// <summary>
        /// Called when the the test fails with an exception.
        /// </summary>
        private void TestException(object state)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new WaitCallback(TestException), state);
                return;
            }

            try
            {
                OkBTN.Enabled = m_running = false;
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), (Exception)state);                
            }                
            catch (Exception e)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), e);
            }
        }
        
        /// <summary>
        /// Runs all tests in a background thread.
        /// </summary>
        private void DoAllTests(object state)
        {
            for (int ii = 0; ii < m_endpoints.Count; ii++)
            {
                try
                {
                    DoTest(m_endpoints[ii]);
                }
                catch (Exception)
                {
                    // ignore.
                }
            }

            m_running = false;
        }

        /// <summary>
        /// Runs the test in a background thread.
        /// </summary>
        private void DoTest(object state)
        {
            try
            {
                DoTest((ConfiguredEndpoint)state);
            }
            catch (Exception e)
            {
                TestException(e);
            }

            m_running = false;
        }

        /// <summary>
        /// Runs the test in a background thread.
        /// </summary>
        private void DoTest(ConfiguredEndpoint endpoint)
        {
            PerformanceTestResult result = new PerformanceTestResult(endpoint, 100);

            result.Results.Add(1, -1);
            result.Results.Add(10, -1);
            result.Results.Add(50, -1);
            result.Results.Add(100, -1);
            result.Results.Add(250, -1);
            result.Results.Add(500, -1);

            try
            {
                // update the endpoint.
                if (endpoint.UpdateBeforeConnect)
                {
                    endpoint.UpdateFromServer();
                }

                SessionClient client = null;

                Uri url = new Uri(endpoint.Description.EndpointUrl);

                ITransportChannel channel = SessionChannel.Create(
                    m_configuration,
                    endpoint.Description,
                    endpoint.Configuration,
                    m_clientCertificate,
                    m_messageContext);

                client = new SessionClient(channel);

                List<int> requestSizes = new List<int>(result.Results.Keys);

                for (int ii = 0; ii < requestSizes.Count; ii++)
                {
                    // update the progress indicator.
                    TestProgress((ii * 100) / requestSizes.Count);

                    lock (m_lock)
                    {
                        if (!m_running)
                        {
                            break;
                        }
                    }

                    int count = requestSizes[ii];

                    // initialize request.
                    RequestHeader requestHeader = new RequestHeader();
                    requestHeader.ReturnDiagnostics = 5000;

                    ReadValueIdCollection nodesToRead = new ReadValueIdCollection(count);

                    for (int jj = 0; jj < count; jj++)
                    {
                        ReadValueId item = new ReadValueId();

                        item.NodeId = new NodeId((uint)jj, 1);
                        item.AttributeId = Attributes.Value;

                        nodesToRead.Add(item);
                    }

                    // ensure valid connection.
                    DataValueCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    client.Read(
                        requestHeader,
                        0,
                        TimestampsToReturn.Both,
                        nodesToRead,
                        out results,
                        out diagnosticInfos);

                    if (results.Count != count)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                    }

                    // do test.
                    DateTime start = DateTime.UtcNow;

                    for (int jj = 0; jj < result.Iterations; jj++)
                    {
                        client.Read(
                            requestHeader,
                            0,
                            TimestampsToReturn.Both,
                            nodesToRead,
                            out results,
                            out diagnosticInfos);

                        if (results.Count != count)
                        {
                            throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                        }
                    }

                    DateTime finish = DateTime.UtcNow;

                    long totalTicks = finish.Ticks - start.Ticks;
                    decimal averageMilliseconds = ((((decimal)totalTicks) / ((decimal)result.Iterations))) / ((decimal)TimeSpan.TicksPerMillisecond);
                    result.Results[requestSizes[ii]] = (double)averageMilliseconds;
                }
            }
            finally
            {
                TestComplete(result);
            }
        }

        private void EndpointSelectorCTRL_ConnectEndpoint(object sender, ConnectEndpointEventArgs e)
        {
            try
            {
                // check if a test is already running.
                lock (m_lock)
                {
                    if (m_running)
                    {
                        throw new InvalidOperationException("A test is already running.");
                    }
                }

                ConfiguredEndpoint endpoint = e.Endpoint;

                // start processing.
                OkBTN.Enabled = m_running = true;
                ThreadPool.QueueUserWorkItem(new WaitCallback(DoTest), endpoint);
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
                e.UpdateControl = false;
            }
        }

        private void OkBTN_Click(object sender, EventArgs e)
        {
            try
            {
                lock (m_lock)
                {
                    m_running = true;
                }
            }
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void SaveBTN_Click(object sender, EventArgs e)
        {
			try
			{
                FileInfo fileInfo = new FileInfo(m_filePath);

				SaveFileDialog dialog = new SaveFileDialog();

				dialog.CheckFileExists  = false;
				dialog.CheckPathExists  = true;
				dialog.DefaultExt       = ".csv";
				dialog.Filter           = "Result Files (*.csv)|*.csv|All Files (*.*)|*.*";
				dialog.ValidateNames    = true;
				dialog.Title            = "Save Performance Test Result File";
				dialog.FileName         = m_filePath;
                dialog.InitialDirectory = fileInfo.DirectoryName;
                dialog.RestoreDirectory = true;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

                SaveResults(dialog.FileName);
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void LoadBTN_Click(object sender, EventArgs e)
        {            
			try
			{
                FileInfo fileInfo = new FileInfo(m_filePath);

				OpenFileDialog dialog = new OpenFileDialog();

				dialog.CheckFileExists  = true;
				dialog.CheckPathExists  = true;
				dialog.DefaultExt       = ".csv";
				dialog.Filter           = "Result Files (*.csv)|*.csv|All Files (*.*)|*.*";
				dialog.Multiselect      = false;
				dialog.ValidateNames    = true;
				dialog.Title            = "Open Performance Test Result File";
				dialog.FileName         = m_filePath;
                dialog.InitialDirectory = fileInfo.DirectoryName;
                dialog.RestoreDirectory = true;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

                LoadResults(dialog.FileName);
			}
            catch (Exception exception)
            {
				GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }

        private void TestAllBTN_Click(object sender, EventArgs e)
        {
            try
            {
                if (m_running)
                {
                    throw new InvalidOperationException("A test is already running.");
                }

                ResultsCTRL.Clear();
                OkBTN.Enabled = m_running = true;
                ThreadPool.QueueUserWorkItem(new WaitCallback(DoAllTests), null);
            }
            catch (Exception exception)
            {
                GuiUtils.HandleException(this.Text, MethodBase.GetCurrentMethod(), exception);
            }
        }
    }

    /// <summary>
    /// The result of a performance test.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class PerformanceTestResult
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with the endpoint being tested an the number of iterations.
        /// </summary>
        public PerformanceTestResult(ConfiguredEndpoint endpoint, int iterations)
        {
            Initialize();

            m_endpoint   = endpoint;
            m_iterations = iterations;
        }

		/// <summary>
		/// Sets private members to default values.
		/// </summary>
		private void Initialize()
		{
            m_endpoint   = null;
            m_iterations = 0;
            m_results    = new Dictionary<int,double>();
		}

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The endpoint that was tested.
        /// </summary>
        [DataMember(Order = 1)]
        public ConfiguredEndpoint Endpoint
        {
            get { return m_endpoint; }
            private set { m_endpoint = value; }
        }

        /// <summary>
        /// The number of iterations for each payload size.
        /// </summary>
        [DataMember(Order = 2)]
        public int Iterations
        {
            get { return m_iterations; }
            private set { m_iterations = value; }
        }

        /// <summary>
        /// The test results returned as an list.
        /// </summary>
        [DataMember(Name = "Result", Order = 3)]
        private List<PerformanceTestResultItem> TestCaseResults
        {
            get
            { 
                List<PerformanceTestResultItem> results = new List<PerformanceTestResultItem>();

                foreach (KeyValuePair<int,double> entry in m_results)
                {
                    PerformanceTestResultItem item = new PerformanceTestResultItem();

                    item.Count   = entry.Key;
                    item.Average = entry.Value;

                    results.Add(item);
                }
                return results;
            }

            set 
            { 
                m_results.Clear();

                if (value != null)
                {
                    foreach (PerformanceTestResultItem item in value)
                    {
                        m_results[item.Count] = item.Average;
                    }
                }
            }
        }

        /// <summary>
        /// The average roundtrip time in milliseconds indexed by the payload size in bytes. 
        /// </summary>
        public IDictionary<int,double> Results
        {
            get { return m_results; }
        }
        #endregion
        
        #region Private Fields
        private ConfiguredEndpoint m_endpoint;
        private int m_iterations;
        private Dictionary<int,double> m_results;
        #endregion
    }

    /// <summary>
    /// The result of a performance test.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class PerformanceTestResultItem
    {
        #region Constructors
        /// <summary>
        /// Initializes with default values.
        /// </summary>
        public PerformanceTestResultItem()
        {
            Initialize();
        }

		/// <summary>
		/// Sets private members to default values.
		/// </summary>
		private void Initialize()
		{
            m_count   = 0;
            m_average = 0;
		}

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The number of items used in the test case.
        /// </summary>
        [DataMember(Order = 1)]
        public int Count
        {
            get { return m_count;  }
            set { m_count = value; }
        }

        /// <summary>
        /// The average response time in milliseconds.
        /// </summary>
        [DataMember(Order = 2)]
        public double Average
        {
            get { return m_average;  }
            set { m_average = value; }
        }
        #endregion
        
        #region Private Fields
        private int m_count;
        private double m_average;
        #endregion
    }
}
