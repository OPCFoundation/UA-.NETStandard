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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    [TestFixture]
    public sealed class ServerAdapterReloadCoordinatorTests
    {
        [Test]
        public async Task ConfigurationChangeAddsPublisherSourceAndReplacesApplicationAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PublishedDataSetDataType second = CreateDataSet("PDS2", 2u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first]);
            PubSubConfigurationDataType configB = AdapterTestHelpers.Configuration(500, [first, second]);
            TestContext context = CreateContext(configA);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            await context.Store.SaveAsync(configB).ConfigureAwait(false);
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            Assert.That(context.Sources.TryGetSource("PDS2", out IPublishedDataSetSource? source), Is.True);
            Assert.That(source, Is.Not.Null);
            context.Application.Verify(a => a.ReplaceConfigurationAsync(
                configB, It.IsAny<CancellationToken>()), Times.Once);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ConfigurationChangeRemovesPublisherSourceAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PublishedDataSetDataType second = CreateDataSet("PDS2", 2u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first, second]);
            PubSubConfigurationDataType configB = AdapterTestHelpers.Configuration(500, [first]);
            TestContext context = CreateContext(configA);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            Assert.That(context.Sources.TryGetSource("PDS2", out _), Is.True);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            await context.Store.SaveAsync(configB).ConfigureAwait(false);
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            Assert.That(context.Sources.TryGetSource("PDS2", out _), Is.False);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task UnchangedPublisherSourceIsPreservedAcrossReloadAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PublishedDataSetDataType second = CreateDataSet("PDS2", 2u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first]);
            PubSubConfigurationDataType configB = AdapterTestHelpers.Configuration(500, [first, second]);
            TestContext context = CreateContext(configA);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            Assert.That(context.Sources.TryGetSource("PDS1", out IPublishedDataSetSource? before), Is.True);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            await context.Store.SaveAsync(configB).ConfigureAwait(false);
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            Assert.That(context.Sources.TryGetSource("PDS1", out IPublishedDataSetSource? after), Is.True);
            Assert.That(after, Is.SameAs(before));
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task PublisherOptionsChangeDoesNotDisturbSubscriberSinkAsync()
        {
            PubSubConfigurationDataType configuration = CreatePublisherSubscriberConfiguration();
            TestContext context = CreateContext(configuration);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.RegisterSubscriberBinding("subscriber1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            Assert.That(context.Sinks.TryGetSink("Reader1", out ISubscribedDataSetSink? before), Is.True);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            context.PublisherOptions.Set(
                "publisher1",
                new ServerPublisherOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://changed:4840" }
                });
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            Assert.That(context.Sinks.TryGetSink("Reader1", out ISubscribedDataSetSink? after), Is.True);
            Assert.That(after, Is.SameAs(before));
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ReloadNowLoadsCurrentConfigurationAndAppliesSubscriberAsync()
        {
            PubSubConfigurationDataType initial = AdapterTestHelpers.Configuration(
                500, [CreateDataSet("PDS1", 1u)]);
            PubSubConfigurationDataType reloaded = CreatePublisherSubscriberConfiguration();
            TestContext context = CreateContext(initial);
            context.Coordinator.RegisterSubscriberBinding("subscriber1");
            context.Coordinator.ApplyInitialConfiguration(initial, CreateBuilder());
            await context.Store.SaveAsync(reloaded).ConfigureAwait(false);

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            Assert.That(context.Sinks.TryGetSink("Reader1", out ISubscribedDataSetSink? sink), Is.True);
            Assert.That(sink, Is.Not.Null);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionPublisherReloadAddsReferencedSourceAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PublishedDataSetDataType second = CreateDataSet("PDS2", 2u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first]);
            PubSubConfigurationDataType configB = AdapterTestHelpers.Configuration(500, [first, second]);
            TestContext context = CreateContext(configA);
            context.PublisherOptions.Set(
                "publisher1",
                new ServerPublisherOptions
                {
                    Connection = new ServerConnectionOptions
                    {
                        EndpointUrl = "opc.tcp://subscription-publisher:4840"
                    },
                    ReadMode = ReadMode.Subscription,
                    Affinity = SubscriptionAffinity.WriterGroup
                });
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            await context.Store.SaveAsync(configB).ConfigureAwait(false);
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            Assert.That(context.Sources.TryGetSource("PDS1", out _), Is.True);
            Assert.That(context.Sources.TryGetSource("PDS2", out IPublishedDataSetSource? source), Is.True);
            Assert.That(source, Is.Not.Null);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stable dataset names do not retain removed fields, callbacks, metadata handlers, or source instances.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF40StableNameReloadWithdrawsOldField(bool removeDataSet)
        {
            var publicNode = new NodeId(501u);
            var confidentialNode = new NodeId(502u);
            var values = new Dictionary<(NodeId, uint), DataValue>
            {
                [(publicNode, Attributes.Value)] = new DataValue(10),
                [(confidentialNode, Attributes.Value)] = new DataValue(999)
            };
            var servers = new List<AdapterSecuritySession>();
            PublishedDataSetDataType original = AdapterSecuritySession.DataSet(
                "Stable", PublishedVariable(publicNode), PublishedVariable(confidentialNode));
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(500, [original]);
            await using TestContext context = CreateContext(configuration, () =>
            {
                var server = new AdapterSecuritySession(values);
                servers.Add(server);
                return server.Session;
            });
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource before = GetSecuritySource(context);
            DataSetMetaDataType oldMetadata = before.BuildMetaData();
            PublishedDataSetSnapshot originalSnapshot = await before.SampleAsync(oldMetadata).ConfigureAwait(false);
            Assert.That(originalSnapshot.Fields.Count, Is.EqualTo(2));
            Assert.That(originalSnapshot.Fields[1].Value, Is.EqualTo(Variant.From(999)));
            int retiredMetadataNotifications = 0;
            ((IMetaDataChangeNotifier)before).MetaDataChanged += (_, _) => retiredMetadataNotifications++;
            FakeDataChangeSubscription oldSubscription = servers[0].Subscriptions[0];
            Action admittedNotification = oldSubscription.CaptureNotification(
                2, confidentialNode, new DataValue(123456));

            var replacement = (PubSubConfigurationDataType)configuration.Clone();
            replacement.PublishedDataSets = removeDataSet
                ? []
                : [AdapterSecuritySession.DataSet("Stable", PublishedVariable(publicNode))];
            context.Store.SetConfiguration(replacement);
            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);
            admittedNotification();
            servers[0].Session.Raise(s => s.ModelChanged += null, EventArgs.Empty);

            Assert.That(oldSubscription.Disposed, Is.True);
            Assert.That(retiredMetadataNotifications, Is.Zero);
            servers[0].Session.VerifyRemove(s => s.ModelChanged -= It.IsAny<EventHandler>(), Times.Once);
            servers[0].Session.Verify(s => s.DisposeAsync(), Times.Once);
            Assert.That(
                async () => await before.SampleAsync(oldMetadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
            if (removeDataSet)
            {
                Assert.That(context.Sources.TryGetSource("Stable", out _), Is.False);
            }
            else
            {
                IPublishedDataSetSource after = GetSecuritySource(context);
                Assert.That(after, Is.Not.SameAs(before));
                Assert.That(after.BuildMetaData().Fields.Count, Is.EqualTo(1));
                Assert.That(servers[1].Subscriptions[0].MonitoredItems, Has.Count.EqualTo(1));
                Assert.That(servers[1].Subscriptions[0].MonitoredItems[0].NodeId, Is.EqualTo(publicNode));
                servers[1].Notify(publicNode, Attributes.Value, new DataValue(55));
                PublishedDataSetSnapshot snapshot = await after.SampleAsync(after.BuildMetaData())
                    .ConfigureAwait(false);
                Assert.That(snapshot.Fields.Count, Is.EqualTo(1));
                Assert.That(snapshot.Fields[0].Value, Is.EqualTo(Variant.From(55)));
            }
            context.Application.Verify(a => a.ReplaceConfigurationAsync(
                replacement, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Same-count and in-place definition edits replace the source and its monitoring rather than its name alone.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The published data set has no variable definition.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="change"/> is not a supported definition edit.
        /// </exception>
        [TestCase("Remap")]
        [TestCase("Order")]
        [TestCase("Attribute")]
        [TestCase("Range")]
        [TestCase("Metadata")]
        [TestCase("Sampling")]
        [TestCase("WriterInterval")]
        [Category("SecurityRegression")]
        public async Task SecurityF40StableNameReloadRebuildsChangedDefinition(string change)
        {
            var firstNode = new NodeId(511u);
            var secondNode = new NodeId(512u);
            var remappedNode = new NodeId(513u);
            var originalValue = Variant.From(s_completeSourceValues);
            var values = new Dictionary<(NodeId, uint), DataValue>
            {
                [(firstNode, Attributes.Value)] = new DataValue(originalValue),
                [(firstNode, Attributes.Description)] = new DataValue(Variant.From("public description")),
                [(secondNode, Attributes.Value)] = new DataValue(99),
                [(remappedNode, Attributes.Value)] = new DataValue(77)
            };
            var servers = new List<AdapterSecuritySession>();
            PublishedDataSetDataType original = AdapterSecuritySession.DataSet(
                "Stable", PublishedVariable(firstNode), PublishedVariable(secondNode));
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(500, [original]);
            await using TestContext context = CreateContext(configuration, () =>
            {
                var server = new AdapterSecuritySession(values);
                servers.Add(server);
                return server.Session;
            });
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource before = GetSecuritySource(context);
            DataSetMetaDataType oldMetadata = before.BuildMetaData();
            Assert.That(original.DataSetSource.TryGetValue(out PublishedDataItemsDataType? items), Is.True);
            PublishedDataItemsDataType published = items ?? throw new InvalidOperationException("Missing variables.");
            Variant expected = originalValue;
            switch (change)
            {
                case "Remap":
                    published.PublishedData[0].PublishedVariable = remappedNode;
                    expected = Variant.From(77);
                    break;
                case "Order":
                    published.PublishedData = [published.PublishedData[1], published.PublishedData[0]];
                    expected = Variant.From(99);
                    break;
                case "Attribute":
                    published.PublishedData[0].AttributeId = Attributes.Description;
                    expected = Variant.From("public description");
                    break;
                case "Range":
                    published.PublishedData[0].IndexRange = "1:2";
                    expected = Variant.From(s_remappedProjection);
                    break;
                case "Metadata":
                    original.DataSetMetaData.Fields[0].Name = "Renamed";
                    original.DataSetMetaData.ConfigurationVersion.MajorVersion = 2;
                    break;
                case "Sampling":
                    published.PublishedData[0].SamplingIntervalHint = 100;
                    break;
                case "WriterInterval":
                    configuration.Connections[0].WriterGroups[0].PublishingInterval = 250;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }

            PublishedDataSetSnapshot unchanged = await before.SampleAsync(oldMetadata).ConfigureAwait(false);
            Assert.That(unchanged.Fields[0].Value, Is.EqualTo(originalValue),
                "Mutating the store's configuration must not mutate a running source.");
            FakeDataChangeSubscription oldSubscription = servers[0].Subscriptions[0];
            Action admittedNotification = oldSubscription.CaptureNotification(1, firstNode, new DataValue(123456));
            context.Store.SetConfiguration(configuration);
            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);
            admittedNotification();

            IPublishedDataSetSource after = GetSecuritySource(context);
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(oldSubscription.Disposed, Is.True);
            Assert.That(servers[1].Subscriptions[0].MonitoredItems, Has.Count.EqualTo(published.PublishedData.Count));
            for (int i = 0; i < published.PublishedData.Count; i++)
            {
                Assert.That(servers[1].Subscriptions[0].MonitoredItems[i].NodeId,
                    Is.EqualTo(published.PublishedData[i].PublishedVariable));
                Assert.That(servers[1].Subscriptions[0].MonitoredItems[i].AttributeId,
                    Is.EqualTo(published.PublishedData[i].AttributeId));
            }
            PublishedDataSetSnapshot snapshot = await after.SampleAsync(after.BuildMetaData()).ConfigureAwait(false);
            Assert.That(snapshot.Fields.Count, Is.EqualTo(2));
            Assert.That(snapshot.Fields[0].Value, Is.EqualTo(expected));
            if (change == "Metadata")
            {
                Assert.That(after.BuildMetaData().Fields[0].Name, Is.EqualTo("Renamed"));
                Assert.That(snapshot.Fields[0].Name, Is.EqualTo("Renamed"));
                Assert.That(snapshot.MetaDataVersion.MajorVersion, Is.EqualTo(2));
            }
            if (change == "Range")
            {
                Assert.That(servers[1].Subscriptions[0].IndexRanges[0], Is.EqualTo("1:2"));
                Assert.That(servers[1].Reads[0][0].IndexRange, Is.EqualTo("1:2"));
            }
            if (change == "Attribute")
            {
                Assert.That(servers[1].Subscriptions[0].MonitoredItems[0].AttributeId,
                    Is.EqualTo(Attributes.Description));
            }
            if (change == "Sampling")
            {
                Assert.That(servers[1].Subscriptions[0].MonitoredItems[0].SamplingMs, Is.EqualTo(100));
            }
            if (change == "WriterInterval")
            {
                servers[1].Session.Verify(s => s.CreateDataChangeSubscriptionAsync(
                    250, It.IsAny<CancellationToken>()), Times.Once);
            }
            Assert.That(
                async () => await before.SampleAsync(oldMetadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// Equivalent definitions preserve the live source, session, and subscription and still receive updates.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF40UnchangedSubscriptionPreservesLiveSource()
        {
            var node = new NodeId(521u);
            var server = new AdapterSecuritySession(new Dictionary<(NodeId, uint), DataValue>
            {
                [(node, Attributes.Value)] = new DataValue(10)
            });
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(node))]);
            await using TestContext context = CreateContext(configuration, () => server.Session);
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource before = GetSecuritySource(context);
            context.Store.SetConfiguration((PubSubConfigurationDataType)configuration.Clone());

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);
            server.Notify(node, Attributes.Value, new DataValue(60));

            IPublishedDataSetSource after = GetSecuritySource(context);
            Assert.That(after, Is.SameAs(before));
            Assert.That(server.Subscriptions, Has.Count.EqualTo(1));
            Assert.That(server.Subscriptions[0].Disposed, Is.False);
            PublishedDataSetSnapshot snapshot = await after.SampleAsync(after.BuildMetaData()).ConfigureAwait(false);
            Assert.That(snapshot.Fields[0].Value, Is.EqualTo(Variant.From(60)));
        }

        /// <summary>
        /// Failed replacement startup is fail-closed, releases both generations, and permits a later valid retry.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF40FailedRebuildRetiresOldSourceAndCanRecover()
        {
            var oldNode = new NodeId(531u);
            var replacementNode = new NodeId(532u);
            var servers = new List<AdapterSecuritySession>();
            var values = new Dictionary<(NodeId, uint), DataValue>
            {
                [(oldNode, Attributes.Value)] = new DataValue(999),
                [(replacementNode, Attributes.Value)] = new DataValue(77)
            };
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(oldNode))]);
            await using TestContext context = CreateContext(configuration, () =>
            {
                var server = new AdapterSecuritySession(values);
                servers.Add(server);
                if (servers.Count == 2)
                {
                    server.Session.Setup(s => s.ReadAsync(
                            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                        .Throws(new ServiceResultException(StatusCodes.BadNotConnected));
                }
                return server.Session;
            });
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource before = GetSecuritySource(context);
            DataSetMetaDataType oldMetadata = before.BuildMetaData();
            context.Store.SetConfiguration(AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(replacementNode))]));

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            Assert.That(context.Sources.TryGetSource("Stable", out _), Is.False);
            Assert.That(servers[0].Subscriptions[0].Disposed, Is.True);
            Assert.That(servers[1].Subscriptions[0].Disposed, Is.True);
            servers[0].Session.Verify(s => s.DisposeAsync(), Times.Once);
            servers[1].Session.Verify(s => s.DisposeAsync(), Times.Once);
            Assert.That(
                async () => await before.SampleAsync(oldMetadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
            context.Application.Verify(a => a.ReplaceConfigurationAsync(
                It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()), Times.Never);

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            IPublishedDataSetSource after = GetSecuritySource(context);
            Assert.That(after, Is.Not.SameAs(before));
            PublishedDataSetSnapshot snapshot = await after.SampleAsync(after.BuildMetaData()).ConfigureAwait(false);
            Assert.That(snapshot.Fields[0].Value, Is.EqualTo(Variant.From(77)));
            Assert.That(servers, Has.Count.EqualTo(3));
            Assert.That(servers[2].Subscriptions[0].MonitoredItems[0].NodeId, Is.EqualTo(replacementNode));
        }

        /// <summary>
        /// Reload before runtime startup removes, rather than retains and later restarts, the retired coordinator.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF40ReloadBeforeRuntimeStartDoesNotRestartRetiredCoordinator()
        {
            var oldNode = new NodeId(541u);
            var replacementNode = new NodeId(542u);
            var servers = new List<AdapterSecuritySession>();
            var values = new Dictionary<(NodeId, uint), DataValue>
            {
                [(oldNode, Attributes.Value)] = new DataValue(999),
                [(replacementNode, Attributes.Value)] = new DataValue(77)
            };
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(oldNode))]);
            await using TestContext context = CreateContext(configuration, () =>
            {
                var server = new AdapterSecuritySession(values);
                servers.Add(server);
                return server.Session;
            });
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            context.Store.SetConfiguration(AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(replacementNode))]));
            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            await context.Runtime.StartAsync().ConfigureAwait(false);

            Assert.That(servers[0].Subscriptions, Is.Empty);
            servers[0].Session.Verify(s => s.DisposeAsync(), Times.Once);
            Assert.That(servers[1].Subscriptions, Has.Count.EqualTo(1));
            IPublishedDataSetSource source = GetSecuritySource(context);
            PublishedDataSetSnapshot snapshot = await source.SampleAsync(source.BuildMetaData()).ConfigureAwait(false);
            Assert.That(snapshot.Fields[0].Value, Is.EqualTo(Variant.From(77)));
        }

        /// <summary>
        /// Read-mode changes retire the previous source and coordinator while preserving authorized sampling.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF40ReadModeReloadRetiresPreviousSourceAndSubscription()
        {
            var node = new NodeId(551u);
            var servers = new List<AdapterSecuritySession>();
            var values = new Dictionary<(NodeId, uint), DataValue>
            {
                [(node, Attributes.Value)] = new DataValue(70)
            };
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(node))]);
            await using TestContext context = CreateContext(configuration, () =>
            {
                var server = new AdapterSecuritySession(values);
                servers.Add(server);
                return server.Session;
            });
            UseSubscriptionPublisher(context);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource subscriptionSource = GetSecuritySource(context);
            DataSetMetaDataType metadata = subscriptionSource.BuildMetaData();
            context.PublisherOptions.Set("publisher1", new ServerPublisherOptions
            {
                Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://subscription-publisher:4840" },
                ReadMode = ReadMode.Cyclic
            }, notify: false);

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            Assert.That(servers[0].Subscriptions[0].Disposed, Is.True);
            IPublishedDataSetSource cyclicSource = GetSecuritySource(context);
            PublishedDataSetSnapshot cyclic = await cyclicSource.SampleAsync(cyclicSource.BuildMetaData())
                .ConfigureAwait(false);
            Assert.That(cyclic.Fields[0].Value, Is.EqualTo(Variant.From(70)));
            Assert.That(servers[1].Subscriptions, Is.Empty);
            Assert.That(
                async () => await subscriptionSource.SampleAsync(metadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());

            UseSubscriptionPublisher(context, notify: false);
            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);

            IPublishedDataSetSource restored = GetSecuritySource(context);
            PublishedDataSetSnapshot subscription = await restored.SampleAsync(restored.BuildMetaData())
                .ConfigureAwait(false);
            Assert.That(subscription.Fields[0].Value, Is.EqualTo(Variant.From(70)));
            Assert.That(servers[2].Subscriptions, Has.Count.EqualTo(1));
            Assert.That(
                async () => await cyclicSource.SampleAsync(metadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        /// <summary>
        /// A publisher reload disposes only its old subscription when another binding still leases the session.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF40ReloadRetiresOldSubscriptionWithoutClosingSharedSession()
        {
            var oldNode = new NodeId(561u);
            var replacementNode = new NodeId(562u);
            var server = new AdapterSecuritySession(new Dictionary<(NodeId, uint), DataValue>
            {
                [(oldNode, Attributes.Value)] = new DataValue(999),
                [(replacementNode, Attributes.Value)] = new DataValue(77)
            });
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(oldNode))]);
            await using TestContext context = CreateContext(configuration, () => server.Session);
            UseSubscriptionPublisher(context);
            await using ServerAdapterRuntime.ServerSessionLease sharedLease = context.Runtime.AcquireSession(
                context.PublisherOptions.Get("publisher1").Connection, AdapterTestHelpers.Telemetry());
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configuration, CreateBuilder());
            await context.Runtime.StartAsync().ConfigureAwait(false);
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            IPublishedDataSetSource before = GetSecuritySource(context);
            DataSetMetaDataType oldMetadata = before.BuildMetaData();
            Action admittedNotification = server.Subscriptions[0].CaptureNotification(
                1, oldNode, new DataValue(123456));
            context.Store.SetConfiguration(AdapterTestHelpers.Configuration(
                500, [AdapterSecuritySession.DataSet("Stable", PublishedVariable(replacementNode))]));

            await context.Coordinator.ReloadNowAsync().ConfigureAwait(false);
            admittedNotification();
            server.Notify(replacementNode, Attributes.Value, new DataValue(88));

            Assert.That(server.Subscriptions, Has.Count.EqualTo(2));
            Assert.That(server.Subscriptions[0].Disposed, Is.True);
            Assert.That(server.Subscriptions[1].Disposed, Is.False);
            server.Session.Verify(s => s.DisposeAsync(), Times.Never);
            IPublishedDataSetSource after = GetSecuritySource(context);
            PublishedDataSetSnapshot snapshot = await after.SampleAsync(after.BuildMetaData()).ConfigureAwait(false);
            Assert.That(snapshot.Fields[0].Value, Is.EqualTo(Variant.From(88)));
            Assert.That(
                async () => await before.SampleAsync(oldMetadata).ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task ActionResponderOptionsChangeRegistersHandlerOnApplicationAsync()
        {
            TestContext context = CreateContext(new PubSubConfigurationDataType());
            context.ActionOptions.Set(
                "action1",
                new ServerActionResponderOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://actions:4840" }
                });
            context.Coordinator.RegisterActionResponderBinding("action1");
            context.Coordinator.ApplyInitialConfiguration(new PubSubConfigurationDataType(), CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            var target = new PubSubActionTarget { ActionName = "DoIt" };

            context.ActionOptions.Set(
                "action1",
                new ServerActionResponderOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://actions:4840" },
                    AllowUnsecured = true,
                    MethodMap = new ActionMethodMap().Add("DoIt", new NodeId(10u), new NodeId(11u)),
                    Targets = [target]
                });
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            context.Application.Verify(
                a => a.ClearActionHandlers(),
                Times.Once);
            context.Application.Verify(
                a => a.RegisterActionHandler(
                    target,
                    It.IsAny<IPubSubActionHandler>(),
                    true,
                    It.IsAny<PubSubResponseAddressPolicy?>()),
                Times.Once);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ActionResponderAllowUnsecuredTighteningClearsAndReregistersAsync()
        {
            var target = new PubSubActionTarget { ActionName = "DoIt" };
            TestContext context = CreateContext(new PubSubConfigurationDataType());
            context.ActionOptions.Set(
                "action1",
                new ServerActionResponderOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://actions:4840" },
                    AllowUnsecured = true,
                    MethodMap = new ActionMethodMap().Add("DoIt", new NodeId(10u), new NodeId(11u)),
                    Targets = [target]
                });
            context.Coordinator.RegisterActionResponderBinding("action1");
            context.Coordinator.ApplyInitialConfiguration(new PubSubConfigurationDataType(), CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            context.ActionOptions.Set(
                "action1",
                new ServerActionResponderOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://actions:4840" },
                    AllowUnsecured = false,
                    MethodMap = new ActionMethodMap().Add("DoIt", new NodeId(10u), new NodeId(11u)),
                    Targets = [target]
                });
            await WaitForReplaceAsync(context).ConfigureAwait(false);

            context.Application.Verify(
                a => a.ClearActionHandlers(),
                Times.Once);
            context.Application.Verify(
                a => a.RegisterActionHandler(
                    target,
                    It.IsAny<IPubSubActionHandler>(),
                    false,
                    It.IsAny<PubSubResponseAddressPolicy?>()),
                Times.Once);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task BurstConfigurationChangesAreDebouncedAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first]);
            TestContext context = CreateContext(configA);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);

            await context.Store.SaveAsync(
                AdapterTestHelpers.Configuration(500, [first, CreateDataSet("PDS2", 2u)]))
                .ConfigureAwait(false);
            await context.Store.SaveAsync(
                AdapterTestHelpers.Configuration(500, [first, CreateDataSet("PDS3", 3u)]))
                .ConfigureAwait(false);
            await context.Store.SaveAsync(
                AdapterTestHelpers.Configuration(500, [first, CreateDataSet("PDS4", 4u)]))
                .ConfigureAwait(false);
            await WaitForReplaceAsync(context).ConfigureAwait(false);
            await Task.Delay(350).ConfigureAwait(false);

            Assert.That(context.ReplaceCount, Is.EqualTo(1));
            Assert.That(context.Sources.TryGetSource("PDS4", out _), Is.True);
            await context.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DisposeAsyncWaitsForInFlightReloadAsync()
        {
            PublishedDataSetDataType first = CreateDataSet("PDS1", 1u);
            PubSubConfigurationDataType configA = AdapterTestHelpers.Configuration(500, [first]);
            PubSubConfigurationDataType configB = AdapterTestHelpers.Configuration(
                500, [first, CreateDataSet("PDS2", 2u)]);
            TestContext context = CreateContext(configA);
            context.Coordinator.RegisterPublisherBinding("publisher1");
            context.Coordinator.ApplyInitialConfiguration(configA, CreateBuilder());
            await context.Coordinator.StartAsync(context.Application.Object).ConfigureAwait(false);
            var replaceEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseReplace = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Application
                .Setup(a => a.ReplaceConfigurationAsync(
                    It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()))
                .Callback(() => replaceEntered.TrySetResult(true))
                .Returns(() => new ValueTask<ArrayOf<StatusCode>>(WaitForReleaseAsync(releaseReplace.Task)));

            Task reloadTask = context.Store.SaveAsync(configB).AsTask();
            await replaceEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Task disposeTask = context.Coordinator.DisposeAsync().AsTask();

            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(disposeTask.IsCompleted, Is.False);

            releaseReplace.SetResult(true);
            await Task.WhenAll(reloadTask, disposeTask).ConfigureAwait(false);
            await context.Runtime.DisposeAsync().ConfigureAwait(false);
        }

        private static TestContext CreateContext(
            PubSubConfigurationDataType configuration,
            Func<Mock<IServerSession>>? createSession = null)
        {
            var store = new FakeConfigurationStore(configuration);
            var publisherOptions = new OptionsMonitorStub<ServerPublisherOptions>();
            publisherOptions.Set(
                "publisher1",
                new ServerPublisherOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://publisher:4840" }
                });
            var subscriberOptions = new OptionsMonitorStub<ServerSubscriberOptions>();
            subscriberOptions.Set(
                "subscriber1",
                new ServerSubscriberOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://subscriber:4840" }
                });
            var actionOptions = new OptionsMonitorStub<ServerActionResponderOptions>();
            actionOptions.Set(
                "action1",
                new ServerActionResponderOptions
                {
                    Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://action:4840" }
                });
            var factory = new Mock<IServerSessionFactory>();
            factory
                .Setup(f => f.Create(
                    It.IsAny<ServerConnectionOptions>(), It.IsAny<ITelemetryContext>()))
                .Returns(() =>
                {
                    if (createSession is not null)
                    {
                        return createSession().Object;
                    }
                    Mock<IServerSession> session = AdapterTestHelpers.ConnectedSession();
                    session
                        .Setup(s => s.CreateDataChangeSubscriptionAsync(
                            It.IsAny<double>(), It.IsAny<CancellationToken>()))
                        .Returns(() =>
                            new ValueTask<IDataChangeSubscription>(new FakeDataChangeSubscription()));
                    return session.Object;
                });
            var runtime = new ServerAdapterRuntime(factory.Object);
            var sources = new MutableDataSetSourceProvider();
            var sinks = new MutableDataSetSinkProvider();
            var application = new Mock<IPubSubApplication>();
            int replaceCount = 0;
            application
                .Setup(a => a.ReplaceConfigurationAsync(
                    It.IsAny<PubSubConfigurationDataType>(), It.IsAny<CancellationToken>()))
                .Callback(() => replaceCount++)
                .Returns(new ValueTask<ArrayOf<StatusCode>>([]));

            var metrics = new AdapterMetrics();
            var coordinator = new ServerAdapterReloadCoordinator(
                store,
                publisherOptions,
                subscriberOptions,
                actionOptions,
                sources,
                sinks,
                runtime,
                AdapterTestHelpers.Telemetry(),
                metrics);
            return new TestContext(
                store,
                publisherOptions,
                subscriberOptions,
                actionOptions,
                runtime,
                sources,
                sinks,
                application,
                coordinator,
                metrics,
                () => replaceCount);
        }

        private static PubSubApplicationBuilder CreateBuilder()
        {
            return new PubSubApplicationBuilder(AdapterTestHelpers.Telemetry());
        }

        private static PublishedDataSetDataType CreateDataSet(string name, uint nodeId)
        {
            return AdapterTestHelpers.PublishedDataSet(
                name, AdapterTestHelpers.Variable.Value(new NodeId(nodeId)));
        }

        /// <summary>
        /// Configures the test binding before its option notifications are subscribed.
        /// </summary>
        private static void UseSubscriptionPublisher(TestContext context, bool notify = true)
        {
            context.PublisherOptions.Set("publisher1", new ServerPublisherOptions
            {
                Connection = new ServerConnectionOptions { EndpointUrl = "opc.tcp://subscription-publisher:4840" },
                ReadMode = ReadMode.Subscription,
                Affinity = SubscriptionAffinity.WriterGroup
            }, notify);
        }

        /// <summary>
        /// Creates one whole-value published variable.
        /// </summary>
        private static PublishedVariableDataType PublishedVariable(NodeId node)
        {
            return new PublishedVariableDataType { PublishedVariable = node, AttributeId = Attributes.Value };
        }

        /// <summary>
        /// Retrieves the real source registered by the reload coordinator.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The expected publication source is not registered.
        /// </exception>
        private static IPublishedDataSetSource GetSecuritySource(TestContext context)
        {
            Assert.That(context.Sources.TryGetSource("Stable", out IPublishedDataSetSource? source), Is.True);
            return source ?? throw new InvalidOperationException("The publication source was not registered.");
        }

        private static PubSubConfigurationDataType CreatePublisherSubscriberConfiguration()
        {
            PubSubConfigurationDataType configuration = AdapterTestHelpers.Configuration(
                500, [CreateDataSet("PDS1", 1u)]);
            var reader = new DataSetReaderDataType
            {
                Name = "Reader1",
                SubscribedDataSet = new ExtensionObject(new TargetVariablesDataType
                {
                    TargetVariables =
                    [
                        new FieldTargetDataType
                        {
                            TargetNodeId = new NodeId(100u),
                            AttributeId = Attributes.Value
                        }
                    ]
                })
            };
            var readerGroup = new ReaderGroupDataType
            {
                Name = "ReaderGroup1",
                DataSetReaders = new[] { reader }.ToArrayOf()
            };
            configuration.Connections[0].ReaderGroups = new[] { readerGroup }.ToArrayOf();
            return configuration;
        }

        private static async Task WaitForReplaceAsync(TestContext context)
        {
            for (int i = 0; i < 20; i++)
            {
                if (context.ReplaceCount > 0)
                {
                    return;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.Fail("Timed out waiting for ReplaceConfigurationAsync.");
        }

        private static async Task<ArrayOf<StatusCode>> WaitForReleaseAsync(Task release)
        {
            await release.ConfigureAwait(false);
            return [];
        }

        private sealed class TestContext : IAsyncDisposable
        {
            public TestContext(
                FakeConfigurationStore store,
                OptionsMonitorStub<ServerPublisherOptions> publisherOptions,
                OptionsMonitorStub<ServerSubscriberOptions> subscriberOptions,
                OptionsMonitorStub<ServerActionResponderOptions> actionOptions,
                ServerAdapterRuntime runtime,
                MutableDataSetSourceProvider sources,
                MutableDataSetSinkProvider sinks,
                Mock<IPubSubApplication> application,
                ServerAdapterReloadCoordinator coordinator,
                AdapterMetrics metrics,
                Func<int> replaceCount)
            {
                Store = store;
                PublisherOptions = publisherOptions;
                SubscriberOptions = subscriberOptions;
                ActionOptions = actionOptions;
                Runtime = runtime;
                Sources = sources;
                Sinks = sinks;
                Application = application;
                Coordinator = coordinator;
                m_metrics = metrics;
                m_replaceCount = replaceCount;
            }

            public FakeConfigurationStore Store { get; }

            public OptionsMonitorStub<ServerPublisherOptions> PublisherOptions { get; }

            public OptionsMonitorStub<ServerSubscriberOptions> SubscriberOptions { get; }

            public OptionsMonitorStub<ServerActionResponderOptions> ActionOptions { get; }

            public ServerAdapterRuntime Runtime { get; }

            public MutableDataSetSourceProvider Sources { get; }

            public MutableDataSetSinkProvider Sinks { get; }

            public Mock<IPubSubApplication> Application { get; }

            public ServerAdapterReloadCoordinator Coordinator { get; }

            public int ReplaceCount => m_replaceCount();

            public async ValueTask DisposeAsync()
            {
                await Coordinator.DisposeAsync().ConfigureAwait(false);
                await Runtime.DisposeAsync().ConfigureAwait(false);
                m_metrics.Dispose();
            }

            private readonly Func<int> m_replaceCount;

            /// <summary>
            /// Test-owned adapter metrics.
            /// </summary>
            private readonly AdapterMetrics m_metrics;
        }

        private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
            where T : class, new()
        {
            public T CurrentValue => Get(Options.DefaultName);

            public T Get(string? name)
            {
                string key = name ?? Options.DefaultName;
                return m_values.TryGetValue(key, out T? value) ? value : new T();
            }

            public IDisposable OnChange(Action<T, string?> listener)
            {
                m_listeners.Add(listener);
                return new Subscription(() => m_listeners.Remove(listener));
            }

            public void Set(string name, T value, bool notify = true)
            {
                m_values[name] = value;
                if (!notify)
                {
                    return;
                }
                foreach (Action<T, string?> listener in m_listeners)
                {
                    listener(value, name);
                }
            }

            private readonly Dictionary<string, T> m_values = new(StringComparer.Ordinal);
            private readonly List<Action<T, string?>> m_listeners = [];
        }

        private sealed class Subscription : IDisposable
        {
            public Subscription(Action dispose)
            {
                m_dispose = dispose;
            }

            public void Dispose()
            {
                m_dispose();
            }

            private readonly Action m_dispose;
        }

        private sealed class FakeConfigurationStore : IPubSubConfigurationStore
        {
            public FakeConfigurationStore(PubSubConfigurationDataType configuration)
            {
                m_configuration = configuration;
            }

            public event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

            public ValueTask<PubSubConfigurationDataType> LoadAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<PubSubConfigurationDataType>(m_configuration);
            }

            public ValueTask SaveAsync(
                PubSubConfigurationDataType configuration,
                CancellationToken cancellationToken = default)
            {
                PubSubConfigurationDataType previous = m_configuration;
                m_configuration = configuration;
                Changed?.Invoke(this, new PubSubConfigurationChangedEventArgs(previous, configuration));
                return default;
            }

            /// <summary>
            /// Replaces stored configuration without scheduling a debounce, for explicit deterministic reload tests.
            /// </summary>
            public void SetConfiguration(PubSubConfigurationDataType configuration)
            {
                m_configuration = configuration;
            }

            public ValueTask<ConfigurationVersionDataType?> GetConfigurationVersionAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ConfigurationVersionDataType?>((ConfigurationVersionDataType?)null);
            }

            public ValueTask SetConfigurationVersionAsync(
                ConfigurationVersionDataType configurationVersion,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask<ConfigurationVersionDataType?> GetPublishedDataSetConfigurationVersionAsync(
                string publishedDataSetName,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ConfigurationVersionDataType?>((ConfigurationVersionDataType?)null);
            }

            public ValueTask SetPublishedDataSetConfigurationVersionAsync(
                string publishedDataSetName,
                ConfigurationVersionDataType configurationVersion,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            private PubSubConfigurationDataType m_configuration;
        }

        /// <summary>The initial complete source value used by stable-name definition reload cases.</summary>
        private static readonly ArrayOf<int> s_completeSourceValues = [10, 20, 30, 40];

        /// <summary>The selected elements after a range-only definition change.</summary>
        private static readonly ArrayOf<int> s_remappedProjection = [20, 30];
    }
}
