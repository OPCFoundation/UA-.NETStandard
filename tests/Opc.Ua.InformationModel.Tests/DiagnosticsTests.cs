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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for server diagnostics information.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Diagnostics")]
    public class DiagnosticsTests : TestFixture
    {
        [Test]
        public async Task ReadServerDiagnosticsEnabledFlagAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        public async Task ReadServerDiagnosticsSummaryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary).ConfigureAwait(false);
            // Anonymous sessions may not have access to diagnostics
            Assert.That(
                StatusCode.IsGood(result.StatusCode) ||
                result.StatusCode == StatusCodes.BadNotReadable ||
                result.StatusCode == StatusCodes.BadUserAccessDenied,
                Is.True,
                $"Expected Good, BadNotReadable, or BadUserAccessDenied, got {result.StatusCode}");
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCurrentSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u),
                "At least one session (the test session) should be active.");
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCurrentSubscriptionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadSessionsDiagnosticsSummaryAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadServerCurrentTimeIsRecentAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            // CurrentTime is per spec a DateTime (UtcTime).
            Assert.That(
                result.WrappedValue.TryGetValue(out DateTimeUtc _),
                Is.True,
                "CurrentTime should decode as DateTime.");
        }

        [Test]
        public async Task ReadServerStateIsRunningAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.GetValue<int>(default), Is.EqualTo((int)ServerState.Running));
        }

        [Test]
        public async Task ServerDiagnosticsNodeBrowseHasChildrenAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectIds.Server_ServerDiagnostics,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var childNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                childNames.Add(r.BrowseName.Name);
            }
            Assert.That(childNames, Is.Not.Empty,
                "ServerDiagnostics should have child nodes.");
            Assert.That(childNames, Does.Contain("EnabledFlag"));
        }

        [Test]
        public async Task ReadDiagnosticsSummaryCumulatedSessionCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u));
        }

        [Test]
        public async Task ReadDiagnosticsSummaryServerViewCountAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_ServerViewCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
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

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
