// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Server Capabilities and Base Information conformance.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("ServerCapabilities")]
    public class ServerCapabilitiesTests : TestFixture
    {
        [Test]
        public async Task ReadServerProfileArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out ArrayOf<string> _), Is.True);
        }

        [Test]
        public async Task ReadServerProfileArrayContainsHistoricalAccessProfilesAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_ServerProfileArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out ArrayOf<string> profiles), Is.True);

            var profileList = new System.Collections.Generic.List<string>();
            foreach (string profile in profiles)
            {
                profileList.Add(profile);
            }

            // The reference server enables history archiving at startup, so the
            // ReferenceNodeManager contributes the Historical Access profiles which
            // the server merges into the configured ServerProfileArray (Part 7).
            Assert.That(
                profileList,
                Has.Member("http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022"));
            Assert.That(
                profileList,
                Has.Member("http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022"));
        }

        [Test]
        public async Task ReadConformanceUnitsContainsHistoricalAccessUnitsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_ConformanceUnits).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);

            var unitNames = new System.Collections.Generic.List<string>();
            foreach (QualifiedName unit in units)
            {
                unitNames.Add(unit.Name!);
            }

            // Base units are always advertised; the Historical Access units are
            // contributed by the ReferenceNodeManager (IConformanceContributor)
            // because the reference server historizes at startup.
            Assert.That(unitNames, Has.Member("Address Space Base"));
            Assert.That(unitNames, Has.Member("Historical Access Read Raw"));
            Assert.That(unitNames, Has.Member("Attribute Historical Read"));
        }

        [Test]
        public async Task ReadLocaleIdArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_LocaleIdArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out ArrayOf<string> _), Is.True);
        }

        [Test]
        public async Task ReadMinSupportedSampleRateAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MinSupportedSampleRate).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            double rate = result.WrappedValue.GetDouble();
            Assert.That(rate, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public async Task ReadMaxBrowseContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxQueryContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadMaxHistoryContinuationPointsAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_MaxHistoryContinuationPoints).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerReadAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerWriteAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadOperationLimitsMaxNodesPerBrowseAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadNamespaceArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_NamespaceArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] namespaces = result.GetValue<string[]>(default!);
            Assert.That(namespaces, Is.Not.Empty);
            Assert.That(namespaces[0], Is.EqualTo(Namespaces.OpcUa));
        }

        [Test]
        public async Task ReadServerArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] serverArray = result.GetValue<string[]>(default!);
            Assert.That(serverArray, Is.Not.Empty);
        }

        [Test]
        public async Task ReadServerStatusStateAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.GetValue<int>(default), Is.EqualTo((int)ServerState.Running));
        }

        [Test]
        public async Task ReadServerStatusCurrentTimeAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
        }

        [Test]
        public async Task ReadServerStatusStartTimeAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_StartTime).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out DateTimeUtc _), Is.True);
        }

        [Test]
        public async Task ReadBuildInfoProductNameAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_ProductName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out string _), Is.True);
            Assert.That(result.WrappedValue.GetString(), Is.Not.Empty);
        }

        [Test]
        public async Task ReadBuildInfoSoftwareVersionAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out string _), Is.True);
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = Attributes.Value }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
