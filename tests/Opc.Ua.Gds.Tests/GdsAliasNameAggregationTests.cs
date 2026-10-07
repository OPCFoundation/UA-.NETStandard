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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// End-to-end tests of the GDS AliasName Server facet (OPC 10000-17
    /// Annex C): a second reference server, an AliasName Server, registers
    /// with the GDS, which reads its AliasNames over a client session.
    /// </summary>
    /// <remarks>
    /// The GDS host is a reference server too, so its TagVariables also hold
    /// its own aliases; the aggregated nodes are told apart by their
    /// namespace, the GDS application-record namespace.
    /// </remarks>
    [TestFixture]
    [Category("GDS")]
    [Category("AliasNames")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class GdsAliasNameAggregationTests : GdsTestFixture
    {
        protected override void ConfigureGds(GlobalDiscoveryServerConfiguration configuration)
        {
            configuration.EnableAliasNameAggregation = true;
        }

        [OneTimeSetUp]
        public async Task StartAliasNameSourceAsync()
        {
            m_sourcePkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            m_source = new ServerFixture<AliasSourceServer>(t => new AliasSourceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_source.LoadConfigurationAsync(m_sourcePkiRoot).ConfigureAwait(false);
            await m_source.StartAsync().ConfigureAwait(false);
            m_sourceUri = m_source.Config.ApplicationUri;
            m_sourceUrl = Utils.UriSchemeOpcTcp + "://localhost:" +
                m_source.Port.ToString(CultureInfo.InvariantCulture);
            m_aggregateNamespace = (ushort)Session.NamespaceUris.GetIndex(
                ApplicationsNodeManager.ApplicationsNamespaceUri);
        }

        [OneTimeTearDown]
        public async Task StopAliasNameSourceAsync()
        {
            if (m_source != null)
            {
                await m_source.StopAsync().ConfigureAwait(false);
            }
            try
            {
                if (m_sourcePkiRoot != null && Directory.Exists(m_sourcePkiRoot))
                {
                    Directory.Delete(m_sourcePkiRoot, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public async Task RegisteredAliasNameServerIsAggregatedAndRemovedAgainAsync()
        {
            NodeId applicationId = await RegisterAsync(AliasSourceRecord(m_sourceUrl)).ConfigureAwait(false);
            try
            {
                // Annex C.2: the aliases are merged before RegisterApplication returns.
                ReferenceDescription alias = await FindAggregatedChildAsync(
                    Ua.ObjectIds.TagVariables, "TIC101_PV").ConfigureAwait(false);
                Assert.That(alias, Is.Not.Null, "TIC101_PV of the source is aggregated under TagVariables.");
                Assert.That(alias.TypeDefinition, Is.EqualTo((ExpandedNodeId)Ua.ObjectTypeIds.AliasNameType));

                ExpandedNodeId target = await GetAliasForTargetAsync(ToNodeId(alias.NodeId)).ConfigureAwait(false);
                Assert.That(target.ServerIndex, Is.GreaterThan(0u), "The target lives on the source Server.");
                Assert.That(target.NamespaceUri, Is.Not.Null.And.Not.Empty);
                string[] serverArray = await ReadServerArrayAsync().ConfigureAwait(false);
                Assert.That(serverArray[target.ServerIndex], Is.EqualTo(m_sourceUri),
                    "The ServerArray entry names the source.");

                // The source's own nested category is aggregated with its aliases.
                ReferenceDescription devices = await FindAggregatedChildAsync(
                    Ua.ObjectIds.TagVariables, "Devices").ConfigureAwait(false);
                Assert.That(devices, Is.Not.Null);
                Assert.That(devices.TypeDefinition, Is.EqualTo((ExpandedNodeId)Ua.ObjectTypeIds.AliasNameCategoryType));
                Assert.That(await FindAggregatedChildAsync(ToNodeId(devices.NodeId), "Heater_Power").ConfigureAwait(false),
                    Is.Not.Null);

                // FindAlias on the well-known category returns the GDS's own
                // alias and the aggregated one.
                AliasNameDataType[] found = await FindAliasAsync(Ua.ObjectIds.TagVariables, "Heater_Power")
                    .ConfigureAwait(false);
                Assert.That(found.SelectMany(a => a.ReferencedNodes.ToArray()).Any(n => n.ServerIndex > 0), Is.True);

                // FindAlias on the aggregated category answers from the merged list.
                AliasNameDataType[] inDevices = await FindAliasAsync(ToNodeId(devices.NodeId), "%")
                    .ConfigureAwait(false);
                Assert.That(inDevices.Select(a => a.AliasName.Name), Is.EquivalentTo(s_deviceAliases));
            }
            finally
            {
                await UnregisterAsync(applicationId).ConfigureAwait(false);
            }

            // Annex C.3: the aliases, the categories and the ServerUri are removed.
            Assert.That(await FindAggregatedChildAsync(Ua.ObjectIds.TagVariables, "TIC101_PV").ConfigureAwait(false),
                Is.Null);
            Assert.That(await FindAggregatedChildAsync(Ua.ObjectIds.TagVariables, "Devices").ConfigureAwait(false),
                Is.Null);
            Assert.That(await ReadServerArrayAsync().ConfigureAwait(false), Does.Not.Contain(m_sourceUri));
            AliasNameDataType[] remaining = await FindAliasAsync(Ua.ObjectIds.TagVariables, "Heater_Power")
                .ConfigureAwait(false);
            Assert.That(remaining.SelectMany(a => a.ReferencedNodes.ToArray()).All(n => n.ServerIndex == 0), Is.True);
        }

        [Test]
        public async Task UnreachableAliasNameServerIsRegisteredWithoutAliasesAsync()
        {
            int closedPort = ServerFixtureUtils.GetNextFreeIPPort();
            ApplicationRecordDataType record = AliasSourceRecord(
                Utils.UriSchemeOpcTcp + "://127.0.0.1:" + closedPort.ToString(CultureInfo.InvariantCulture));
            record.ApplicationUri = "urn:localhost:tests:unreachable-alias-source";

            NodeId applicationId = await RegisterAsync(record).ConfigureAwait(false);
            try
            {
                Assert.That(applicationId.IsNull, Is.False);
                Assert.That(await ReadServerArrayAsync().ConfigureAwait(false), Does.Not.Contain(record.ApplicationUri));
            }
            finally
            {
                await UnregisterAsync(applicationId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerWithoutAliasCapabilityIsNotAggregatedAsync()
        {
            ApplicationRecordDataType record = AliasSourceRecord(m_sourceUrl);
            record.ServerCapabilities = ["DA"];

            NodeId applicationId = await RegisterAsync(record).ConfigureAwait(false);
            try
            {
                Assert.That(await FindAggregatedChildAsync(Ua.ObjectIds.TagVariables, "TIC101_PV").ConfigureAwait(false),
                    Is.Null);
                Assert.That(await ReadServerArrayAsync().ConfigureAwait(false), Does.Not.Contain(m_sourceUri));
            }
            finally
            {
                await UnregisterAsync(applicationId).ConfigureAwait(false);
            }
        }

        private ApplicationRecordDataType AliasSourceRecord(string discoveryUrl)
        {
            return new ApplicationRecordDataType
            {
                ApplicationUri = m_sourceUri,
                ApplicationType = ApplicationType.Server,
                ApplicationNames = new LocalizedText[] { new("en-US", "AliasName source") }.ToArrayOf(),
                ProductUri = "urn:opcfoundation.org:tests:alias-source",
                DiscoveryUrls = new[] { discoveryUrl }.ToArrayOf(),
                ServerCapabilities = ["ALIAS"]
            };
        }

        private async Task<ReferenceDescription> FindAggregatedChildAsync(NodeId parentId, string browseName)
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(parentId).ConfigureAwait(false);
            return children.FirstOrDefault(r =>
                r.BrowseName.Name == browseName &&
                r.NodeId.NamespaceIndex == m_aggregateNamespace);
        }

        private async Task<ExpandedNodeId> GetAliasForTargetAsync(NodeId aliasId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[] {
                    new() {
                        NodeId = aliasId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.AliasFor,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.EqualTo(1));
            return response.Results[0].References[0].NodeId;
        }

        private async Task<string[]> ReadServerArrayAsync()
        {
            DataValue value = await Session.ReadValueAsync(Ua.VariableIds.Server_ServerArray).ConfigureAwait(false);
            return value.WrappedValue.GetStringArray().ToArray();
        }

        private async Task<AliasNameDataType[]> FindAliasAsync(NodeId categoryId, string pattern)
        {
            ReferenceDescription method = await FindChildAsync(categoryId, Ua.BrowseNames.FindAlias).ConfigureAwait(false);
            Assert.That(method, Is.Not.Null, $"FindAlias on {categoryId}");
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = categoryId,
                        MethodId = ToNodeId(method.NodeId),
                        InputArguments = new Variant[] { pattern, ReferenceTypeIds.AliasFor }.ToArrayOf()
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"FindAlias failed: {response.Results[0].StatusCode}");
            return [.. response.Results[0].OutputArguments[0].GetStructureArray<AliasNameDataType>()];
        }

        private async Task<NodeId> RegisterAsync(ApplicationRecordDataType record)
        {
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = ToNodeId(ObjectIds.Directory),
                        MethodId = ToNodeId(MethodIds.Directory_RegisterApplication),
                        InputArguments = new Variant[] { new(new ExtensionObject(record)) }.ToArrayOf()
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"RegisterApplication failed: {response.Results[0].StatusCode}");
            return (NodeId)response.Results[0].OutputArguments[0];
        }

        private async Task UnregisterAsync(NodeId applicationId)
        {
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = ToNodeId(ObjectIds.Directory),
                        MethodId = ToNodeId(MethodIds.Directory_UnregisterApplication),
                        InputArguments = new Variant[] { new(applicationId) }.ToArrayOf()
                    }
                }.ToArrayOf(),
                default).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"UnregisterApplication failed: {response.Results[0].StatusCode}");
        }

        private static readonly string[] s_deviceAliases = ["Pump1_Status", "Heater_Power"];
        private ServerFixture<AliasSourceServer> m_source;
        private string m_sourcePkiRoot;
        private string m_sourceUri;
        private string m_sourceUrl;
        private ushort m_aggregateNamespace;
    }

    /// <summary>
    /// The reference server under another name, so the test fixture gives it an
    /// ApplicationUri of its own (the GDS host is a reference server too).
    /// </summary>
    public sealed class AliasSourceServer : ReferenceServer
    {
        public AliasSourceServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }
    }
}
