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
            string[] namespaces = result.GetValue<string[]>(default);
            Assert.That(namespaces, Is.Not.Empty);
            Assert.That(namespaces[0], Is.EqualTo(Namespaces.OpcUa));
        }

        [Test]
        public async Task ReadServerArrayAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerArray).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            string[] serverArray = result.GetValue<string[]>(default);
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
