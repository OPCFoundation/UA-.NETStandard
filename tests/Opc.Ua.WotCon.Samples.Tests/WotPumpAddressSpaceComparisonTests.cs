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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AggregationClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Server.Fluent;
using Pumps;

namespace Opc.Ua.WotCon.Samples.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [Category("Samples")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class WotPumpAddressSpaceComparisonTests
    {
        [Test]
        public async Task WotPumpInstanceMatchesNativePumpSubsetAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment wotEnvironment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable wotEnvironmentLifetime =
                wotEnvironment.ConfigureAwait(false);
            NativePumpEnvironment nativeEnvironment = await NativePumpEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable nativeEnvironmentLifetime =
                nativeEnvironment.ConfigureAwait(false);

            _ = await AggregationClientRunner
                .RunAsync(wotEnvironment.ClientOptions, timeout.Token)
                .ConfigureAwait(false);

            WotClientConnection wotConnection = await wotEnvironment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable wotConnectionLifetime =
                wotConnection.ConfigureAwait(false);
            ManagedSessionConnection nativeConnection = await nativeEnvironment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable nativeConnectionLifetime =
                nativeConnection.ConfigureAwait(false);

            ushort wotPumpNs = ResolveNamespace(wotConnection.Session, kWotPumpNamespaceUri);
            var wotRoot = new NodeId("Pump1", wotPumpNs);
            var nativeRoot = new NodeId("5001_Pump_1", 1);
            AddressSpaceTree wotTree = await CaptureTreeAsync(
                wotConnection.Session,
                wotRoot,
                "Pump1",
                timeout.Token).ConfigureAwait(false);
            AddressSpaceTree nativeTree = await CaptureTreeAsync(
                nativeConnection.Session,
                nativeRoot,
                "Pump_1",
                timeout.Token).ConfigureAwait(false);

            WriteNativeOnlyNodes(wotTree, nativeTree);
            string failures = CompareWotSubset(wotTree, nativeTree);
            Assert.That(failures, Is.Empty, failures);
        }

        [Test]
        public async Task CompanionTypeDefinitionsMatchNativePumpServerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            WotSampleEnvironment wotEnvironment = await WotSampleEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable wotEnvironmentLifetime =
                wotEnvironment.ConfigureAwait(false);
            NativePumpEnvironment nativeEnvironment = await NativePumpEnvironment
                .StartAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable nativeEnvironmentLifetime =
                nativeEnvironment.ConfigureAwait(false);

            _ = await AggregationClientRunner
                .RunAsync(wotEnvironment.ClientOptions, timeout.Token)
                .ConfigureAwait(false);

            WotClientConnection wotConnection = await wotEnvironment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable wotConnectionLifetime =
                wotConnection.ConfigureAwait(false);
            ManagedSessionConnection nativeConnection = await nativeEnvironment
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable nativeConnectionLifetime =
                nativeConnection.ConfigureAwait(false);

            Dictionary<TypeKey, TypeDefinitionInfo> wotTypes = await CaptureCompanionTypesAsync(
                wotConnection.Session,
                timeout.Token).ConfigureAwait(false);
            Dictionary<TypeKey, TypeDefinitionInfo> nativeTypes = await CaptureCompanionTypesAsync(
                nativeConnection.Session,
                timeout.Token).ConfigureAwait(false);

            string failures = CompareTypeDefinitions(wotTypes, nativeTypes);
            Assert.That(failures, Is.Empty, failures);
        }

        private static ushort ResolveNamespace(ManagedSession session, string namespaceUri)
        {
            int namespaceIndex = session.NamespaceUris.GetIndex(namespaceUri);
            Assert.That(
                namespaceIndex,
                Is.GreaterThan(0),
                $"The namespace '{namespaceUri}' must exist.");
            return checked((ushort)namespaceIndex);
        }

        private static async Task<AddressSpaceTree> CaptureTreeAsync(
            ManagedSession session,
            NodeId rootNodeId,
            string rootName,
            CancellationToken cancellationToken)
        {
            var nodes = new Dictionary<string, StructuralNode>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            await CaptureTreeNodeAsync(
                session,
                rootNodeId,
                rootName,
                string.Empty,
                depth: 0,
                nodes,
                visited,
                cancellationToken).ConfigureAwait(false);
            return new AddressSpaceTree(rootName, nodes);
        }

        private static async Task CaptureTreeNodeAsync(
            ManagedSession session,
            NodeId nodeId,
            string displayPath,
            string relativePath,
            int depth,
            Dictionary<string, StructuralNode> nodes,
            HashSet<string> visited,
            CancellationToken cancellationToken)
        {
            if (depth > kMaxTreeDepth)
            {
                return;
            }

            string visitKey = nodeId.ToString();
            if (!visited.Add(visitKey))
            {
                return;
            }

            NodeAttributes attributes = await ReadNodeAttributesAsync(
                session,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            TypeKey? typeDefinition = await ReadTypeDefinitionAsync(
                session,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            nodes.Add(
                relativePath,
                new StructuralNode(displayPath, attributes.BrowseName, attributes.NodeClass, typeDefinition));
            if (depth == kMaxTreeDepth)
            {
                return;
            }

            ArrayOf<ReferenceDescription> references = await BrowseHierarchicalReferencesAsync(
                session,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            ReferenceDescription[] orderedReferences = references.ToArray() ?? [];
            foreach (ReferenceDescription reference in orderedReferences.OrderBy(
                r => FormatBrowseName(ToBrowseNameKey(session, r.BrowseName)),
                StringComparer.Ordinal))
            {
                NodeId childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (childNodeId.IsNull)
                {
                    continue;
                }

                BrowseNameKey childName = ToBrowseNameKey(session, reference.BrowseName);
                string childRelativePath = relativePath.Length == 0
                    ? FormatBrowseName(childName)
                    : relativePath + "/" + FormatBrowseName(childName);
                string childDisplayPath = displayPath + "/" + FormatBrowseName(childName);
                await CaptureTreeNodeAsync(
                    session,
                    childNodeId,
                    childDisplayPath,
                    childRelativePath,
                    depth + 1,
                    nodes,
                    visited,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<Dictionary<TypeKey, TypeDefinitionInfo>> CaptureCompanionTypesAsync(
            ManagedSession session,
            CancellationToken cancellationToken)
        {
            var types = new Dictionary<TypeKey, TypeDefinitionInfo>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            await CaptureTypeTreeAsync(
                session,
                Opc.Ua.ObjectIds.ObjectTypesFolder,
                expectedNodeClass: NodeClass.ObjectType,
                depth: 0,
                types,
                visited,
                cancellationToken).ConfigureAwait(false);
            await CaptureTypeTreeAsync(
                session,
                Opc.Ua.ObjectIds.VariableTypesFolder,
                expectedNodeClass: NodeClass.VariableType,
                depth: 0,
                types,
                visited,
                cancellationToken).ConfigureAwait(false);
            return types;
        }

        private static async Task CaptureTypeTreeAsync(
            ManagedSession session,
            NodeId nodeId,
            NodeClass expectedNodeClass,
            int depth,
            Dictionary<TypeKey, TypeDefinitionInfo> types,
            HashSet<string> visited,
            CancellationToken cancellationToken)
        {
            if (depth > kMaxTypeDepth)
            {
                return;
            }

            string visitKey = expectedNodeClass.ToString() + ":" + nodeId;
            if (!visited.Add(visitKey))
            {
                return;
            }

            ArrayOf<ReferenceDescription> references = await BrowseHierarchicalReferencesAsync(
                session,
                nodeId,
                cancellationToken).ConfigureAwait(false);
            ReferenceDescription[] referenceArray = references.ToArray() ?? [];
            foreach (ReferenceDescription reference in referenceArray)
            {
                NodeId childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (childNodeId.IsNull)
                {
                    continue;
                }

                BrowseNameKey childBrowseName = ToBrowseNameKey(session, reference.BrowseName);
                if (reference.NodeClass == expectedNodeClass &&
                    s_companionNamespaceUris.Contains(childBrowseName.NamespaceUri))
                {
                    var key = new TypeKey(childBrowseName.NamespaceUri, childBrowseName.Name);
                    TypeKey? superType = await ReadSuperTypeAsync(
                        session,
                        childNodeId,
                        cancellationToken).ConfigureAwait(false);
                    types.Add(key, new TypeDefinitionInfo(key, expectedNodeClass, superType));
                }

                await CaptureTypeTreeAsync(
                    session,
                    childNodeId,
                    expectedNodeClass,
                    depth + 1,
                    types,
                    visited,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseHierarchicalReferencesAsync(
            ManagedSession session,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                nodeClassMask: 0,
                cancellationToken).ConfigureAwait(false);
            return references;
        }

        private static async Task<NodeAttributes> ReadNodeAttributesAsync(
            ManagedSession session,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.NodeClass }
            ];
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].WrappedValue.TryGetValue(out QualifiedName browseName), Is.True);
            Assert.That(response.Results[1].WrappedValue.TryGetValue(out NodeClass nodeClass), Is.True);
            return new NodeAttributes(ToBrowseNameKey(session, browseName), nodeClass);
        }

        private static async Task<TypeKey?> ReadTypeDefinitionAsync(
            ManagedSession session,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                nodeId,
                maxResultsToReturn: 1,
                BrowseDirection.Forward,
                Opc.Ua.ReferenceTypeIds.HasTypeDefinition,
                includeSubtypes: false,
                nodeClassMask: (uint)NodeClass.ObjectType | (uint)NodeClass.VariableType,
                cancellationToken).ConfigureAwait(false);
            if (references.Count == 0)
            {
                return null;
            }

            BrowseNameKey browseName = ToBrowseNameKey(session, references[0].BrowseName);
            return new TypeKey(browseName.NamespaceUri, browseName.Name);
        }

        private static async Task<TypeKey?> ReadSuperTypeAsync(
            ManagedSession session,
            NodeId typeNodeId,
            CancellationToken cancellationToken)
        {
            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                typeNodeId,
                maxResultsToReturn: 1,
                BrowseDirection.Inverse,
                Opc.Ua.ReferenceTypeIds.HasSubtype,
                includeSubtypes: false,
                nodeClassMask: (uint)NodeClass.ObjectType | (uint)NodeClass.VariableType,
                cancellationToken).ConfigureAwait(false);
            if (references.Count == 0)
            {
                return null;
            }

            BrowseNameKey browseName = ToBrowseNameKey(session, references[0].BrowseName);
            return new TypeKey(browseName.NamespaceUri, browseName.Name);
        }

        private static BrowseNameKey ToBrowseNameKey(ManagedSession session, QualifiedName browseName)
        {
            string namespaceUri = session.NamespaceUris.GetString(browseName.NamespaceIndex) ?? string.Empty;
            return new BrowseNameKey(namespaceUri, browseName.Name ?? string.Empty);
        }

        private static string CompareWotSubset(AddressSpaceTree wotTree, AddressSpaceTree nativeTree)
        {
            var failures = new StringBuilder();
            foreach ((string path, StructuralNode wotNode) in wotTree.Nodes.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal))
            {
                if (!nativeTree.Nodes.TryGetValue(path, out StructuralNode? nativeNode))
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{wotNode.DisplayPath} missing from native Pump_1.");
                    failures.AppendLine();
                    continue;
                }

                if (path.Length != 0 && wotNode.BrowseName != nativeNode.BrowseName)
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{wotNode.DisplayPath} BrowseName differs: ");
                    failures.Append(CultureInfo.InvariantCulture, $"WoT {FormatBrowseName(wotNode.BrowseName)}, ");
                    failures.Append(CultureInfo.InvariantCulture, $"native {FormatBrowseName(nativeNode.BrowseName)}.");
                    failures.AppendLine();
                }

                if (wotNode.NodeClass != nativeNode.NodeClass)
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{wotNode.DisplayPath} NodeClass differs: ");
                    failures.Append(CultureInfo.InvariantCulture, $"WoT {wotNode.NodeClass}, ");
                    failures.Append(CultureInfo.InvariantCulture, $"native {nativeNode.NodeClass}.");
                    failures.AppendLine();
                }

                if (wotNode.TypeDefinition != nativeNode.TypeDefinition)
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{wotNode.DisplayPath} TypeDefinition differs: ");
                    failures.Append(CultureInfo.InvariantCulture, $"WoT {FormatTypeKey(wotNode.TypeDefinition)}, ");
                    failures.Append(CultureInfo.InvariantCulture, $"native {FormatTypeKey(nativeNode.TypeDefinition)}.");
                    failures.AppendLine();
                }
            }

            return failures.ToString();
        }

        private static string CompareTypeDefinitions(
            Dictionary<TypeKey, TypeDefinitionInfo> wotTypes,
            Dictionary<TypeKey, TypeDefinitionInfo> nativeTypes)
        {
            var failures = new StringBuilder();
            foreach ((TypeKey key, TypeDefinitionInfo wotType) in wotTypes.OrderBy(
                pair => FormatTypeKey(pair.Key),
                StringComparer.Ordinal))
            {
                if (!nativeTypes.TryGetValue(key, out TypeDefinitionInfo? nativeType))
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{FormatTypeKey(key)} missing from native server.");
                    failures.AppendLine();
                    continue;
                }

                if (wotType.NodeClass != nativeType.NodeClass)
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{FormatTypeKey(key)} NodeClass differs: ");
                    failures.Append(CultureInfo.InvariantCulture, $"WoT {wotType.NodeClass}, ");
                    failures.Append(CultureInfo.InvariantCulture, $"native {nativeType.NodeClass}.");
                    failures.AppendLine();
                }

                if (wotType.SuperType != nativeType.SuperType)
                {
                    failures.Append(CultureInfo.InvariantCulture, $"{FormatTypeKey(key)} supertype differs: ");
                    failures.Append(CultureInfo.InvariantCulture, $"WoT {FormatTypeKey(wotType.SuperType)}, ");
                    failures.Append(CultureInfo.InvariantCulture, $"native {FormatTypeKey(nativeType.SuperType)}.");
                    failures.AppendLine();
                }
            }

            foreach (TypeKey key in nativeTypes.Keys.Except(wotTypes.Keys).OrderBy(FormatTypeKey, StringComparer.Ordinal))
            {
                failures.Append(CultureInfo.InvariantCulture, $"{FormatTypeKey(key)} missing from WoT server.");
                failures.AppendLine();
            }

            return failures.ToString();
        }

        private static void WriteNativeOnlyNodes(AddressSpaceTree wotTree, AddressSpaceTree nativeTree)
        {
            var nativeOnly = nativeTree.Nodes.Keys
                .Except(wotTree.Nodes.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            if (nativeOnly.Count == 0)
            {
                TestContext.Out.WriteLine("Native Pump_1 has no extra nodes beyond the WoT Pump1 subset.");
                return;
            }

            TestContext.Out.WriteLine("Native Pump_1 extra nodes not present in WoT Pump1: {0}", nativeOnly.Count);
            foreach (string path in nativeOnly)
            {
                TestContext.Out.WriteLine("  {0}", nativeTree.Nodes[path].DisplayPath);
            }
        }

        private static string FormatBrowseName(BrowseNameKey browseName)
        {
            return browseName.NamespaceUri + ":" + browseName.Name;
        }

        private static string FormatTypeKey(TypeKey? typeKey)
        {
            return typeKey == null ? "<none>" : typeKey.NamespaceUri + ":" + typeKey.Name;
        }

        private const int kMaxTreeDepth = 6;
        private const int kMaxTypeDepth = 30;
        private const string kWotPumpNamespaceUri =
            "urn:opcfoundation.org:UA:WotAggregation:PumpInstance";
        private const string kPumpsNamespaceUri = "http://opcfoundation.org/UA/Pumps/";
        private const string kMachineryNamespaceUri = "http://opcfoundation.org/UA/Machinery/";
        private const string kDiNamespaceUri = "http://opcfoundation.org/UA/DI/";

        private static readonly HashSet<string> s_companionNamespaceUris =
        [
            kPumpsNamespaceUri,
            kMachineryNamespaceUri,
            kDiNamespaceUri
        ];

        private sealed record AddressSpaceTree(
            string RootName,
            Dictionary<string, StructuralNode> Nodes);

        private sealed record BrowseNameKey(string NamespaceUri, string Name);

        private sealed record ManagedSessionConnection(IHost Host, ManagedSession Session) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await Host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    Host.Dispose();
                }
            }
        }

        private sealed record NativePumpEnvironment(
            string Root,
            string EndpointUrl,
            IHost ServerHost) : IAsyncDisposable
        {
            public static async Task<NativePumpEnvironment> StartAsync(CancellationToken cancellationToken)
            {
                string id = Guid.NewGuid().ToString("N");
                string root = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    nameof(NativePumpEnvironment),
                    id);
                Directory.CreateDirectory(root);
                int port = TestPorts.GetFreePort();
                string endpointUrl = "opc.tcp://127.0.0.1:" +
                    port.ToString(CultureInfo.InvariantCulture) +
                    "/PumpDeviceIntegrationServer";
                IHost serverHost = BuildServerHost(root, endpointUrl);
                var environment = new NativePumpEnvironment(root, endpointUrl, serverHost);
                try
                {
                    await serverHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    await WaitForTcpAsync(port, cancellationToken).ConfigureAwait(false);
                    return environment;
                }
                catch
                {
                    await environment.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async Task<ManagedSessionConnection> ConnectAsync(CancellationToken cancellationToken)
            {
                IHost clientHost = BuildClientHost(Root, EndpointUrl);
                try
                {
                    await clientHost.StartAsync(cancellationToken).ConfigureAwait(false);
                    Func<CancellationToken, Task<ManagedSession>> connect =
                        clientHost.Services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
                    ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                    await session.FetchNamespaceTablesAsync(cancellationToken).ConfigureAwait(false);
                    session.MessageContext.NamespaceUris.Update(session.NamespaceUris.ToArray());
                    return new ManagedSessionConnection(clientHost, session);
                }
                catch
                {
                    await clientHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    clientHost.Dispose();
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await ServerHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TestContext.Out.WriteLine("Ignoring best-effort native server teardown failure: {0}", ex);
                }
                finally
                {
                    ServerHost.Dispose();
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, recursive: true);
                    }
                }
            }

            private static IHost BuildServerHost(string root, string endpointUrl)
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddLogging();
                builder.Services.Configure<PumpDeviceIntegrationOptions>(options =>
                {
                    options.PumpCount = 1;
                });
                builder.Services
                    .AddOpcUa()
                    .AddServer(options =>
                    {
                        options.ApplicationName = "PumpDeviceIntegrationServer" + Guid.NewGuid().ToString("N");
                        options.ApplicationUri = "urn:localhost:OPCFoundation:" + options.ApplicationName;
                        options.ProductUri = "uri:opcfoundation.org:PumpDeviceIntegrationServer";
                        options.AutoAcceptUntrustedCertificates = true;
                        options.IncludeUnsecurePolicyNone = true;
                        options.PkiRoot = Path.Combine(root, "Server", "pki");
                        options.RejectSHA1Certificates = true;
                        options.MinCertificateKeySize = 2048;
                        options.EndpointUrls.Clear();
                        options.EndpointUrls.Add(endpointUrl);
                    })
                    .AddNodeManager<PumpNodeManagerFactory>()
                    .ConfigureDevicesFor<PumpNodeManager>(context =>
                    {
                        var manager = (PumpNodeManager)context.Manager;
                        foreach (NodeId pumpNodeId in manager.PumpNodeIds)
                        {
                            ITopologyElementBuilder<PumpState> pump =
                                context.TopologyElement<PumpState>(pumpNodeId);
                            pump.WithFunctionalGroup(
                                new QualifiedName("Diagnostics", manager.InstanceNamespaceIndex),
                                fg => fg.Configure(node =>
                                    node.WithProperty("LastError", Variant.From(string.Empty), p => p.Writable())
                                        .WithProperty("ErrorCount", 0)
                                        .WithProperty("LastSelfTest", (DateTimeUtc)DateTime.UtcNow)));
                        }

                        return new ValueTask();
                    });
                return builder.Build();
            }

            private static IHost BuildClientHost(string root, string endpointUrl)
            {
                HostApplicationBuilder builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                builder.Services
                    .AddOpcUa()
                    .AddOpcTcpTransport()
                    .AddClient(client =>
                    {
                        string applicationName = "NativePumpComparisonClient" + Guid.NewGuid().ToString("N");
                        client.ApplicationName = applicationName;
                        client.ApplicationUri = "urn:localhost:OPCFoundation:" + applicationName;
                        client.ProductUri = "uri:opcfoundation.org:NativePumpComparisonClient";
                        client.PkiRoot = Path.Combine(root, "Client", Guid.NewGuid().ToString("N"), "pki");
                        client.AutoAcceptUntrustedCertificates = true;
                        client.Session = new ManagedSessionOptions
                        {
                            SessionName = "NativePumpComparisonClient",
                            SessionTimeout = TimeSpan.FromSeconds(60)
                        };
                    })
                    .AddDiscoveryAndConnect(discovery =>
                    {
                        discovery.DiscoveryUrl = endpointUrl;
                        discovery.SecurityMode = MessageSecurityMode.None;
                        discovery.SecurityPolicyUri = SecurityPolicies.None;
                    });
                return builder.Build();
            }

            private static async Task WaitForTcpAsync(int port, CancellationToken cancellationToken)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var client = new TcpClient();
                    try
                    {
                        await client.ConnectAsync("127.0.0.1", port, cancellationToken)
                            .ConfigureAwait(false);
                        return;
                    }
                    catch (SocketException)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private sealed record NodeAttributes(BrowseNameKey BrowseName, NodeClass NodeClass);

        private sealed record StructuralNode(
            string DisplayPath,
            BrowseNameKey BrowseName,
            NodeClass NodeClass,
            TypeKey? TypeDefinition);

        private sealed record TypeDefinitionInfo(
            TypeKey Key,
            NodeClass NodeClass,
            TypeKey? SuperType);

        private sealed record TypeKey(string NamespaceUri, string Name);
    }
}
