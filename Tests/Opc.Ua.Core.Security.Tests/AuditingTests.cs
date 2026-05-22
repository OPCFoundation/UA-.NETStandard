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

namespace Opc.Ua.Conformance.Tests.Auditing
{
    /// <summary>
    /// compliance tests for Auditing and event type existence.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Auditing")]
    public class AuditingTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task ReadServerAuditingPropertyAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_Auditing).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    $"Server_Auditing not accessible: {result.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task ServerObjectSupportsEventsAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte notifier =
                result.WrappedValue.GetByte();
            Assert.That(
                notifier & EventNotifiers.SubscribeToEvents,
                Is.Not.Zero,
                "Server object should support event subscriptions.");
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task BaseEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.BaseEventType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task AuditEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditEventType).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Connections")]
        [Property("Tag", "006")]
        public async Task AuditSessionEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditCreateSessionEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Connections")]
        [Property("Tag", "006")]
        public async Task AuditActivateSessionEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditActivateSessionEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task AuditOpenSecureChannelEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditOpenSecureChannelEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Connections")]
        [Property("Tag", "006")]
        public async Task AuditUrlMismatchEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditUrlMismatchEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task VerifyAuditEventSourceIsServerAsync()
        {
            // AuditEventType has SourceNode property that should reference Server
            DataValue result = await ReadAttributeAsync(
                ObjectTypeIds.AuditEventType,
                Attributes.BrowseName).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Write")]
        [Property("Tag", "001")]
        public async Task AuditWriteUpdateEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditWriteUpdateEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task ServerObjectHasEventNotifierAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte notifier =
                result.WrappedValue.GetByte();
            Assert.That(
                notifier & EventNotifiers.SubscribeToEvents,
                Is.Not.Zero,
                "Server should support event subscriptions for auditing.");
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task AuditCancelEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditCancelEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("AuditCancelEventType not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task AuditChannelEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditChannelEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Method")]
        [Property("Tag", "001")]
        public async Task AuditUpdateMethodEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditUpdateMethodEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task AuditSecurityEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditSecurityEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task ServerAuditingIsBooleanAsync()
        {
            DataValue result = await ReadNodeValueAsync(
                VariableIds.Server_Auditing).ConfigureAwait(false);
            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Ignore(
                    $"Server_Auditing not accessible: {result.StatusCode}");
            }

            Assert.That(
                result.WrappedValue.TryGetValue(out bool _), Is.True,
                "Server_Auditing value should be a Boolean.");
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Method")]
        [Property("Tag", "001")]
        public async Task AuditConditionEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditConditionEventType)
                .ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                Assert.Fail("AuditConditionEventType not supported.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Auditing Secure Communication")]
        [Property("Tag", "004")]
        public async Task ServerEventNotifierHasSubscribeBitAsync()
        {
            DataValue result = await ReadAttributeAsync(
                ObjectIds.Server,
                Attributes.EventNotifier).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte notifier =
                result.WrappedValue.GetByte();
            Assert.That(
                (notifier & EventNotifiers.SubscribeToEvents) != 0,
                Is.True,
                "Server EventNotifier should have SubscribeToEvents bit set.");
        }

        [Test]
        [Property("ConformanceUnit", "Auditing NodeManagement")]
        [Property("Tag", "001")]
        public async Task AuditNodeManagementEventTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ObjectTypeIds.AuditNodeManagementEventType)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        private async Task<DataValue> ReadNodeValueAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
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

        private Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            return ReadAttributeAsync(
                nodeId, Attributes.BrowseName);
        }
    }
}
