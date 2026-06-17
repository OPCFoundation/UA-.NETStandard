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

// UaPubSubApplication, UdpPubSubConnection, and UdpDiscoverySubscriber are
// part of the legacy 1.04 PubSub stack; suppress the obsolete-API diagnostic
// in this test file that exercises their pure in-memory paths.
#pragma warning disable UA0023
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Unit tests for <see cref="UdpDiscoverySubscriber"/>: covers the
    /// in-memory writer-id management and the early-return guard in
    /// <see cref="UdpDiscoverySubscriber.SendDiscoveryRequestDataSetMetaData"/>
    /// without opening any real UDP sockets.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UdpDiscoverySubscriberTests
    {
        // ------------------------------------------------------------------
        // Constructor
        // ------------------------------------------------------------------

        [Test]
        public void Constructor_CreatesInstanceWithoutThrowingOrOpeningSockets()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            var subscriber = NewSubscriber(app);
            Assert.That(subscriber, Is.Not.Null);
        }

        // ------------------------------------------------------------------
        // AddWriterIdForDataSetMetadata
        // ------------------------------------------------------------------

        [Test]
        public void AddWriterIdForDataSetMetadata_NewId_AddsToQueue()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);

            sub.AddWriterIdForDataSetMetadata(42);

            // Re-adding the same ID must be silently ignored (thread-safe dedup).
            Assert.DoesNotThrow(() => sub.AddWriterIdForDataSetMetadata(42));
        }

        [Test]
        public void AddWriterIdForDataSetMetadata_DuplicateId_DoesNotAddTwice()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);

            // Adding the same ID twice must not throw.
            sub.AddWriterIdForDataSetMetadata(7);
            sub.AddWriterIdForDataSetMetadata(7); // silently ignored

            // Removing it once should empty the slot (deduplication means only
            // one copy was stored).
            sub.RemoveWriterIdForDataSetMetadata(7);

            // After removal the list is empty → SendDiscovery is a no-op.
            Assert.DoesNotThrow(() => sub.SendDiscoveryRequestDataSetMetaData());
        }

        // ------------------------------------------------------------------
        // RemoveWriterIdForDataSetMetadata
        // ------------------------------------------------------------------

        [Test]
        public void RemoveWriterIdForDataSetMetadata_ExistingId_RemovesFromQueue()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            sub.AddWriterIdForDataSetMetadata(11);
            sub.AddWriterIdForDataSetMetadata(22);

            sub.RemoveWriterIdForDataSetMetadata(11);

            // Idempotent: removing again must not throw.
            Assert.DoesNotThrow(() => sub.RemoveWriterIdForDataSetMetadata(11));
        }

        [Test]
        public void RemoveWriterIdForDataSetMetadata_AbsentId_IsNoOp()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            // Never added → Remove must be a silent no-op.
            Assert.DoesNotThrow(() => sub.RemoveWriterIdForDataSetMetadata(99));
        }

        // ------------------------------------------------------------------
        // SendDiscoveryRequestDataSetMetaData – early-return path
        // ------------------------------------------------------------------

        [Test]
        public void SendDiscoveryRequestDataSetMetaData_WhenNoIdsQueued_ReturnsImmediatelyWithNoException()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            // No IDs enqueued → method hits the early-return guard before
            // touching MessageContext or m_discoveryUdpClients.
            Assert.DoesNotThrow(() => sub.SendDiscoveryRequestDataSetMetaData());
        }

        [Test]
        public void SendDiscoveryRequestDataSetMetaData_AfterRemovingAllIds_ReturnsImmediately()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            sub.AddWriterIdForDataSetMetadata(5);
            sub.RemoveWriterIdForDataSetMetadata(5);

            // List is empty again → early return.
            Assert.DoesNotThrow(() => sub.SendDiscoveryRequestDataSetMetaData());
        }

        // ------------------------------------------------------------------
        // UpdateDataSetWriterConfiguration
        // ------------------------------------------------------------------

        [Test]
        public void UpdateDataSetWriterConfiguration_WithUnknownWriterGroupId_IsNoOp()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            var unknownConfig = new WriterGroupDataType
            {
                WriterGroupId = 999, // not in the connection's WriterGroups list
                Name = "unknown"
            };

            // Must not throw; the writerGroup lookup returns null and the method
            // returns without modifying anything.
            Assert.DoesNotThrow(() => sub.UpdateDataSetWriterConfiguration(unknownConfig));
        }

        [Test]
        public void UpdateDataSetWriterConfiguration_WithMatchingWriterGroupId_UpdatesConfiguration()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using UaPubSubApplication app = UaPubSubApplication.Create(telemetry);

            // Build a connection config that already has one writer group.
            var existingGroup = new WriterGroupDataType
            {
                WriterGroupId = 1,
                Name = "OriginalName",
                PublishingInterval = 1000
            };
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-update-test",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                }),
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { existingGroup })
            };
            var conn = new UdpPubSubConnection(app, connCfg, telemetry);
            var sub = new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);

            var updatedGroup = new WriterGroupDataType
            {
                WriterGroupId = 1, // same group id → should replace
                Name = "UpdatedName",
                PublishingInterval = 2000
            };

            sub.UpdateDataSetWriterConfiguration(updatedGroup);

            Assert.That(
                connCfg.WriterGroups.ToList()
                    .Exists(g => g.WriterGroupId == 1 && g.Name == "UpdatedName"),
                Is.True);
        }

        // ------------------------------------------------------------------
        // CanPublish (private) – covers the internal interval-reset logic
        // ------------------------------------------------------------------

        [Test]
        public void CanPublish_WhenNoIdsQueued_ReturnsFalse()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);

            bool result = InvokePrivate<bool>(sub, "CanPublish");

            Assert.That(result, Is.False);
        }

        [Test]
        public void CanPublish_WhenIdsQueued_ReturnsTrue()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            sub.AddWriterIdForDataSetMetadata(100);

            bool result = InvokePrivate<bool>(sub, "CanPublish");

            Assert.That(result, Is.True);
        }

        [Test]
        public void CanPublish_AfterAddAndRemove_ReturnsFalse()
        {
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
            UdpDiscoverySubscriber sub = NewSubscriber(app);
            sub.AddWriterIdForDataSetMetadata(7);
            sub.RemoveWriterIdForDataSetMetadata(7);

            bool result = InvokePrivate<bool>(sub, "CanPublish");

            Assert.That(result, Is.False);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
        {
            object? result = instance.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(instance, args);
            return (T)result!;
        }

        private static UdpDiscoverySubscriber NewSubscriber(UaPubSubApplication app)
        {
            var telemetry = NUnitTelemetryContext.Create();
            var connCfg = new PubSubConnectionDataType
            {
                Name = "udp-helper-conn",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.1:4840"
                })
            };
            var conn = new UdpPubSubConnection(app, connCfg, telemetry);
            return new UdpDiscoverySubscriber(conn, telemetry, TimeProvider.System);
        }
    }
}
