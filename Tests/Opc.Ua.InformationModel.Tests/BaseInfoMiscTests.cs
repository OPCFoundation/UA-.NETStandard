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

namespace Opc.Ua.Conformance.Tests.InformationModel
{
    /// <summary>
    /// compliance tests for miscellaneous base information model checks.
    /// Verifies event types, service level, and diagnostics summary nodes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoMisc")]
    public class BaseInfoMiscTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Base Info ServerType")]
        [Property("Tag", "001")]
        public async Task ReadServerServiceLevelAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServiceLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            byte serviceLevel = result.WrappedValue.GetByte();
            Assert.That(serviceLevel, Is.InRange((byte)0, (byte)255));
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.BaseEventType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasEventIdAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "EventId"),
                Is.True,
                "BaseEventType should have an EventId property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasEventTypeAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "EventType"),
                Is.True,
                "BaseEventType should have an EventType property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasSourceNodeAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "SourceNode"),
                Is.True,
                "BaseEventType should have a SourceNode property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasSourceNameAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "SourceName"),
                Is.True,
                "BaseEventType should have a SourceName property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasTimeAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "Time"),
                Is.True,
                "BaseEventType should have a Time property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasMessageAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "Message"),
                Is.True,
                "BaseEventType should have a Message property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyBaseEventTypeHasSeverityAsync()
        {
            BrowseResponse response = await BrowseChildrenAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(
                HasChildWithName(response.Results[0].References, "Severity"),
                Is.True,
                "BaseEventType should have a Severity property.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifySystemEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.SystemEventType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Device Failure")]
        [Property("Tag", "000")]
        public async Task VerifyDeviceFailureEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.DeviceFailureEventType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Events Capabilities")]
        [Property("Tag", "001")]
        public async Task VerifyAuditEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditEventType, Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "002")]
        public async Task ReadSessionDiagnosticsSummaryNodeAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSessionCount).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            uint count = result.WrappedValue.GetUInt32();
            Assert.That(count, Is.GreaterThanOrEqualTo(1u),
                "At least one session (the test session) should be active.");
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Diagnostics")]
        [Property("Tag", "001")]
        public async Task ReadServerDiagnosticsEnabledFlagAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            Assert.That(result.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Base Info Progress Events")]
        [Property("Tag", "001")]
        public async Task VerifyProgressEventTypeExistsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.ProgressEventType, Attributes.BrowseName).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("ProgressEventType is not supported by this server.");
            }
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

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<BrowseResponse> BrowseChildrenAsync(NodeId nodeId)
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
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
            return response;
        }

        private static bool HasChildWithName(
            ArrayOf<ReferenceDescription> references, string name)
        {
            foreach (ReferenceDescription r in references)
            {
                if (r.BrowseName.Name == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
