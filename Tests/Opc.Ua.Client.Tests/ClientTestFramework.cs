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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client test framework, common code to setup connections.
    /// </summary>
    public class ClientTestFramework
    {
        public static readonly object[] FixtureArgs = {
            new object [] { Utils.UriSchemeOpcTcp},
            new object [] { Utils.UriSchemeHttps}
        };

        public const int MaxReferences = 100;
        public const int MaxTimeout = 10000;
        public const int TransportQuota_MaxMessageSize = 4 * 1024 * 1024;
        public const int TransportQuota_MaxStringLength = 1 * 1024 * 1024;

        public bool SingleSession { get; set; } = true;

        protected ServerFixture<ReferenceServer> m_serverFixture;
        protected ClientFixture m_clientFixture;
        protected ReferenceServer m_server;
        protected EndpointDescriptionCollection m_endpoints;
        protected ReferenceDescriptionCollection m_referenceDescriptions;
        protected ISession m_session;
        protected OperationLimits m_operationLimits;
        protected string m_uriScheme;
        protected string m_pkiRoot;
        protected Uri m_url;

        public ClientTestFramework(string uriScheme = Utils.UriSchemeOpcTcp)
        {
            m_uriScheme = uriScheme;
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
        public async Task OneTimeSetUpAsync(TextWriter writer = null)
        {
            // pki directory root for test runs.
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // start Ref server
            m_serverFixture = new ServerFixture<ReferenceServer> {
                UriScheme = m_uriScheme,
                SecurityNone = true,
                AutoAccept = true,
                OperationLimits = true
            };

            if (writer != null)
            {
                m_serverFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
            }

            await m_serverFixture.LoadConfiguration(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.TransportQuotas.MaxMessageSize =
            m_serverFixture.Config.TransportQuotas.MaxBufferSize = TransportQuota_MaxMessageSize;
            m_serverFixture.Config.TransportQuotas.MaxByteStringLength =
            m_serverFixture.Config.TransportQuotas.MaxStringLength = TransportQuota_MaxStringLength;
            m_server = await m_serverFixture.StartAsync(writer ?? TestContext.Out).ConfigureAwait(false);

            m_clientFixture = new ClientFixture();
            await m_clientFixture.LoadClientConfiguration(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture.Config.TransportQuotas.MaxMessageSize =
            m_clientFixture.Config.TransportQuotas.MaxBufferSize = 4 * 1024 * 1024;
            m_clientFixture.Config.TransportQuotas.MaxByteStringLength =
            m_clientFixture.Config.TransportQuotas.MaxStringLength = 1 * 1024 * 1024;
            m_url = new Uri(m_uriScheme + "://localhost:" + m_serverFixture.Port.ToString());
            if (SingleSession)
            {
                try
                {
                    m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Assert.Ignore("OneTimeSetup failed to create session, tests skipped. Error: {0}", e.Message);
                }
            }
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                m_session.Close();
                m_session.Dispose();
                m_session = null;
            }
            await m_serverFixture.StopAsync().ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
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
                    m_session = await m_clientFixture.ConnectAsync(m_url, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Assert.Ignore("OneTimeSetup failed to create session, tests skipped. Error: {0}", e.Message);
                }
            }
            m_serverFixture.SetTraceOutput(TestContext.Out);
        }

        /// <summary>
        /// Test Teardown.
        /// </summary>
        public Task TearDown()
        {
            if (!SingleSession && m_session != null)
            {
                m_session.Close();
                m_session.Dispose();
                m_session = null;
            }
            return Task.CompletedTask;
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
            m_session = m_clientFixture.ConnectAsync(m_url, SecurityPolicy).GetAwaiter().GetResult();
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
            m_operationLimits = operationLimits;
        }

        public uint GetOperationLimitValue(NodeId nodeId)
        {
            try
            {
                return (uint)m_session.ReadValue(nodeId).Value;
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
    }
}
