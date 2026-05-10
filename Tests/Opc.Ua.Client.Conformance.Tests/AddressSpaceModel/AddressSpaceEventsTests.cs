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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for event-related address space structure.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceEvents")]
    public class AddressSpaceEventsTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task ServerObjectHasEventNotifierAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            byte eventNotifier = response.Results[0].GetValue<byte>(default);
            Assert.That(eventNotifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents),
                "Server object should support SubscribeToEvents.");
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task BaseEventTypeExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectTypeIds.BaseEventType,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            QualifiedName browseName = response.Results[0].GetValue<QualifiedName>(default);
            Assert.That(browseName.Name, Is.EqualTo("BaseEventType"));
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task BaseEventTypeHasMandatoryPropertiesAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectTypeIds.BaseEventType,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasProperty,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            var propertyNames = new List<string>();
            foreach (ReferenceDescription r in response.Results[0].References)
            {
                propertyNames.Add(r.BrowseName.Name);
            }

            Assert.That(propertyNames, Does.Contain("EventId"));
            Assert.That(propertyNames, Does.Contain("EventType"));
            Assert.That(propertyNames, Does.Contain("SourceNode"));
            Assert.That(propertyNames, Does.Contain("SourceName"));
            Assert.That(propertyNames, Does.Contain("Time"));
            Assert.That(propertyNames, Does.Contain("Message"));
            Assert.That(propertyNames, Does.Contain("Severity"));
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task AuditEventTypeExistsAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectTypeIds.AuditEventType,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task AuditEventTypeIsSubtypeOfBaseEventTypeAsync()
        {
            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = ObjectTypeIds.AuditEventType,
                        BrowseDirection = BrowseDirection.Inverse,
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            Assert.That(response.Results[0].References.Count, Is.GreaterThan(0));

            var parentId = ExpandedNodeId.ToNodeId(
                response.Results[0].References[0].NodeId, Session.NamespaceUris);
            Assert.That(parentId, Is.EqualTo(ObjectTypeIds.BaseEventType));
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Events")]
        [Property("Tag", "000")]
        public async Task ObjectsFolderHasEventNotifierAttributeAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.ObjectsFolder,
                        AttributeId = Attributes.EventNotifier
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
        }
    }
}
