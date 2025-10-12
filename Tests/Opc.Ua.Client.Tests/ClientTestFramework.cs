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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using Microsoft.Extensions.Logging;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client test framework, common code to setup connections.
    /// </summary>
    public class ClientTestFramework
    {
        public static readonly object[] FixtureArgs =
        [
            new object[] { Utils.UriSchemeOpcTcp },
            new object[] { Utils.UriSchemeHttps },
            new object[] { Utils.UriSchemeOpcHttps }
        ];

        public const int MaxReferences = 100;
        public const int MaxTimeout = 10000;
        public const int TransportQuotaMaxMessageSize = 4 * 1024 * 1024;
        public const int TransportQuotaMaxStringLength = 1 * 1024 * 1024;
        public TokenValidatorMock TokenValidator { get; set; } = new TokenValidatorMock();
        public bool SingleSession { get; set; } = true;
        public int MaxChannelCount { get; set; } = 100;
        public bool SupportsExternalServerUrl { get; set; }
        public ServerFixture<ReferenceServer> ServerFixture { get; set; }
        public ClientFixture ClientFixture { get; set; }
        public ReferenceServer ReferenceServer { get; set; }
        public EndpointDescriptionCollection Endpoints { get; set; }
        public ReferenceDescriptionCollection ReferenceDescriptions { get; set; }
        public ISession Session { get; protected set; }
        public OperationLimits OperationLimits { get; private set; }
        public string UriScheme { get; }
        public string PkiRoot { get; set; }
        public Uri ServerUrl { get; private set; }
        public int ServerFixturePort { get; set; }
        public ExpandedNodeId[] TestSetStatic { get; private set; }
        public ExpandedNodeId[] TestSetSimulation { get; private set; }
        public ExpandedNodeId[] TestSetDataSimulation { get; }
        public ExpandedNodeId[] TestSetHistory { get; }
        public ITelemetryContext Telemetry { get; }

        private readonly ILogger<ClientTestFramework> m_logger;

        public ClientTestFramework(string uriScheme = Utils.UriSchemeOpcTcp, ITelemetryContext telemetry = null)
        {
            Telemetry = telemetry ?? NUnitTelemetryContext.Create();
            m_logger = Telemetry.CreateLogger<ClientTestFramework>();
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

        [DatapointSource]
        public static readonly string[] Policies =
        [
            .. SecurityPolicies.GetDisplayNames().Select(SecurityPolicies.GetUri)
        ];

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        public virtual Task OneTimeSetUpAsync()
        {
            return OneTimeSetUpCoreAsync();
        }

        /// <summary>
        /// Setup a server and client fixture.
        /// </summary>
        public virtual async Task OneTimeSetUpCoreAsync(
            bool securityNone = false,
            bool enableClientSideTracing = false,
            bool enableServerSideTracing = false,
            bool disableActivityLogging = false)
        {
            // pki directory root for test runs.
            PkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_logger.LogInformation("Using the Pki Root {FilePath}", PkiRoot);

            // The parameters are read from the .runsettings file
            string customUrl = null;
            if (SupportsExternalServerUrl)
            {
                customUrl = TestContext.Parameters["ServerUrl"];
                if (customUrl?.StartsWith(UriScheme, StringComparison.Ordinal) == true)
                {
                    m_logger.LogInformation("Using the external Server Url {Url}", customUrl);

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
                await CreateReferenceServerFixtureAsync(
                        enableServerSideTracing,
                        disableActivityLogging,
                        securityNone)
                    .ConfigureAwait(false);
            }

            ClientFixture = new ClientFixture(enableClientSideTracing, disableActivityLogging, Telemetry);

            await ClientFixture.LoadClientConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ClientFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ClientFixture.Config.TransportQuotas.MaxByteStringLength = ClientFixture
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;

            if (!string.IsNullOrEmpty(customUrl))
            {
                ServerUrl = new Uri(customUrl);
            }
            else
            {
                string url = UriScheme +
                    "://localhost:" +
                    ServerFixturePort.ToString(CultureInfo.InvariantCulture);

                if (UriScheme.StartsWith(Utils.UriSchemeHttp, StringComparison.Ordinal) ||
                    Utils.IsUriHttpsScheme(UriScheme))
                {
                    url += ConfiguredEndpoint.DiscoverySuffix;
                }

                ServerUrl = new Uri(url);
            }

            if (SingleSession)
            {
                try
                {
                    Session = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                        .ConfigureAwait(false);
                    Assert.NotNull(Session);
                    Session.ReturnDiagnostics = DiagnosticsMasks.All;
                }
                catch (Exception e)
                {
                    NUnit.Framework.Assert.Warn(
                        $"OneTimeSetup failed to create session with {ServerUrl}, tests fail. Error: {e.Message}");
                }
            }
        }

        public virtual async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
            // start Ref server
            ServerFixture = new ServerFixture<ReferenceServer>(
                enableTracing,
                disableActivityLogging,
                Telemetry)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = true,
                OperationLimits = true
            };

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = ServerFixture
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies
                .Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                });

            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384"
                });

            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate)
                {
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate)
                {
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate)
                {
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate)
                {
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384"
                });

            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken,
                    PolicyId = Profiles.JwtUserToken,
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken,
                    PolicyId = Profiles.JwtUserToken,
                    SecurityPolicyUri
                        = "http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP384r1"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken,
                    PolicyId = Profiles.JwtUserToken,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256"
                });
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken,
                    PolicyId = Profiles.JwtUserToken,
                    SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP384"
                });

            ServerFixture.Config.ServerConfiguration.MaxChannelCount = MaxChannelCount;
            ReferenceServer = await ServerFixture.StartAsync()
                .ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        public virtual async Task OneTimeTearDownAsync()
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

            // Clean up pki
            try
            {
                if (!string.IsNullOrEmpty(PkiRoot) && Directory.Exists(PkiRoot))
                {
                    Directory.Delete(PkiRoot, true);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        public virtual async Task SetUpAsync()
        {
            if (!SingleSession)
            {
                try
                {
                    Session = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    NUnit.Framework.Assert.Ignore(
                        $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Test Teardown.
        /// </summary>
        public virtual async Task TearDownAsync()
        {
            if (!SingleSession && Session != null)
            {
                await Session.CloseAsync().ConfigureAwait(false);
                Session.Dispose();
                Session = null;
            }
        }

        /// <summary>
        /// Return a test set of nodes with static character.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of static test nodes.</returns>
        public IList<NodeId> GetTestSetStatic(NamespaceTable namespaceUris)
        {
            return [.. TestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .Where(n => n != null)];
        }

        /// <summary>
        /// Return a test set of nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetSimulation(NamespaceTable namespaceUris)
        {
            return [.. TestSetSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .Where(n => n != null)];
        }

        /// <summary>
        /// Return a test set of nodes all nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetFullSimulation(NamespaceTable namespaceUris)
        {
            var simulation = TestSetSimulation
                .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .Where(n => n != null)
                .ToList();
            simulation.AddRange(
                TestSetDataSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                    .Where(n => n != null));
            return simulation;
        }

        /// <summary>
        /// Return a test data set of nodes with simulated values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of simulated test nodes.</returns>
        public IList<NodeId> GetTestSetDataSimulation(NamespaceTable namespaceUris)
        {
            return
            [
                .. TestSetDataSimulation.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                    .Where(n => n != null)
            ];
        }

        /// <summary>
        /// Return a test set of nodes with history values.
        /// </summary>
        /// <param name="namespaceUris">The namesapce table used in the session.</param>
        /// <returns>The list of test nodes.</returns>
        public IList<NodeId> GetTestSetHistory(NamespaceTable namespaceUris)
        {
            return [.. TestSetHistory.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                .Where(n => n != null)];
        }

        /// <summary>
        /// Enumerator for security policies.
        /// </summary>
        public IEnumerable<string> BenchPolicies()
        {
            return Policies;
        }

        /// <summary>
        /// Helper variable for benchmark.
        /// </summary>
        [ParamsSource(nameof(BenchPolicies))]
        public string SecurityPolicy = SecurityPolicies.None;

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        public virtual void GlobalSetup()
        {
            Console.WriteLine("GlobalSetup: Start Server");
            OneTimeSetUpCoreAsync().GetAwaiter().GetResult();
            Console.WriteLine("GlobalSetup: Connecting");
            Session = ClientFixture.ConnectAsync(ServerUrl, SecurityPolicy).GetAwaiter()
                .GetResult();
            Console.WriteLine("GlobalSetup: Ready");
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        public virtual void GlobalCleanup()
        {
            Console.WriteLine("GlobalCleanup: Disconnect and Stop Server");
            OneTimeTearDownAsync().GetAwaiter().GetResult();
            Console.WriteLine("GlobalCleanup: Done");
        }

        public async Task GetOperationLimitsAsync()
        {
            OperationLimits = new OperationLimits
            {
                MaxNodesPerRead = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead
                ).ConfigureAwait(false),
                MaxNodesPerHistoryReadData = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData
                ).ConfigureAwait(false),
                MaxNodesPerHistoryReadEvents = await GetOperationLimitValueAsync(
                    VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents
                ).ConfigureAwait(false),
                MaxNodesPerWrite = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite
                ).ConfigureAwait(false),
                MaxNodesPerHistoryUpdateData = await GetOperationLimitValueAsync(
                    VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData
                ).ConfigureAwait(false),
                MaxNodesPerHistoryUpdateEvents = await GetOperationLimitValueAsync(
                    VariableIds
                        .Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents
                ).ConfigureAwait(false),
                MaxNodesPerBrowse = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse
                ).ConfigureAwait(false),
                MaxMonitoredItemsPerCall = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall
                ).ConfigureAwait(false),
                MaxNodesPerNodeManagement = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement
                ).ConfigureAwait(false),
                MaxNodesPerRegisterNodes = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes
                ).ConfigureAwait(false),
                MaxNodesPerTranslateBrowsePathsToNodeIds = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds
                ).ConfigureAwait(false),
                MaxNodesPerMethodCall = await GetOperationLimitValueAsync(
                    VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall
                ).ConfigureAwait(false)
            };
        }

        public async Task<uint> GetOperationLimitValueAsync(NodeId nodeId)
        {
            try
            {
                return await Session.ReadValueAsync<uint>(nodeId).ConfigureAwait(false);
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
            writer.WriteLine(
                "CurrentPublishingEnabled: {0}",
                subscription.CurrentPublishingEnabled);
            writer.WriteLine("CurrentPriority         : {0}", subscription.CurrentPriority);
            writer.WriteLine("PublishTime             : {0}", subscription.PublishTime);
            writer.WriteLine("LastNotificationTime    : {0}", subscription.LastNotificationTime);
            writer.WriteLine("SequenceNumber          : {0}", subscription.SequenceNumber);
            writer.WriteLine("NotificationCount       : {0}", subscription.NotificationCount);
            writer.WriteLine("LastNotification        : {0}", subscription.LastNotification);
            writer.WriteLine("Notifications           : {0}", subscription.Notifications.Count());
            writer.WriteLine(
                "OutstandingMessageWorker: {0}",
                subscription.OutstandingMessageWorkers);
        }

        private static ExpandedNodeId[] ReadCustomTestSet(string param)
        {
            // load custom test sets
            string testSetParameter = TestContext.Parameters[param];
            string[] testSetParameters = testSetParameter.Split('#');
            if (testSetParameters != null)
            {
                // parse the custom content
                var testSet = new List<ExpandedNodeId>();
                foreach (string parameter in testSetParameters)
                {
                    testSet.Add(ExpandedNodeId.Parse(parameter));
                }
                return [.. testSet];
            }
            return [];
        }

        protected void SessionClosing(object sender, EventArgs e)
        {
            if (sender is ISession session)
            {
                TestContext.Out.WriteLine("Session_Closing: {0}", session.SessionId);
            }
        }
    }
}
