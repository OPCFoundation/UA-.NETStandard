/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Sessions.Tests;
using Opc.Ua.Tests;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// This stack's client tests against the 1.5.x Quickstarts reference
    /// server (the legacy peer with --kind reference and all of its default
    /// node managers). The tests are the server-independent workers of
    /// <see cref="ClientTest"/>, which run here against the 1.5 server
    /// instead of the 2.0 reference server, plus a load of the 1.5 server's
    /// custom data types with the 2.0 complex type system.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class LegacyReferenceServerTests
    {
        private static readonly TimeSpan s_startTimeout = TimeSpan.FromMinutes(2);

        private LegacyPeerProcess m_server;
        private string m_pkiRoot;
        private ClientTest m_clientTest;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = InteropPki.CreateRoot();
            (m_server, string url) = await LegacyPeerProcess
                .StartServerAsync(InteropPki.ServerPki(m_pkiRoot), s_startTimeout, "--kind", "reference")
                .ConfigureAwait(false);

            m_clientTest = new ClientTest(Utils.UriSchemeOpcTcp) { ExternalServerUrl = url };
            await m_clientTest.OneTimeSetUpAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            try
            {
                if (m_clientTest != null)
                {
                    await m_clientTest.OneTimeTearDownAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await InteropPki.StopAndDeleteAsync(m_server, m_pkiRoot).ConfigureAwait(false);
            }
        }

        [SetUp]
        public Task SetUpAsync()
        {
            return m_clientTest.SetUpAsync();
        }

        [TearDown]
        public Task TearDownAsync()
        {
            return m_clientTest.TearDownAsync();
        }

        [Test]
        [Order(100)]
        public Task GetEndpointsAsync()
        {
            return m_clientTest.GetEndpointsAsync();
        }

        [Test]
        [Order(100)]
        public Task FindServersAsync()
        {
            return m_clientTest.FindServersAsync();
        }

        [Test]
        [Order(100)]
        public Task FindServersOnNetworkAsync()
        {
            return m_clientTest.FindServersOnNetworkAsync();
        }

        [Test]
        [Order(105)]
        public Task GetEndpointsOnDiscoveryChannelAsync()
        {
            return m_clientTest.GetEndpointsOnDiscoveryChannelAsync(true);
        }

        [Test]
        [Order(110)]
        public Task InvalidConfigurationAsync()
        {
            return m_clientTest.InvalidConfigurationAsync();
        }

        [Test]
        [Order(202)]
        public Task ConnectAndCloseAsyncReadAfterCloseAsync()
        {
            return m_clientTest.ConnectAndCloseAsyncReadAfterCloseAsync();
        }

        [Test]
        [Order(204)]
        public Task ConnectAndCloseAsyncReadAfterCloseSessionReconnectAsync()
        {
            return m_clientTest.ConnectAndCloseAsyncReadAfterCloseSessionReconnectAsync();
        }

        [Test]
        [Order(210)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [CancelAfter(120_000)]
        public Task ConnectAndReconnectAsync(bool reconnectAbort, bool useMaxReconnectPeriod, CancellationToken ct)
        {
            return m_clientTest.ConnectAndReconnectAsync(reconnectAbort, useMaxReconnectPeriod, ct);
        }

        [Test]
        [Order(240)]
        public Task ConnectMultipleSessionsAsync()
        {
            return m_clientTest.ConnectMultipleSessionsAsync();
        }

        [Test]
        [Order(250)]
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(120_000)]
        public Task ReconnectSessionOnAlternateChannelAsync(bool closeChannel, CancellationToken ct)
        {
            return m_clientTest.ReconnectSessionOnAlternateChannelAsync(closeChannel, ct);
        }

        [Test]
        [Order(260)]
        [TestCase(SecurityPolicies.None, true)]
        [TestCase(SecurityPolicies.Basic256Sha256, true)]
        [TestCase(SecurityPolicies.Basic256Sha256, false)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, false)]
        [CancelAfter(120_000)]
        public Task ReconnectSessionOnAlternateChannelWithSavedSessionSecretsAsync(
            string securityPolicy,
            bool anonymous,
            CancellationToken ct)
        {
            return m_clientTest.ReconnectSessionOnAlternateChannelWithSavedSessionSecretsAsync(
                securityPolicy,
                anonymous,
                ct);
        }

        [Test]
        [Order(270)]
        public Task RecreateSessionWithRenewUserIdentityAsync()
        {
            return m_clientTest.RecreateSessionWithRenewUserIdentityAsync();
        }

        [Test]
        [Order(300)]
        public Task GetOperationLimitsAsync()
        {
            return m_clientTest.GetOperationLimitsTestAsync();
        }

        [Test]
        public void ReadPublicProperties()
        {
            m_clientTest.ReadPublicProperties();
        }

        [Test]
        public Task ChangePreferredLocalesAsync()
        {
            return m_clientTest.ChangePreferredLocalesAsync();
        }

        [Test]
        public Task ReadValueAsync()
        {
            return m_clientTest.ReadValueAsync();
        }

        [Test]
        public Task ReadValueTypedAsync()
        {
            return m_clientTest.ReadValueTypedAsync();
        }

        [Test]
        public Task ReadValueFromTestSimulationAsync()
        {
            return m_clientTest.ReadValueFromTestSimulationAsync();
        }

        [Test]
        public Task ReadValuesAsync()
        {
            return m_clientTest.ReadValuesAsync();
        }

        [Test]
        public Task ReadDataTypeDefinitionAsync()
        {
            return m_clientTest.ReadDataTypeDefinitionAsync();
        }

        [Test]
        public Task ReadDataTypeDefinition2Async()
        {
            return m_clientTest.ReadDataTypeDefinition2Async();
        }

        [Test]
        public Task ReadDataTypeDefinitionNodesAsync()
        {
            return m_clientTest.ReadDataTypeDefinitionNodesAsync();
        }

        [Test]
        [Order(400)]
        [TestCase(SecurityPolicies.Basic256Sha256, false)]
        [TestCase(SecurityPolicies.Basic256Sha256, true)]
        public Task BrowseFullAddressSpaceAsync(string securityPolicy, bool operationLimits)
        {
            return m_clientTest.BrowseFullAddressSpaceAsync(securityPolicy, operationLimits);
        }

        [Test]
        [Order(450)]
        public Task ReadDisplayNamesAsync()
        {
            return m_clientTest.ReadDisplayNamesAsync();
        }

        [Test]
        [Order(480)]
        public Task SubscriptionAsync()
        {
            return m_clientTest.SubscriptionAsync();
        }

        [Test]
        [Order(550)]
        public Task ReadNodeSyncAsync()
        {
            return m_clientTest.ReadNodeSyncAsync();
        }

        [Test]
        [Order(550)]
        public Task ReadNodeAsync()
        {
            return m_clientTest.ReadNodeAsync();
        }

        [Test]
        [Order(570)]
        [TestCase(0)]
        [TestCase(ClientTest.MaxReferences)]
        public Task ReadNodesAsync(int nodeCount)
        {
            return m_clientTest.ReadNodesAsync(nodeCount);
        }

        [Test]
        [Order(620)]
        public Task ReadAvailableEncodingsAsync()
        {
            return m_clientTest.ReadAvailableEncodingsAsync();
        }

        [Test]
        [Order(800)]
        [TestCase(true)]
        [TestCase(false)]
        public Task TransferSubscriptionNativeAsync(bool sendInitialData)
        {
            return m_clientTest.TransferSubscriptionNativeAsync(sendInitialData);
        }

        [Test]
        [Order(10000)]
        public Task ReadBuildInfoAsync()
        {
            return m_clientTest.ReadBuildInfoAsync();
        }

        /// <summary>
        /// Loads the custom data types of the 1.5 server (TestData
        /// structures, unions, structures with optional fields,
        /// enumerations) with the 2.0 complex type system, decodes every
        /// variable whose data type is a custom structure, and writes a
        /// sample of the decoded structures back unchanged.
        /// </summary>
        [Test]
        [Order(900)]
        [CancelAfter(300_000)]
        public async Task LoadAndDecodeLegacyComplexTypesAsync(CancellationToken ct)
        {
            ISession session = m_clientTest.Session;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using ComplexTypeSystem typeSystem =
                ComplexTypeSystem.Create(session, new ComplexTypeBuilderFactory(), telemetry);
            bool loaded = await typeSystem.LoadAsync(throwOnError: true, ct: ct).ConfigureAwait(false);
            Assert.That(loaded, Is.True, "the complex type system did not load every type");

            string[] typeNames = [.. typeSystem.GetDefinedTypes().Select(t => t.Name)];
            Assert.That(
                typeNames,
                Is.SupersetOf(new[] { "ScalarStructureDataType", "VectorUnion", "VectorWithOptionalFields" }),
                string.Join(", ", typeNames));

            List<NodeId> variables = await BrowseVariablesAsync(session, ct).ConfigureAwait(false);
            DataValue[] dataTypes = await ReadAsync(session, variables, Attributes.DataType, ct)
                .ConfigureAwait(false);
            // Only structure data types: a BaseDataType-derived type such as
            // VariantDataType can hold a random ExtensionObject nobody can decode.
            HashSet<NodeId> structureTypes = await BrowseStructureTypesAsync(session, ct).ConfigureAwait(false);
            var custom = new List<NodeId>();
            for (int ii = 0; ii < variables.Count; ii++)
            {
                if (dataTypes[ii].WrappedValue.TryGetValue(out NodeId dataType) &&
                    dataType.NamespaceIndex != 0 &&
                    structureTypes.Contains(dataType))
                {
                    custom.Add(variables[ii]);
                }
            }
            Assert.That(custom, Has.Count.GreaterThanOrEqualTo(10), "variables with a custom structure data type");

            DataValue[] values = await ReadAsync(session, custom, Attributes.Value, ct).ConfigureAwait(false);
            DataValue[] accessLevels = await ReadAsync(session, custom, Attributes.UserAccessLevel, ct)
                .ConfigureAwait(false);
            var undecoded = new List<(NodeId Variable, ExpandedNodeId TypeId)>();
            var writes = new List<WriteValue>();
            int structures = 0;
            for (int ii = 0; ii < custom.Count; ii++)
            {
                if (StatusCode.IsBad(values[ii].StatusCode))
                {
                    continue;
                }
                ExtensionObject[] extensions = ExtensionObjects(values[ii].WrappedValue);
                foreach (ExtensionObject extension in extensions)
                {
                    structures++;
                    if (!extension.TryGetValue(out IEncodeable _))
                    {
                        undecoded.Add((custom[ii], extension.TypeId));
                    }
                }
                if (extensions.Length > 0 &&
                    writes.Count < 50 &&
                    accessLevels[ii].WrappedValue.TryGetValue(out byte access) &&
                    (access & AccessLevels.CurrentWrite) != 0)
                {
                    writes.Add(new WriteValue
                    {
                        NodeId = custom[ii],
                        AttributeId = Attributes.Value,
                        Value = new DataValue(values[ii].WrappedValue)
                    });
                }
            }
            Assert.That(structures, Is.GreaterThan(0), "no structure values were read");
            // The simulated variables of the reference server occasionally hold a
            // random structure whose TypeId (a random opaque NodeId) is no node of
            // the server; only a structure with an encoding the server defines
            // must decode.
            var failures = new List<string>();
            if (undecoded.Count > 0)
            {
                List<NodeId> typeIds =
                [
                    .. undecoded.Select(u => ExpandedNodeId.ToNodeId(u.TypeId, session.NamespaceUris))
                ];
                DataValue[] nodeClasses = await ReadAsync(session, typeIds, Attributes.NodeClass, ct)
                    .ConfigureAwait(false);
                for (int ii = 0; ii < undecoded.Count; ii++)
                {
                    if (StatusCode.IsGood(nodeClasses[ii].StatusCode))
                    {
                        failures.Add($"{undecoded[ii].Variable} ({undecoded[ii].TypeId})");
                    }
                }
            }
            Assert.That(failures, Is.Empty, $"{failures.Count} of {structures} structures were not decoded");
            Assert.That(writes, Is.Not.Empty, "no writable structure variable found");

            ArrayOf<WriteValue> nodesToWrite = [.. writes];
            WriteResponse response = await session.WriteAsync(null, nodesToWrite, ct).ConfigureAwait(false);
            string[] rejected =
            [
                .. writes
                    .Select((w, ii) => (w, ii))
                    .Where(x => StatusCode.IsBad(response.Results[x.ii]))
                    .Select(x => $"{x.w.NodeId}: {response.Results[x.ii]}")
            ];
            Assert.That(rejected, Is.Empty, "structure writes rejected by the 1.5 server");

            DataValue[] readBack = await ReadAsync(session, [.. writes.Select(w => w.NodeId)], Attributes.Value, ct)
                .ConfigureAwait(false);
            string[] changed =
            [
                .. writes
                    .Select((w, ii) => (w, ii))
                    .Where(x => !x.w.Value.WrappedValue.Equals(readBack[x.ii].WrappedValue))
                    .Select(x => x.w.NodeId.ToString())
            ];
            Assert.That(changed, Is.Empty, "structures changed in a write and read round trip");
        }

        private static ExtensionObject[] ExtensionObjects(Variant value)
        {
            if (value.TryGetValue(out ExtensionObject extension))
            {
                return [extension];
            }
            if (value.TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                return [.. extensions.ToArray().Where(e => !e.IsNull)];
            }
            return [];
        }

        /// <summary>
        /// The variables below the Objects folder outside namespace 0.
        /// </summary>
        private static async Task<List<NodeId>> BrowseVariablesAsync(ISession session, CancellationToken ct)
        {
            var variables = new List<NodeId>();
            var visited = new HashSet<NodeId> { ObjectIds.ObjectsFolder };
            var queue = new Queue<NodeId>();
            queue.Enqueue(ObjectIds.ObjectsFolder);
            while (queue.Count > 0 && visited.Count < 30_000)
            {
                NodeId nodeId = queue.Dequeue();
                if (nodeId == ObjectIds.Server)
                {
                    continue;
                }
                List<ReferenceDescription> references = await BrowseAsync(
                    session, nodeId, ReferenceTypeIds.HierarchicalReferences, ct).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                    if (reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }
                    var target = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    if (!visited.Add(target))
                    {
                        continue;
                    }
                    queue.Enqueue(target);
                    if (reference.NodeClass == NodeClass.Variable && target.NamespaceIndex != 0)
                    {
                        variables.Add(target);
                    }
                }
            }
            return variables;
        }

        /// <summary>
        /// Structure and all its subtypes.
        /// </summary>
        private static async Task<HashSet<NodeId>> BrowseStructureTypesAsync(ISession session, CancellationToken ct)
        {
            var types = new HashSet<NodeId> { DataTypeIds.Structure };
            var queue = new Queue<NodeId>();
            queue.Enqueue(DataTypeIds.Structure);
            while (queue.Count > 0)
            {
                List<ReferenceDescription> subtypes = await BrowseAsync(
                    session, queue.Dequeue(), ReferenceTypeIds.HasSubtype, ct).ConfigureAwait(false);
                foreach (ReferenceDescription subtype in subtypes)
                {
                    var id = ExpandedNodeId.ToNodeId(subtype.NodeId, session.NamespaceUris);
                    if (!id.IsNull && types.Add(id))
                    {
                        queue.Enqueue(id);
                    }
                }
            }
            return types;
        }

        /// <summary>
        /// Browses the forward references of one node, following continuation points.
        /// </summary>
        private static async Task<List<ReferenceDescription>> BrowseAsync(
            ISession session,
            NodeId nodeId,
            NodeId referenceTypeId,
            CancellationToken ct)
        {
            ArrayOf<BrowseDescription> nodesToBrowse =
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            BrowseResponse response = await session.BrowseAsync(null, null, 0, nodesToBrowse, ct)
                .ConfigureAwait(false);
            var references = new List<ReferenceDescription>(response.Results[0].References.ToArray());
            ByteString continuationPoint = response.Results[0].ContinuationPoint;
            while (!continuationPoint.IsEmpty)
            {
                BrowseNextResponse next = await session
                    .BrowseNextAsync(null, false, [continuationPoint], ct)
                    .ConfigureAwait(false);
                references.AddRange(next.Results[0].References.ToArray());
                continuationPoint = next.Results[0].ContinuationPoint;
            }
            return references;
        }

        private static async Task<DataValue[]> ReadAsync(
            ISession session,
            List<NodeId> nodes,
            uint attributeId,
            CancellationToken ct)
        {
            var results = new List<DataValue>();
            for (int offset = 0; offset < nodes.Count; offset += 500)
            {
                ArrayOf<ReadValueId> nodesToRead =
                [
                    .. nodes
                        .Skip(offset)
                        .Take(500)
                        .Select(n => new ReadValueId { NodeId = n, AttributeId = attributeId })
                ];
                ReadResponse response = await session
                    .ReadAsync(null, 0, TimestampsToReturn.Neither, nodesToRead, ct)
                    .ConfigureAwait(false);
                results.AddRange(response.Results.ToArray());
            }
            return [.. results];
        }
    }
}
