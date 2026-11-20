// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
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
                propertyNames.Add(r.BrowseName.Name!);
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
