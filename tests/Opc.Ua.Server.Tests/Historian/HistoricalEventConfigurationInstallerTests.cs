/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies historical event configuration metadata and refresh of advertised event types.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistoricalEventConfigurationInstallerTests
    {
        /// <summary>
        /// Verifies that installed event configuration contains supported event types and archive metadata.
        /// </summary>
        [Test]
        public async Task ConfigurationContainsEventTypesAndArchiveMetadataAsync()
        {
            ISystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            using var provider = new InMemoryHistorianProvider();
            DateTimeUtc startOfArchive = DateTime.UtcNow.AddHours(-2);
            var sortField = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [new QualifiedName(BrowseNames.Time)],
                AttributeId = Attributes.Value
            };
            provider.Register(notifier.NodeId, new HistorianNodeCapabilities
            {
                ReadRawData = false,
                ReadModifiedData = false,
                ReadAtTime = false,
                ReadProcessedData = false,
                ReadEventHistory = true,
                EventTypes = [ObjectTypeIds.BaseEventType],
                SortByEventFields = [sortField],
                StartOfArchive = startOfArchive,
                StartOfOnlineArchive = startOfArchive
            });

            HistoricalEventConfigurationState configuration =
                await HistoricalEventConfigurationInstaller.EnsureInstalledAsync(
                    context,
                    notifier,
                    provider,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(configuration.EventTypes, Is.Not.Null);
            Assert.That(
                configuration.EventTypes!.ReferenceExists(
                    ReferenceTypeIds.Organizes,
                    false,
                    ObjectTypeIds.BaseEventType),
                Is.True);
            Assert.That(configuration.StartOfArchive?.Value, Is.EqualTo(startOfArchive));
            Assert.That(
                configuration.StartOfOnlineArchive?.Value,
                Is.EqualTo(startOfArchive));
            Assert.That(configuration.SortByEventFields, Is.Not.Null);
            Assert.That(
                configuration.SortByEventFields!.Value.Count,
                Is.EqualTo(1));
            Assert.That(
                notifier.ReferenceExists(
                    ReferenceTypeIds.HasHistoricalConfiguration,
                    false,
                    configuration.NodeId),
                Is.True);
        }

        /// <summary>
        /// Verifies that configuration refresh removes event types no longer advertised by the provider.
        /// </summary>
        [Test]
        public async Task RefreshRemovesEventTypesNoLongerAdvertisedAsync()
        {
            ISystemContext context = CreateSystemContext();
            BaseObjectState notifier = CreateNotifier();
            using var provider = new InMemoryHistorianProvider();
            provider.Register(notifier.NodeId, new HistorianNodeCapabilities
            {
                ReadEventHistory = true,
                EventTypes =
                [
                    ObjectTypeIds.BaseEventType,
                    ObjectTypeIds.ConditionType
                ]
            });
            HistoricalEventConfigurationState first =
                await HistoricalEventConfigurationInstaller.EnsureInstalledAsync(
                    context,
                    notifier,
                    provider,
                    CancellationToken.None).ConfigureAwait(false);

            provider.SetCapabilities(notifier.NodeId, new HistorianNodeCapabilities
            {
                ReadEventHistory = true,
                EventTypes = [ObjectTypeIds.BaseEventType]
            });
            HistoricalEventConfigurationState second =
                await HistoricalEventConfigurationInstaller.EnsureInstalledAsync(
                    context,
                    notifier,
                    provider,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(second, Is.SameAs(first));
            Assert.That(
                second.EventTypes!.ReferenceExists(
                    ReferenceTypeIds.Organizes,
                    false,
                    ObjectTypeIds.BaseEventType),
                Is.True);
            Assert.That(
                second.EventTypes.ReferenceExists(
                    ReferenceTypeIds.Organizes,
                    false,
                    ObjectTypeIds.ConditionType),
                Is.False);
        }

        private static BaseObjectState CreateNotifier()
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1)
            };
        }

        private static ServerSystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:event-configuration");
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree)
                .Returns(new TypeTable(namespaceUris));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(telemetry);
            return new ServerSystemContext(server.Object);
        }
    }
}
