/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Verifies that Runtime NodeSets composed before startup participate in the
    /// live NodeManager lifecycle and expose typed Method argument children.
    /// </summary>
    [TestFixture]
    [Category("NodeManagerLifecycle")]
    [Category("RuntimeNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class StartupRuntimeNodeSetLifecycleTests
    {
        private string m_pkiRoot;
        private ServerFixture<StartupRuntimeNodeSetServer> m_fixture;
        private StartupRuntimeNodeSetServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ILogger m_logger;

        /// <summary>
        /// Starts a server with two Runtime NodeSets registered before startup.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(StartupRuntimeNodeSetLifecycleTests),
                Guid.NewGuid().ToString("N"));

            m_fixture = new ServerFixture<StartupRuntimeNodeSetServer>(
                telemetry => new StartupRuntimeNodeSetServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_logger = NUnitTelemetryContext.Create()
                .CreateLogger<StartupRuntimeNodeSetLifecycleTests>();
            (m_requestHeader, m_secureChannelContext) = await m_server
                .CreateAndActivateSessionAsync(nameof(StartupRuntimeNodeSetLifecycleTests))
                .ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
        }

        /// <summary>
        /// Stops the server and removes its temporary PKI.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                m_requestHeader.Timestamp = DateTimeUtc.Now;
                await m_server
                    .CloseSessionAsync(
                        m_secureChannelContext,
                        m_requestHeader,
                        true,
                        RequestLifetime.None)
                    .ConfigureAwait(false);
            }

            m_server?.Dispose();
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            Assert.Multiple(() =>
            {
                Assert.That(
                    m_server.Probe.DeleteAddressSpaceCount,
                    Is.EqualTo(1));
                Assert.That(
                    m_server.Probe.DisposeCount,
                    Is.EqualTo(1));
            });
            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// A Method imported at startup must expose its typed argument properties
        /// and accept a Call carrying the declared input.
        /// </summary>
        [Test]
        [Order(100)]
        public async Task ImportedMethodAcceptsDeclaredArgumentsAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            NodeManagerRegistration registration = FindRegistration(
                StartupRuntimeNodeSetServer.PrimaryNamespaceUri);
            var manager = (RuntimeNodeSetNodeManager)registration.NodeManager;
            ushort namespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                StartupRuntimeNodeSetServer.PrimaryNamespaceUri);
            var rootId = new NodeId(
                StartupRuntimeNodeSetServer.PrimaryRootNodeId,
                namespaceIndex);
            var methodId = new NodeId(
                StartupRuntimeNodeSetServer.LoadMethodNodeId,
                namespaceIndex);
            MethodState method = manager.FindPredefinedNode<MethodState>(methodId);

            Assert.Multiple(() =>
            {
                Assert.That(method, Is.Not.Null);
                Assert.That(method.InputArguments, Is.Not.Null);
                Assert.That(method.OutputArguments, Is.Not.Null);
                Assert.That(method.InputArguments!.Value, Has.Count.EqualTo(1));
                Assert.That(method.InputArguments.Value[0].Name, Is.EqualTo("revision"));
                Assert.That(method.OutputArguments!.Value, Has.Count.EqualTo(1));
                Assert.That(method.OutputArguments.Value[0].Name, Is.EqualTo("accepted"));
            });

            ArrayOf<CallMethodRequest> methodsToCall =
            [
                new CallMethodRequest
                {
                    ObjectId = rootId,
                    MethodId = methodId,
                    InputArguments = [Variant.From("Rev1")]
                }
            ];
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            CallResponse response = await m_server
                .CallAsync(
                    m_secureChannelContext,
                    m_requestHeader,
                    methodsToCall,
                    RequestLifetime.None)
                .ConfigureAwait(false);

            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                methodsToCall);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                methodsToCall,
                response.ResponseHeader.StringTable,
                m_logger);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].OutputArguments, Has.Count.EqualTo(1));
            Assert.That(
                response.Results[0].OutputArguments[0].GetBoolean(),
                Is.True);
        }

        /// <summary>
        /// A factory that returns an already-live manager must be rejected
        /// without deleting or disposing the existing startup registration.
        /// </summary>
        [Test]
        [Order(150)]
        public async Task DuplicateFactoryOutputDoesNotDestroyStartupManagerAsync()
        {
            NodeManagerRegistration registration = FindRegistration(
                StartupRuntimeNodeSetServer.ProbeNamespaceUri);
            var factory = new ExistingNodeManagerFactory(
                registration.NodeManager,
                registration.NamespaceUris);

            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .AddAsync(factory, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<NodeManagerAlreadyRegisteredException>()
                    .With.Message.EqualTo("The NodeManager is already registered."));
            Assert.That(
                async () => await m_server.NodeManagerLifecycle
                    .ReloadAsync(registration, factory, null)
                    .ConfigureAwait(false),
                Throws.TypeOf<NodeManagerAlreadyRegisteredException>()
                    .With.Message.EqualTo("The NodeManager is already registered."));

            Assert.Multiple(() =>
            {
                Assert.That(m_server.Probe.DeleteAddressSpaceCount, Is.Zero);
                Assert.That(m_server.Probe.DisposeCount, Is.Zero);
                Assert.That(
                    FindRegistration(
                        StartupRuntimeNodeSetServer.ProbeNamespaceUri),
                    Is.SameAs(registration));
                Assert.That(
                    m_server.CurrentInstance.NodeManager.AsyncNodeManagers.Any(
                        manager => ReferenceEquals(
                            manager,
                            registration.NodeManager)),
                    Is.True);
            });
        }

        /// <summary>
        /// Startup registrations must support reload and removal without deleting
        /// external references contributed by another startup NodeManager.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task StartupRuntimeNodeSetCanReloadAndRemoveWithoutAffectingPeerAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            var master = (MasterNodeManager)server.NodeManager;
            ArrayOf<NodeManagerRegistration> registrations =
                m_server.NodeManagerLifecycle.Registrations;
            NodeManagerRegistration primary = FindRegistration(
                StartupRuntimeNodeSetServer.PrimaryNamespaceUri);
            NodeManagerRegistration peer = FindRegistration(
                StartupRuntimeNodeSetServer.PeerNamespaceUri);
            NodeManagerRegistration probe = FindRegistration(
                StartupRuntimeNodeSetServer.ProbeNamespaceUri);

            Assert.Multiple(() =>
            {
                Assert.That(primary.Generation, Is.EqualTo(1));
                Assert.That(peer.Generation, Is.EqualTo(1));
                Assert.That(probe.Generation, Is.EqualTo(1));
                Assert.That(probe.NodeManager, Is.InstanceOf<StartupProbeNodeManager>());
                Assert.That(
                    registrations.Find(registration =>
                        ReferenceEquals(
                            registration.NodeManager,
                            master.AsyncNodeManagers[0])),
                    Is.Null);
                Assert.That(
                    registrations.Find(registration =>
                        ReferenceEquals(
                            registration.NodeManager,
                            master.AsyncNodeManagers[1])),
                    Is.Null);
            });

            Assert.That(await ReadPrimaryValueAsync().ConfigureAwait(false), Is.EqualTo(1));
            AssertObjectsFolderContains(
                await BrowseObjectsFolderAsync().ConfigureAwait(false),
                expectedPrimary: true,
                expectedPeer: true);

            NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                .ReloadRuntimeNodeSetAsync(
                    primary,
                    StartupRuntimeNodeSetServer.CreatePrimaryOptions(generation: 2),
                    callerContext: null)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Id, Is.EqualTo(primary.Id));
                Assert.That(reloaded.Generation, Is.EqualTo(2));
            });
            Assert.That(await ReadPrimaryValueAsync().ConfigureAwait(false), Is.EqualTo(2));

            await m_server.NodeManagerLifecycle
                .RemoveAsync(reloaded, callerContext: null)
                .ConfigureAwait(false);

            ushort primaryNamespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                StartupRuntimeNodeSetServer.PrimaryNamespaceUri);
            NodeState removedRoot = await server.NodeManager
                .FindNodeInAddressSpaceAsync(
                    new NodeId(
                        StartupRuntimeNodeSetServer.PrimaryRootNodeId,
                        primaryNamespaceIndex))
                .ConfigureAwait(false);
            ArrayOf<NodeManagerRegistration> afterRemove =
                m_server.NodeManagerLifecycle.Registrations;

            Assert.Multiple(() =>
            {
                Assert.That(removedRoot, Is.Null);
                Assert.That(
                    afterRemove.Find(registration => registration.Id == reloaded.Id),
                    Is.Null);
                Assert.That(
                    afterRemove.Find(registration => registration.Id == peer.Id),
                    Is.Not.Null);
            });
            AssertObjectsFolderContains(
                await BrowseObjectsFolderAsync().ConfigureAwait(false),
                expectedPrimary: false,
                expectedPeer: true);
        }

        /// <summary>
        /// A failure after the address space starts but before startup commits
        /// must not expose disposed startup managers through the lifecycle.
        /// </summary>
        [Test]
        [Order(300)]
        public async Task FailedStartupPublishesNoLifecycleRegistrationsAsync()
        {
            // Leave room for certificate filenames within the .NET Framework path limit.
            string pkiRoot = Path.Combine(
                Path.GetTempPath(),
                "ua-startup-" + Guid.NewGuid().ToString("N"));
            FailingStartupRuntimeNodeSetServer failedServer = null;
            var fixture = new ServerFixture<FailingStartupRuntimeNodeSetServer>(
                telemetry =>
                {
                    failedServer = new FailingStartupRuntimeNodeSetServer(telemetry);
                    return failedServer;
                })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            try
            {
                Assert.That(
                    () => fixture.StartAsync(pkiRoot),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(failedServer, Is.Not.Null);
                Assert.That(
                    failedServer.NodeManagerLifecycle.Registrations,
                    Is.Empty);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                failedServer?.Dispose();
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// A stopped server must re-arm its lifecycle provider and adopt the
        /// newly created startup NodeManager generations on restart.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task ServerRestartRecreatesStartupRegistrationsAsync()
        {
            string pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(ServerRestartRecreatesStartupRegistrationsAsync),
                Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<StartupRuntimeNodeSetServer>(
                telemetry => new StartupRuntimeNodeSetServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            try
            {
                StartupRuntimeNodeSetServer server = await fixture
                    .StartAsync(pkiRoot)
                    .ConfigureAwait(false);
                var firstRegistrationIds = new HashSet<Guid>();
                server.NodeManagerLifecycle.Registrations.ForEach(
                    registration => firstRegistrationIds.Add(registration.Id));

                await server.StopAsync().ConfigureAwait(false);
                await server.StartAsync(fixture.Config).ConfigureAwait(false);

                ArrayOf<NodeManagerRegistration> restartedRegistrations =
                    server.NodeManagerLifecycle.Registrations;
                Assert.Multiple(() =>
                {
                    Assert.That(server.NodeManagerLifecycle.IsShuttingDown, Is.False);
                    Assert.That(
                        restartedRegistrations,
                        Has.Count.EqualTo(firstRegistrationIds.Count));
                    Assert.That(
                        restartedRegistrations.Find(registration =>
                            firstRegistrationIds.Contains(registration.Id)),
                        Is.Null);
                });
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        private NodeManagerRegistration FindRegistration(string namespaceUri)
        {
            return m_server.NodeManagerLifecycle.Registrations.Find(registration =>
                registration.NamespaceUris.Contains(namespaceUri)) ??
                throw new InvalidOperationException(
                    $"No lifecycle registration owns namespace '{namespaceUri}'.");
        }

        private async Task<int> ReadPrimaryValueAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort namespaceIndex = (ushort)server.NamespaceUris.GetIndex(
                StartupRuntimeNodeSetServer.PrimaryNamespaceUri);
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(
                    new NodeId(
                        StartupRuntimeNodeSetServer.PrimaryValueNodeId,
                        namespaceIndex))
                .ConfigureAwait(false);
            Assert.That(node, Is.InstanceOf<BaseVariableState>());
            return ((BaseVariableState)node).WrappedValue.GetInt32();
        }

        private async Task<BrowseResponse> BrowseObjectsFolderAsync()
        {
            var services = new ServerTestServices(
                m_server,
                m_secureChannelContext);
            var template = new BrowseDescription
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };
            ArrayOf<BrowseDescription> nodesToBrowse =
                ServerFixtureUtils.CreateBrowseDescriptionCollectionFromNodeId(
                    [ObjectIds.ObjectsFolder],
                    template);

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await services
                .BrowseAsync(
                    m_requestHeader,
                    view: null,
                    requestedMaxReferencesPerNode: 0,
                    nodesToBrowse)
                .ConfigureAwait(false);
            ServerFixtureUtils.ValidateResponse(
                response.ResponseHeader,
                response.Results,
                nodesToBrowse);
            ServerFixtureUtils.ValidateDiagnosticInfos(
                response.DiagnosticInfos,
                nodesToBrowse,
                response.ResponseHeader.StringTable,
                m_logger);
            return response;
        }

        private static void AssertObjectsFolderContains(
            BrowseResponse response,
            bool expectedPrimary,
            bool expectedPeer)
        {
            ArrayOf<ReferenceDescription> references = response.Results[0].References;
            Assert.Multiple(() =>
            {
                Assert.That(
                    references.Contains(reference =>
                        reference.BrowseName.Name ==
                            StartupRuntimeNodeSetServer.PrimaryRootBrowseName),
                    Is.EqualTo(expectedPrimary));
                Assert.That(
                    references.Contains(reference =>
                        reference.BrowseName.Name ==
                            StartupRuntimeNodeSetServer.PeerRootBrowseName),
                    Is.EqualTo(expectedPeer));
            });
        }
    }

    internal sealed class StartupRuntimeNodeSetServer : ReferenceServer
    {
        public const string PrimaryNamespaceUri =
            "urn:opcfoundation.org:Tests:StartupRuntimeNodeSet:Primary";
        public const string PeerNamespaceUri =
            "urn:opcfoundation.org:Tests:StartupRuntimeNodeSet:Peer";
        public const string ProbeNamespaceUri =
            "urn:opcfoundation.org:Tests:StartupRuntimeNodeSet:Probe";
        public const string PrimaryRootBrowseName = "StartupPrimary";
        public const string PeerRootBrowseName = "StartupPeer";
        public const uint PrimaryRootNodeId = 1000;
        public const uint PrimaryValueNodeId = 1001;
        public const uint LoadMethodNodeId = 1010;

        public StartupRuntimeNodeSetServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            Probe = new StartupNodeManagerProbe();
            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(
                CreatePrimaryOptions(generation: 1)));
            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(
                CreatePeerOptions()));
            AddNodeManager(new StartupProbeNodeManagerFactory(Probe));
        }

        public StartupNodeManagerProbe Probe { get; }

        public static RuntimeNodeSetOptions CreatePrimaryOptions(int generation)
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        $"StartupPrimary-{generation}",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(
                                Encoding.UTF8.GetBytes(BuildPrimaryNodeSetXml()))),
                        [PrimaryNamespaceUri])
                ],
                DefaultNamespaceUri = PrimaryNamespaceUri,
                Configure = builder =>
                {
                    builder.Variable<int>(
                        $"{PrimaryRootBrowseName}/Value").Node.Value = generation;
                    builder.Node(
                        $"{PrimaryRootBrowseName}/Load").OnCall(
                        static (
                            ISystemContext context,
                            MethodState method,
                            NodeId objectId,
                            ArrayOf<Variant> inputArguments,
                            List<Variant> outputArguments) =>
                        {
                            if (inputArguments.Count != 1 ||
                                !inputArguments[0].TryGetValue(out string revision) ||
                                string.IsNullOrEmpty(revision))
                            {
                                return StatusCodes.BadInvalidArgument;
                            }
                            outputArguments[0] = Variant.From(true);
                            return ServiceResult.Good;
                        });
                }
            };
        }

        private static RuntimeNodeSetOptions CreatePeerOptions()
        {
            return new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "StartupPeer",
                        _ => new ValueTask<Stream>(
                            new MemoryStream(
                                Encoding.UTF8.GetBytes(BuildPeerNodeSetXml()))),
                        [PeerNamespaceUri])
                ],
                DefaultNamespaceUri = PeerNamespaceUri
            };
        }

        private static string BuildPrimaryNodeSetXml()
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                           xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris>
                    <Uri>{PrimaryNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{PrimaryNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i={PrimaryRootNodeId}"
                            BrowseName="1:{PrimaryRootBrowseName}">
                    <DisplayName>{PrimaryRootBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35">ns=1;i={PrimaryValueNodeId}</Reference>
                      <Reference ReferenceType="i=47">ns=1;i={LoadMethodNodeId}</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i={PrimaryValueNodeId}" BrowseName="1:Value"
                              ParentNodeId="ns=1;i={PrimaryRootNodeId}" DataType="i=6"
                              ValueRank="-1" AccessLevel="3" UserAccessLevel="3">
                    <DisplayName>Value</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=63</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">ns=1;i={PrimaryRootNodeId}</Reference>
                    </References>
                  </UAVariable>
                  <UAMethod NodeId="ns=1;i={LoadMethodNodeId}" BrowseName="1:Load"
                            ParentNodeId="ns=1;i={PrimaryRootNodeId}">
                    <DisplayName>Load</DisplayName>
                    <References>
                      <Reference ReferenceType="i=46">ns=1;i=1011</Reference>
                      <Reference ReferenceType="i=46">ns=1;i=1012</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i={PrimaryRootNodeId}</Reference>
                    </References>
                  </UAMethod>
                  <UAVariable NodeId="ns=1;i=1011" BrowseName="InputArguments"
                              ParentNodeId="ns=1;i={LoadMethodNodeId}" DataType="i=296"
                              ValueRank="1" ArrayDimensions="1">
                    <DisplayName>InputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;i={LoadMethodNodeId}</Reference>
                    </References>
                    <Value>
                      <uax:ListOfExtensionObject>
                        <uax:ExtensionObject>
                          <uax:TypeId>
                            <uax:Identifier>i=297</uax:Identifier>
                          </uax:TypeId>
                          <uax:Body>
                            <uax:Argument>
                              <uax:Name>revision</uax:Name>
                              <uax:DataType>
                                <uax:Identifier>i=12</uax:Identifier>
                              </uax:DataType>
                              <uax:ValueRank>-1</uax:ValueRank>
                              <uax:ArrayDimensions />
                            </uax:Argument>
                          </uax:Body>
                        </uax:ExtensionObject>
                      </uax:ListOfExtensionObject>
                    </Value>
                  </UAVariable>
                  <UAVariable NodeId="ns=1;i=1012" BrowseName="OutputArguments"
                              ParentNodeId="ns=1;i={LoadMethodNodeId}" DataType="i=296"
                              ValueRank="1" ArrayDimensions="1">
                    <DisplayName>OutputArguments</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;i={LoadMethodNodeId}</Reference>
                    </References>
                    <Value>
                      <uax:ListOfExtensionObject>
                        <uax:ExtensionObject>
                          <uax:TypeId>
                            <uax:Identifier>i=297</uax:Identifier>
                          </uax:TypeId>
                          <uax:Body>
                            <uax:Argument>
                              <uax:Name>accepted</uax:Name>
                              <uax:DataType>
                                <uax:Identifier>i=1</uax:Identifier>
                              </uax:DataType>
                              <uax:ValueRank>-1</uax:ValueRank>
                              <uax:ArrayDimensions />
                            </uax:Argument>
                          </uax:Body>
                        </uax:ExtensionObject>
                      </uax:ListOfExtensionObject>
                    </Value>
                  </UAVariable>
                </UANodeSet>
                """;
        }

        private static string BuildPeerNodeSetXml()
        {
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris>
                    <Uri>{PeerNamespaceUri}</Uri>
                  </NamespaceUris>
                  <Models>
                    <Model ModelUri="{PeerNamespaceUri}" />
                  </Models>
                  <UAObject NodeId="ns=1;i=2000" BrowseName="1:{PeerRootBrowseName}">
                    <DisplayName>{PeerRootBrowseName}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=58</Reference>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                    </References>
                  </UAObject>
                </UANodeSet>
                """;
        }
    }

    internal sealed class StartupNodeManagerProbe
    {
        public int DeleteAddressSpaceCount =>
            Volatile.Read(ref m_deleteAddressSpaceCount);

        public int DisposeCount =>
            Volatile.Read(ref m_disposeCount);

        public void RecordDeleteAddressSpace()
        {
            Interlocked.Increment(ref m_deleteAddressSpaceCount);
        }

        public void RecordDispose()
        {
            Interlocked.Increment(ref m_disposeCount);
        }

        private int m_deleteAddressSpaceCount;
        private int m_disposeCount;
    }

    internal sealed class ExistingNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public ExistingNodeManagerFactory(
            IAsyncNodeManager nodeManager,
            ArrayOf<string> namespaceUris)
        {
            m_nodeManager = nodeManager;
            NamespacesUris = namespaceUris;
        }

        public ArrayOf<string> NamespacesUris { get; }

        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IAsyncNodeManager>(m_nodeManager);
        }

        private readonly IAsyncNodeManager m_nodeManager;
    }

    internal sealed class FailingStartupRuntimeNodeSetServer : ReferenceServer
    {
        public FailingStartupRuntimeNodeSetServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(
                StartupRuntimeNodeSetServer.CreatePrimaryOptions(generation: 1)));
        }

        protected override async ValueTask OnServerStartedAsync(
            CancellationToken cancellationToken = default)
        {
            await base.OnServerStartedAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Fail startup after the address space is ready.");
        }
    }

    internal sealed class StartupProbeNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public StartupProbeNodeManagerFactory(StartupNodeManagerProbe probe)
        {
            m_probe = probe;
        }

        public ArrayOf<string> NamespacesUris { get; } =
            [StartupRuntimeNodeSetServer.ProbeNamespaceUri];

        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<IAsyncNodeManager>(
                new StartupProbeNodeManager(
                    server,
                    configuration,
                    m_probe,
                    NamespacesUris[0]));
        }

        private readonly StartupNodeManagerProbe m_probe;
    }

    internal sealed class StartupProbeNodeManager : AsyncCustomNodeManager
    {
        public StartupProbeNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            StartupNodeManagerProbe probe,
            string namespaceUri)
            : base(server, configuration, namespaceUri)
        {
            m_probe = probe;
        }

        public override ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            m_probe.RecordDeleteAddressSpace();
            return base.DeleteAddressSpaceAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_probe.RecordDispose();
            }
            base.Dispose(disposing);
        }

        private readonly StartupNodeManagerProbe m_probe;
    }
}
