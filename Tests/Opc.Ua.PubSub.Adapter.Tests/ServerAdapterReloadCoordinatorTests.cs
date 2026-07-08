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

        private static TestContext CreateContext(PubSubConfigurationDataType configuration)
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

            var coordinator = new ServerAdapterReloadCoordinator(
                store,
                publisherOptions,
                subscriberOptions,
                actionOptions,
                sources,
                sinks,
                runtime,
                AdapterTestHelpers.Telemetry(),
                new AdapterMetrics());
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
            }

            private readonly Func<int> m_replaceCount;
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

            public void Set(string name, T value)
            {
                m_values[name] = value;
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
    }
}
