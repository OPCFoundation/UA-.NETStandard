/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
    [TestFixture]
    [Category("Historian")]
    [Parallelizable]
    public sealed class HistoricalEventConfigurationInstallerTests
    {
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
