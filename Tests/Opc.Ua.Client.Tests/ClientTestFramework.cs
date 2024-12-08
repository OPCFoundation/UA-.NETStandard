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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client test framework, common code to setup connections.
    /// </summary>
    public class ClientTestFramework
    {
        public static readonly object[] FixtureArgs = {
            new object [] { Utils.UriSchemeOpcTcp},
            new object [] { Utils.UriSchemeHttps},
            new object [] { Utils.UriSchemeOpcHttps},
        };

        public const int MaxReferences = 100;
        public const int MaxTimeout = 10000;
        public const int TransportQuotaMaxMessageSize = 4 * 1024 * 1024;
        public const int TransportQuotaMaxStringLength = 1 * 1024 * 1024;
        public TokenValidatorMock TokenValidator { get; set; } = new TokenValidatorMock();

        public bool SingleSession { get; set; } = true;
        public int MaxChannelCount { get; set; } = 10;
        public bool SupportsExternalServerUrl { get; set; } = false;
        public ServerFixture<ReferenceServer> ServerFixture { get; set; }
        public ClientFixture ClientFixture { get; set; }
        public ReferenceServer ReferenceServer { get; set; }
        public EndpointDescriptionCollection Endpoints { get; set; }
        public ReferenceDescriptionCollection ReferenceDescriptions { get; set; }
        public ISession Session { get; private set; }
        public OperationLimits OperationLimits { get; private set; }
        public string UriScheme { get; private set; }
        public string PkiRoot { get; set; }
        public Uri ServerUrl { get; private set; }
        public int ServerFixturePort { get; set; }
        public ExpandedNodeId[] TestSetStatic { get; private set; }
        public ExpandedNodeId[] TestSetSimulation { get; private set; }
        public ExpandedNodeId[] TestSetDataSimulation { get; private set; }
        public ExpandedNodeId[] TestSetHistory { get; private set; }
        public ClientTestFramework(string uriScheme = Utils.UriSchemeOpcTcp)
        {
            UriScheme = uriScheme;
            TestSetStatic = CommonTestWorkers.NodeIdTestSetStatic;
            TestSetSimulation = CommonTestWorkers.NodeIdTestSetSimulation;
            TestSetDataSimulation = CommonTestWorkers.NodeIdTestSetDataSimulation;
            TestSetHistory = CommonTestWorkers.NodeIdTestDataHistory;
        }
        public void InitializeSession(ISession session)
        {
            Session = session;
        }

        #region DataPointSources
        [DatapointSource]
        public static readonly string[] Policies = SecurityPolicies.GetDisplayNames()
            .Select(displayName => SecurityPolicies.GetUri(displayName)).ToArray();
        #endregion

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        public Task OneTimeSetUp()
        {
            return OneTimeSetUpAsync(null);
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        /// <param name="writer">The test output writer.</param>
        public async Task OneTimeSetUpAsync(TextWriter writer = null,
            bool securityNone = false,
            bool enableClientSideTracing = false,
            bool enableServerSideTracing = false,
            bool disableActivityLogging = false
            )
        {
            // pki directory root for test runs.
            PkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            TestContext.Out.WriteLine("Using the Pki Root {0}", PkiRoot);

            // The parameters are read from the .runsettings file
            string customUrl = null;
            if (SupportsExternalServerUrl)
            {
                customUrl = TestContext.Parameters["ServerUrl"];
                if (customUrl?.StartsWith(UriScheme, StringComparison.Ordinal) == true)
                {
                    TestContext.Out.WriteLine("Using the external Server Url {0}", customUrl);

                    // load custom test sets
                    TestSetStatic = ReadCustomTestSet("TestSetStatic");
                    TestSetSimulation = ReadCustomTestSet("TestSetSimulation");
                }
                else
                {
                    customUrl = null;
                }
            }

            if (customUrl == null)
            {
                await CreateReferenceServerFixture(enableServerSideTracing, disableActivityLogging, securityNone, writer).ConfigureAwait(false);
            }

            ClientFixture = new ClientFixture(enableClientSideTracing, disableActivityLogging);

            await ClientFixture.LoadClientConfiguration(PkiRoot).ConfigureAwait(false);
            ClientFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ClientFixture.Config.TransportQuotas.MaxByteStringLength =
            ClientFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;

            if (!string.IsNullOrEmpty(customUrl))
            {
                ServerUrl = new Uri(customUrl);
            }
            else
            {
                ServerUrl = new Uri(UriScheme + "://localhost:" + ServerFixturePort.ToString(CultureInfo.InvariantCulture));
            }

            if (SingleSession)
            {
                try
                {
                    Session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                    Assert.NotNull(Session);
                    Session.ReturnDiagnostics = DiagnosticsMasks.All;
                }
                catch (Exception e)
                {
                    Assert.Warn($"OneTimeSetup failed to create session with {ServerUrl}, tests fail. Error: {e.Message}");
                }
            }
        }

        virtual public async Task CreateReferenceServerFixture(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone,
            TextWriter writer)
        {
            {
                // start Ref server
                ServerFixture = new ServerFixture<ReferenceServer>(enableTracing, disableActivityLogging) {
                    UriScheme = UriScheme,
                    SecurityNone = securityNone,
                    AutoAccept = true,
                    AllNodeManagers = true,
                    OperationLimits = true
                };
            }

            if (writer != null)
            {
                ServerFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
            }

            await ServerFixture.LoadConfiguration(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength =
            ServerFixture.Config.TransportQuotas.MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Opc.Ua.Profiles.JwtUserToken });

            ReferenceServer = await ServerFixture.StartAsync(writer ?? TestContext.Out).ConfigureAwait(false);
            ReferenceServer.TokenValidator = this.TokenValidator;
            ServerFixturePort = ServerFixture.Port;
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        public async Task OneTimeTearDownAsync()
        {
            if (Session != null)
            {
                await Session.CloseAsync(5000, true).ConfigureAwait(false);
                Session.Dispose();
                Session = null;
            }
            if (ServerFixture != null)
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
            }
            Utils.SilentDispose(ClientFixture);
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        public async Task SetUp()
        {
            if (!SingleSession)
            {
                try
                {
                    Session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Assert.Ignore($"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
                }
            }
            if (ServerFixture == null)
            {
                ClientFixture.SetTraceOutput(TestContext.Out);
            }
            else
            {
                ServerFixture.SetTraceOutput(TestContext.Out);
            }
        }

        /// <summary>
        /// Test Teardown.
        /// </summary>
        public Task TearDown()
        {
            if (!SingleSession && Session != null)
            {
                Session.Close();
                Session.Dispose();
                Session = null;
            }
            return Task.CompletedTask;
        }
        #endregion

        #region Nodes Test Set
        /// <summary>
        /// Return a test set of nodes with static character.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of static test nodes.</returns>
        public IList<NodeId> GetTestSetStatic(NamespaceTable namespaceUris)
        {
            return TestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null).ToList();
        }

        /// <summary>
        /// Return a test set of nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetSimulation(NamespaceTable namespaceUris)
        {
            return TestSetSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null).ToList();
        }

        /// <summary>
        /// Return a test set of nodes all nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetFullSimulation(NamespaceTable namespaceUris)
        {
            var simulation = TestSetSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null).ToList();
            simulation.AddRange(TestSetDataSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null));
            return simulation;
        }

        /// <summary>
        /// Return a test data set of nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetDataSimulation(NamespaceTable namespaceUris)
        {
            return TestSetDataSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null).ToList();
        }

        /// <summary>
        /// Return a test set of nodes with history values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of test nodes.</returns>
        public IList<NodeId> GetTestSetHistory(NamespaceTable namespaceUris)
        {
            return TestSetHistory.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).Where(n => n != null).ToList();
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Enumerator for security policies.
        /// </summary>
        public IEnumerable<string> BenchPolicies() { return Policies; }

        /// <summary>
        /// Helper variable for benchmark.
        /// </summary>
        [ParamsSource(nameof(BenchPolicies))]
        public string SecurityPolicy = SecurityPolicies.None;

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        public void GlobalSetup()
        {
            Console.WriteLine("GlobalSetup: Start Server");
            OneTimeSetUpAsync(Console.Out).GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Connecting");
            Session = ClientFixture.ConnectAsync(ServerUrl, SecurityPolicy).GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Ready");
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        public void GlobalCleanup()
        {
            Console.WriteLine("GlobalCleanup: Disconnect and Stop Server");
            OneTimeTearDownAsync().GetAwaiter().GetResult();
            Console.WriteLine("GlobalCleanup: Done");
        }
        #endregion

        #region Public Methods
        public void GetOperationLimits()
        {
            var operationLimits = new OperationLimits() {
                MaxNodesPerRead = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead),
                MaxNodesPerHistoryReadData = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData),
                MaxNodesPerHistoryReadEvents = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents),
                MaxNodesPerWrite = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite),
                MaxNodesPerHistoryUpdateData = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData),
                MaxNodesPerHistoryUpdateEvents = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents),
                MaxNodesPerBrowse = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse),
                MaxMonitoredItemsPerCall = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall),
                MaxNodesPerNodeManagement = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement),
                MaxNodesPerRegisterNodes = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes),
                MaxNodesPerTranslateBrowsePathsToNodeIds = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds),
                MaxNodesPerMethodCall = GetOperationLimitValue(VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall)
            };
            OperationLimits = operationLimits;
        }

        public uint GetOperationLimitValue(NodeId nodeId)
        {
            try
            {
                return (uint)Session.ReadValue(nodeId).Value;
            }
            catch (ServiceResultException sre)
            {
                if (sre.StatusCode == StatusCodes.BadNodeIdUnknown)
                {
                    return 0;
                }
                throw;
            }
        }

        public void OutputSubscriptionInfo(TextWriter writer, Subscription subscription)
        {
            writer.WriteLine("Subscription            : {0}", subscription.DisplayName);
            writer.WriteLine("CurrentKeepAliveCount   : {0}", subscription.CurrentKeepAliveCount);
            writer.WriteLine("CurrentPublishingEnabled: {0}", subscription.CurrentPublishingEnabled);
            writer.WriteLine("CurrentPriority         : {0}", subscription.CurrentPriority);
            writer.WriteLine("PublishTime             : {0}", subscription.PublishTime);
            writer.WriteLine("LastNotificationTime    : {0}", subscription.LastNotificationTime);
            writer.WriteLine("SequenceNumber          : {0}", subscription.SequenceNumber);
            writer.WriteLine("NotificationCount       : {0}", subscription.NotificationCount);
            writer.WriteLine("LastNotification        : {0}", subscription.LastNotification);
            writer.WriteLine("Notifications           : {0}", subscription.Notifications.Count());
            writer.WriteLine("OutstandingMessageWorker: {0}", subscription.OutstandingMessageWorkers);
        }
        #endregion

        #region Private Methods
        private ExpandedNodeId[] ReadCustomTestSet(string param)
        {
            // load custom test sets
            var testSetParameter = TestContext.Parameters[param];
            var testSetParameters = testSetParameter.Split('#');
            if (testSetParameters != null)
            {
                // parse the custom content
                var testSet = new List<ExpandedNodeId>();
                foreach (var parameter in testSetParameters)
                {
                    testSet.Add(ExpandedNodeId.Parse(parameter));
                }
                return testSet.ToArray();
            }
            return Array.Empty<ExpandedNodeId>();
        }

        protected void Session_Closing(object sender, EventArgs e)
        {
            if (sender is ISession session)
            {
                TestContext.Out.WriteLine("Session_Closing: {0}", session.SessionId);
            }
        }
        #endregion
    }
}
