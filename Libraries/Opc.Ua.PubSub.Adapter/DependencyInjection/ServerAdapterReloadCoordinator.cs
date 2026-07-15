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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Diagnostics;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;

namespace Opc.Ua.PubSub.Adapter.DependencyInjection
{
    /// <summary>
    /// Incrementally rewires the external-server PubSub adapter when the
    /// PubSub configuration or named adapter options change.
    /// </summary>
    internal sealed class ServerAdapterReloadCoordinator : IAsyncDisposable
    {
        public ServerAdapterReloadCoordinator(
            IPubSubConfigurationStore configurationStore,
            IOptionsMonitor<ServerPublisherOptions> publisherOptions,
            IOptionsMonitor<ServerSubscriberOptions> subscriberOptions,
            IOptionsMonitor<ServerActionResponderOptions> actionOptions,
            IDataSetSourceProvider sourceProvider,
            IDataSetSinkProvider sinkProvider,
            ServerAdapterRuntime runtime,
            ITelemetryContext telemetry,
            AdapterMetrics metrics)
        {
            m_configurationStore = configurationStore ?? throw new ArgumentNullException(nameof(configurationStore));
            m_publisherOptions = publisherOptions ?? throw new ArgumentNullException(nameof(publisherOptions));
            m_subscriberOptions = subscriberOptions ?? throw new ArgumentNullException(nameof(subscriberOptions));
            m_actionOptions = actionOptions ?? throw new ArgumentNullException(nameof(actionOptions));
            m_sources = sourceProvider as MutableDataSetSourceProvider
                ?? throw new InvalidOperationException(
                    "The external-server adapter requires a mutable data-set source provider.");
            m_sinks = sinkProvider as MutableDataSetSinkProvider
                ?? throw new InvalidOperationException(
                    "The external-server adapter requires a mutable data-set sink provider.");
            m_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            m_logger = telemetry.CreateLogger<ServerAdapterReloadCoordinator>();
        }

        public void RegisterPublisherBinding(string optionsName)
        {
            RegisterBinding(AdapterBindingKind.Publisher, optionsName);
        }

        public void RegisterSubscriberBinding(string optionsName)
        {
            RegisterBinding(AdapterBindingKind.Subscriber, optionsName);
        }

        public void RegisterActionResponderBinding(string optionsName)
        {
            RegisterBinding(AdapterBindingKind.ActionResponder, optionsName);
        }

        public void ApplyInitialConfiguration(
            PubSubConfigurationDataType configuration,
            PubSubApplicationBuilder builder)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            AdapterBinding[] bindings;
            lock (m_gate)
            {
                bindings = [.. m_bindings];
            }

            foreach (AdapterBinding binding in bindings)
            {
                switch (binding.Kind)
                {
                    case AdapterBindingKind.Publisher:
                        ApplyInitialPublisher(binding.OptionsName, configuration);
                        break;
                    case AdapterBindingKind.Subscriber:
                        ApplyInitialSubscriber(binding.OptionsName, configuration);
                        break;
                    case AdapterBindingKind.ActionResponder:
                        ApplyInitialActionResponder(binding.OptionsName, builder);
                        break;
                }
            }
        }

        public ValueTask StartAsync(
            IPubSubApplication application,
            CancellationToken cancellationToken = default)
        {
            if (application is null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            lock (m_gate)
            {
                if (m_disposed || m_started)
                {
                    return default;
                }

                m_application = application;
                m_started = true;
                m_configurationStore.Changed += OnConfigurationChanged;
                IDisposable? publisherSubscription = m_publisherOptions.OnChange(
                    (_, name) => OnOptionsChanged(AdapterBindingKind.Publisher, name));
                if (publisherSubscription is not null)
                {
                    m_optionSubscriptions.Add(publisherSubscription);
                }
                IDisposable? subscriberSubscription = m_subscriberOptions.OnChange(
                    (_, name) => OnOptionsChanged(AdapterBindingKind.Subscriber, name));
                if (subscriberSubscription is not null)
                {
                    m_optionSubscriptions.Add(subscriberSubscription);
                }
                IDisposable? actionSubscription = m_actionOptions.OnChange(
                    (_, name) => OnOptionsChanged(AdapterBindingKind.ActionResponder, name));
                if (actionSubscription is not null)
                {
                    m_optionSubscriptions.Add(actionSubscription);
                }
            }

            return default;
        }

        public async ValueTask ReloadNowAsync(CancellationToken cancellationToken = default)
        {
            PubSubConfigurationDataType configuration = await m_configurationStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            await ApplyConfigurationAsync(
                configuration, builder: null, replaceApplication: true, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            CancellationTokenSource? debounce;
            List<IDisposable> subscriptions;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;
                m_configurationStore.Changed -= OnConfigurationChanged;
                debounce = m_debounce;
                m_debounce = null;
                subscriptions = [.. m_optionSubscriptions];
                m_optionSubscriptions.Clear();
            }

            debounce?.Cancel();
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }

            try
            {
                await m_reloadLock.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                debounce?.Dispose();
                return;
            }

            PublisherBindingState[] publishers;
            SubscriberBindingState[] subscribers;
            ActionBindingState[] actions;
            try
            {
                publishers = [.. m_publishers.Values];
                subscribers = [.. m_subscribers.Values];
                actions = [.. m_actions.Values];
                m_publishers.Clear();
                m_subscribers.Clear();
                m_actions.Clear();
            }
            finally
            {
                m_reloadLock.Release();
            }

            foreach (PublisherBindingState publisher in publishers)
            {
                await publisher.DisposeAsync(m_sources).ConfigureAwait(false);
            }
            foreach (SubscriberBindingState subscriber in subscribers)
            {
                await subscriber.DisposeAsync(m_sinks).ConfigureAwait(false);
            }
            foreach (ActionBindingState action in actions)
            {
                await action.DisposeAsync().ConfigureAwait(false);
            }
            debounce?.Dispose();
            m_reloadLock.Dispose();
        }

        private void RegisterBinding(AdapterBindingKind kind, string optionsName)
        {
            if (optionsName is null)
            {
                throw new ArgumentNullException(nameof(optionsName));
            }

            var binding = new AdapterBinding(kind, optionsName);
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(ServerAdapterReloadCoordinator));
                }
                m_bindings.Add(binding);
            }
        }

        private void OnConfigurationChanged(object? sender, PubSubConfigurationChangedEventArgs e)
        {
            ScheduleReload(e.Current);
        }

        private void OnOptionsChanged(AdapterBindingKind kind, string? optionsName)
        {
            string name = optionsName ?? Options.DefaultName;
            lock (m_gate)
            {
                if (!m_bindings.Contains(new AdapterBinding(kind, name)))
                {
                    return;
                }
            }

            ScheduleReload(null);
        }

        private void ScheduleReload(PubSubConfigurationDataType? configuration)
        {
            CancellationTokenSource debounce;
            lock (m_gate)
            {
                if (m_disposed || !m_started)
                {
                    return;
                }

                m_pendingConfiguration = configuration ?? m_pendingConfiguration;
                m_debounce?.Cancel();
                m_debounce = new CancellationTokenSource();
                debounce = m_debounce;
            }

            _ = DebounceAndReloadAsync(debounce);
        }

        private async Task DebounceAndReloadAsync(CancellationTokenSource debounce)
        {
            try
            {
                await Task.Delay(s_debounceInterval, debounce.Token).ConfigureAwait(false);
                PubSubConfigurationDataType? configuration;
                lock (m_gate)
                {
                    if (m_disposed || !ReferenceEquals(m_debounce, debounce))
                    {
                        return;
                    }
                    configuration = m_pendingConfiguration;
                    m_pendingConfiguration = null;
                }

                configuration ??= await m_configurationStore
                        .LoadAsync(debounce.Token)
                        .ConfigureAwait(false);

                await ApplyConfigurationAsync(
                    configuration, builder: null, replaceApplication: true, debounce.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                m_logger.HotReloadFailed(ex);
            }
            finally
            {
                lock (m_gate)
                {
                    if (ReferenceEquals(m_debounce, debounce))
                    {
                        m_debounce = null;
                    }
                }
                debounce.Dispose();
            }
        }

        private async ValueTask ApplyConfigurationAsync(
            PubSubConfigurationDataType configuration,
            PubSubApplicationBuilder? builder,
            bool replaceApplication,
            CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
            }
            try
            {
                await m_reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            try
            {
                AdapterBinding[] bindings;
                IPubSubApplication? application = null;
                lock (m_gate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    bindings = [.. m_bindings];
                    if (replaceApplication && HasActionResponderBinding(bindings))
                    {
                        application = m_application;
                    }
                }

                application?.ClearActionHandlers();

                foreach (AdapterBinding binding in bindings)
                {
                    switch (binding.Kind)
                    {
                        case AdapterBindingKind.Publisher:
                            await ApplyPublisherAsync(
                                binding.OptionsName, configuration, cancellationToken).ConfigureAwait(false);
                            break;
                        case AdapterBindingKind.Subscriber:
                            await ApplySubscriberAsync(binding.OptionsName, configuration).ConfigureAwait(false);
                            break;
                        case AdapterBindingKind.ActionResponder:
                            await ApplyActionResponderAsync(
                                binding.OptionsName, builder, application, cancellationToken).ConfigureAwait(false);
                            break;
                    }
                }

                if (replaceApplication)
                {
                    lock (m_gate)
                    {
                        application = m_application;
                    }
                    if (application is not null)
                    {
                        await application.ReplaceConfigurationAsync(configuration, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (replaceApplication && ex is not OperationCanceledException)
            {
                m_logger.ApplyHotReloadFailed(ex);
            }
            finally
            {
                m_reloadLock.Release();
            }
        }

        private async ValueTask ApplyPublisherAsync(
            string optionsName,
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken)
        {
            ServerPublisherOptions options = m_publisherOptions.Get(optionsName);
            if (!m_publishers.TryGetValue(optionsName, out PublisherBindingState? state))
            {
                state = new PublisherBindingState();
                m_publishers.Add(optionsName, state);
            }

            List<PublishedDataSetDataType> dataSets = EnumeratePublishedDataSets(configuration);
            if (options.ReadMode == ReadMode.Subscription)
            {
                await ApplySubscriptionPublisherAsync(
                    state, options, configuration, dataSets, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (state.Session is null || !state.Connection.Equals(options.Connection))
            {
                await state.DisposeAsync(m_sources).ConfigureAwait(false);
                state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
                state.Connection = CloneConnectionOptions(options.Connection);
                state.Cyclic = new CyclicReadStrategy(state.Session.Session, m_telemetry, m_metrics);
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (PublishedDataSetDataType dataSet in dataSets)
            {
                string dataSetName = dataSet.Name ?? string.Empty;
                if (dataSetName.Length == 0)
                {
                    continue;
                }
                desired.Add(dataSetName);
                if (state.Items.TryGetValue(dataSetName, out PublisherItemState? existing) &&
                    Utils.IsEqual(existing.Configuration, dataSet))
                {
                    continue;
                }

                if (state.Items.TryGetValue(dataSetName, out PublisherItemState? oldItem))
                {
                    oldItem.Dispose();
                }

                // Ownership is transferred into PublisherItemState, which disposes the builder when the source is
                // removed. TODO: expose a disposable source wrapper so CA2000 can follow the ownership transfer.
#pragma warning disable CA2000
                DataSetMetaDataBuilder metaDataBuilder = CreateMetaDataBuilder(dataSet, state.Session.Session);
#pragma warning restore CA2000
                var source = new ServerPublishedDataSetSource(
                    dataSet, state.Cyclic!, metaDataBuilder, m_telemetry);
                m_sources.Register(dataSetName, source);
                state.Items[dataSetName] = new PublisherItemState(dataSet, source, metaDataBuilder);
            }

            foreach (string removed in GetRemovedKeys(state.Items.Keys, desired))
            {
                m_sources.Remove(removed);
                state.Items[removed].Dispose();
                state.Items.Remove(removed);
            }

            if (state.Items.Count == 0)
            {
                await state.DisposeAsync(m_sources).ConfigureAwait(false);
            }
        }

        private async ValueTask ApplySubscriptionPublisherAsync(
            PublisherBindingState state,
            ServerPublisherOptions options,
            PubSubConfigurationDataType configuration,
            List<PublishedDataSetDataType> dataSets,
            CancellationToken cancellationToken)
        {
            HashSet<string> referenced = CollectWriterDataSetNames(configuration);
            if (referenced.Count == 0)
            {
                await state.DisposeAsync(m_sources).ConfigureAwait(false);
                return;
            }

            bool recreate = state.Session is null ||
                !state.Connection.Equals(options.Connection) ||
                state.ReadMode != options.ReadMode ||
                state.Affinity != options.Affinity ||
                !SetEquals(state.ReferencedDataSets, referenced);
            if (!recreate)
            {
                return;
            }

            await state.DisposeAsync(m_sources).ConfigureAwait(false);
            state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
            state.Connection = CloneConnectionOptions(options.Connection);
            state.ReadMode = options.ReadMode;
            state.Affinity = options.Affinity;
            state.ReferencedDataSets = referenced;
            state.Coordinator = new SubscriptionCoordinator(
                configuration, state.Session.Session, options.Affinity, m_telemetry);
            await m_runtime.AddCoordinatorAsync(state.Coordinator, cancellationToken).ConfigureAwait(false);

            foreach (PublishedDataSetDataType dataSet in dataSets)
            {
                string dataSetName = dataSet.Name ?? string.Empty;
                if (dataSetName.Length == 0 || !referenced.Contains(dataSetName))
                {
                    continue;
                }

                IReadStrategy strategy = state.Coordinator.GetReadStrategy(dataSetName);
                // Ownership is transferred into PublisherItemState, which disposes the builder when the source is
                // removed. TODO: expose a disposable source wrapper so CA2000 can follow the ownership transfer.
#pragma warning disable CA2000
                DataSetMetaDataBuilder metaDataBuilder = CreateMetaDataBuilder(dataSet, state.Session.Session);
#pragma warning restore CA2000
                var source = new ServerPublishedDataSetSource(
                    dataSet, strategy, metaDataBuilder, m_telemetry);
                m_sources.Register(dataSetName, source);
                state.Items[dataSetName] = new PublisherItemState(dataSet, source, metaDataBuilder);
            }
        }

        private async ValueTask ApplySubscriberAsync(
            string optionsName,
            PubSubConfigurationDataType configuration)
        {
            ServerSubscriberOptions options = m_subscriberOptions.Get(optionsName);
            if (!m_subscribers.TryGetValue(optionsName, out SubscriberBindingState? state))
            {
                state = new SubscriberBindingState();
                m_subscribers.Add(optionsName, state);
            }

            if (state.Session is null || !state.Connection.Equals(options.Connection))
            {
                await state.DisposeAsync(m_sinks).ConfigureAwait(false);
                state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
                state.Connection = CloneConnectionOptions(options.Connection);
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataSetReaderDataType reader in EnumerateDataSetReaders(configuration))
            {
                string readerName = reader.Name ?? string.Empty;
                if (readerName.Length == 0 ||
                    reader.SubscribedDataSet.IsNull ||
                    !reader.SubscribedDataSet.TryGetValue(out TargetVariablesDataType? targetVariables) ||
                    targetVariables is null)
                {
                    continue;
                }

                desired.Add(readerName);
                if (state.Items.TryGetValue(readerName, out SubscriberItemState? existing) &&
                    Utils.IsEqual(existing.Configuration, targetVariables))
                {
                    continue;
                }

                ISubscribedDataSetSink sink = ServerSubscribedDataSetSink.Create(
                    targetVariables, state.Session.Session, m_telemetry, m_metrics);
                m_sinks.Register(readerName, sink);
                state.Items[readerName] = new SubscriberItemState(targetVariables, sink);
            }

            foreach (string removed in GetRemovedKeys(state.Items.Keys, desired))
            {
                m_sinks.Remove(removed);
                state.Items.Remove(removed);
            }

            if (state.Items.Count == 0)
            {
                await state.DisposeAsync(m_sinks).ConfigureAwait(false);
            }
        }

        private async ValueTask ApplyActionResponderAsync(
            string optionsName,
            PubSubApplicationBuilder? builder,
            IPubSubApplication? application,
            CancellationToken cancellationToken)
        {
            ServerActionResponderOptions options = m_actionOptions.Get(optionsName);
            if (!m_actions.TryGetValue(optionsName, out ActionBindingState? state))
            {
                state = new ActionBindingState();
                m_actions.Add(optionsName, state);
            }

            bool recreate = state.Session is null ||
                !state.Connection.Equals(options.Connection) ||
                !ReferenceEquals(state.MethodMap, options.MethodMap);
            if (recreate)
            {
                await state.DisposeAsync().ConfigureAwait(false);
                state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
                state.Connection = CloneConnectionOptions(options.Connection);
                state.MethodMap = options.MethodMap;
                state.Handler = new ServerActionHandler(
                    state.Session.Session, options.MethodMap, m_telemetry, m_metrics);
                state.RegisteredTargets.Clear();
            }

            foreach (PubSubActionTarget target in options.Targets)
            {
                if (target is null)
                {
                    continue;
                }

                if (builder is not null)
                {
                    builder.AddActionResponder(target, state.Handler!, options.AllowUnsecured);
                }
                else
                {
                    application?.RegisterActionHandler(target, state.Handler!, options.AllowUnsecured);
                }
            }
        }

        private void ApplyInitialPublisher(
            string optionsName,
            PubSubConfigurationDataType configuration)
        {
            ServerPublisherOptions options = m_publisherOptions.Get(optionsName);
            if (!m_publishers.TryGetValue(optionsName, out PublisherBindingState? state))
            {
                state = new PublisherBindingState();
                m_publishers.Add(optionsName, state);
            }
            if (state.Session is not null)
            {
                return;
            }

            List<PublishedDataSetDataType> dataSets = EnumeratePublishedDataSets(configuration);
            HashSet<string> referenced = CollectWriterDataSetNames(configuration);
            if (dataSets.Count == 0 ||
                (options.ReadMode == ReadMode.Subscription && referenced.Count == 0))
            {
                return;
            }

            state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
            state.Connection = CloneConnectionOptions(options.Connection);
            state.ReadMode = options.ReadMode;
            state.Affinity = options.Affinity;

            if (options.ReadMode == ReadMode.Subscription)
            {
                state.ReferencedDataSets = referenced;
                state.Coordinator = new SubscriptionCoordinator(
                    configuration, state.Session.Session, options.Affinity, m_telemetry);
                m_runtime.AddCoordinator(state.Coordinator);
            }
            else
            {
                state.Cyclic = new CyclicReadStrategy(state.Session.Session, m_telemetry, m_metrics);
            }

            foreach (PublishedDataSetDataType dataSet in dataSets)
            {
                string dataSetName = dataSet.Name ?? string.Empty;
                if (dataSetName.Length == 0)
                {
                    continue;
                }

                IReadStrategy strategy;
                if (state.Coordinator is not null)
                {
                    if (!referenced.Contains(dataSetName))
                    {
                        continue;
                    }
                    strategy = state.Coordinator.GetReadStrategy(dataSetName);
                }
                else
                {
                    strategy = state.Cyclic!;
                }

                // Ownership is transferred into PublisherItemState, which disposes the builder when the source is
                // removed. TODO: expose a disposable source wrapper so CA2000 can follow the ownership transfer.
#pragma warning disable CA2000
                DataSetMetaDataBuilder metaDataBuilder = CreateMetaDataBuilder(dataSet, state.Session.Session);
#pragma warning restore CA2000
                var source = new ServerPublishedDataSetSource(
                    dataSet, strategy, metaDataBuilder, m_telemetry);
                m_sources.Register(dataSetName, source);
                state.Items[dataSetName] = new PublisherItemState(dataSet, source, metaDataBuilder);
            }
        }

        private DataSetMetaDataBuilder CreateMetaDataBuilder(
            PublishedDataSetDataType dataSet,
            IServerSession session)
        {
            return new DataSetMetaDataBuilder(dataSet, session, m_telemetry, m_metrics);
        }

        private void ApplyInitialSubscriber(
            string optionsName,
            PubSubConfigurationDataType configuration)
        {
            ServerSubscriberOptions options = m_subscriberOptions.Get(optionsName);
            if (!m_subscribers.TryGetValue(optionsName, out SubscriberBindingState? state))
            {
                state = new SubscriberBindingState();
                m_subscribers.Add(optionsName, state);
            }
            if (state.Session is not null)
            {
                return;
            }

            List<DataSetReaderDataType> readers = EnumerateDataSetReaders(configuration);
            if (readers.Count == 0)
            {
                return;
            }

            state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
            state.Connection = CloneConnectionOptions(options.Connection);
            foreach (DataSetReaderDataType reader in readers)
            {
                string readerName = reader.Name ?? string.Empty;
                if (readerName.Length == 0 ||
                    reader.SubscribedDataSet.IsNull ||
                    !reader.SubscribedDataSet.TryGetValue(out TargetVariablesDataType? targetVariables) ||
                    targetVariables is null)
                {
                    continue;
                }

                ISubscribedDataSetSink sink = ServerSubscribedDataSetSink.Create(
                    targetVariables, state.Session.Session, m_telemetry, m_metrics);
                m_sinks.Register(readerName, sink);
                state.Items[readerName] = new SubscriberItemState(targetVariables, sink);
            }
        }

        private void ApplyInitialActionResponder(
            string optionsName,
            PubSubApplicationBuilder builder)
        {
            ServerActionResponderOptions options = m_actionOptions.Get(optionsName);
            if (!m_actions.TryGetValue(optionsName, out ActionBindingState? state))
            {
                state = new ActionBindingState();
                m_actions.Add(optionsName, state);
            }
            if (state.Session is null)
            {
                state.Session = m_runtime.AcquireSession(options.Connection, m_telemetry);
                state.Connection = CloneConnectionOptions(options.Connection);
                state.MethodMap = options.MethodMap;
                state.Handler = new ServerActionHandler(
                    state.Session.Session, options.MethodMap, m_telemetry, m_metrics);
            }

            foreach (PubSubActionTarget target in options.Targets)
            {
                if (target is null || !state.RegisteredTargets.Add(target))
                {
                    continue;
                }
                builder.AddActionResponder(target, state.Handler!, options.AllowUnsecured);
            }
        }

        private static List<PublishedDataSetDataType> EnumeratePublishedDataSets(
            PubSubConfigurationDataType configuration)
        {
            var dataSets = new List<PublishedDataSetDataType>();
            if (configuration.PublishedDataSets.IsNull)
            {
                return dataSets;
            }
            foreach (PublishedDataSetDataType dataSet in configuration.PublishedDataSets)
            {
                if (dataSet is not null)
                {
                    dataSets.Add(dataSet);
                }
            }
            return dataSets;
        }

        private static List<DataSetReaderDataType> EnumerateDataSetReaders(
            PubSubConfigurationDataType configuration)
        {
            var readers = new List<DataSetReaderDataType>();
            if (configuration.Connections.IsNull)
            {
                return readers;
            }
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                if (connection?.ReaderGroups is null || connection.ReaderGroups.IsNull)
                {
                    continue;
                }
                foreach (ReaderGroupDataType readerGroup in connection.ReaderGroups)
                {
                    if (readerGroup is null || readerGroup.DataSetReaders.IsNull)
                    {
                        continue;
                    }
                    foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                    {
                        if (reader is not null)
                        {
                            readers.Add(reader);
                        }
                    }
                }
            }
            return readers;
        }

        private static HashSet<string> CollectWriterDataSetNames(PubSubConfigurationDataType configuration)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (configuration.Connections.IsNull)
            {
                return names;
            }
            foreach (PubSubConnectionDataType connection in configuration.Connections)
            {
                if (connection?.WriterGroups is null || connection.WriterGroups.IsNull)
                {
                    continue;
                }
                foreach (WriterGroupDataType writerGroup in connection.WriterGroups)
                {
                    if (writerGroup is null || writerGroup.DataSetWriters.IsNull)
                    {
                        continue;
                    }
                    foreach (DataSetWriterDataType writer in writerGroup.DataSetWriters)
                    {
                        if (!string.IsNullOrEmpty(writer?.DataSetName))
                        {
                            names.Add(writer!.DataSetName!);
                        }
                    }
                }
            }
            return names;
        }

        private static List<string> GetRemovedKeys(
            IEnumerable<string> current,
            HashSet<string> desired)
        {
            var removed = new List<string>();
            foreach (string key in current)
            {
                if (!desired.Contains(key))
                {
                    removed.Add(key);
                }
            }
            return removed;
        }

        private static bool SetEquals(HashSet<string> left, HashSet<string> right)
        {
            return left.Count == right.Count && left.SetEquals(right);
        }

        private static ServerConnectionOptions CloneConnectionOptions(ServerConnectionOptions options)
        {
            return new ServerConnectionOptions
            {
                EndpointUrl = options.EndpointUrl,
                SecurityMode = options.SecurityMode,
                SecurityPolicyUri = options.SecurityPolicyUri,
                UserIdentity = options.UserIdentity,
                UserName = options.UserName,
                Password = options.Password,
                SessionName = options.SessionName,
                SessionTimeout = options.SessionTimeout,
                ApplicationConfiguration = options.ApplicationConfiguration,
                ApplicationName = options.ApplicationName
            };
        }

        private static bool HasActionResponderBinding(AdapterBinding[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Kind == AdapterBindingKind.ActionResponder)
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly TimeSpan s_debounceInterval = TimeSpan.FromMilliseconds(250);
        private readonly IPubSubConfigurationStore m_configurationStore;
        private readonly IOptionsMonitor<ServerPublisherOptions> m_publisherOptions;
        private readonly IOptionsMonitor<ServerSubscriberOptions> m_subscriberOptions;
        private readonly IOptionsMonitor<ServerActionResponderOptions> m_actionOptions;
        private readonly MutableDataSetSourceProvider m_sources;
        private readonly MutableDataSetSinkProvider m_sinks;
        private readonly ServerAdapterRuntime m_runtime;
        private readonly ITelemetryContext m_telemetry;
        private readonly AdapterMetrics m_metrics;
        private readonly ILogger m_logger;
        private readonly Lock m_gate = new();
        private readonly SemaphoreSlim m_reloadLock = new(1, 1);
        private readonly HashSet<AdapterBinding> m_bindings = [];
        private readonly List<IDisposable> m_optionSubscriptions = [];
        private readonly Dictionary<string, PublisherBindingState> m_publishers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SubscriberBindingState> m_subscribers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ActionBindingState> m_actions = new(StringComparer.Ordinal);
        private IPubSubApplication? m_application;
        private CancellationTokenSource? m_debounce;
        private PubSubConfigurationDataType? m_pendingConfiguration;
        private bool m_started;
        private bool m_disposed;

        private enum AdapterBindingKind
        {
            Publisher,
            Subscriber,
            ActionResponder
        }

        private sealed record AdapterBinding(AdapterBindingKind Kind, string OptionsName);

        private sealed class PublisherBindingState
        {
            public ServerAdapterRuntime.ServerSessionLease? Session { get; set; }

            public ServerConnectionOptions Connection { get; set; } = new();

            public ReadMode ReadMode { get; set; }

            public SubscriptionAffinity Affinity { get; set; }

            public CyclicReadStrategy? Cyclic { get; set; }

            public SubscriptionCoordinator? Coordinator { get; set; }

            public HashSet<string> ReferencedDataSets { get; set; } = new(StringComparer.Ordinal);

            public Dictionary<string, PublisherItemState> Items { get; } = new(StringComparer.Ordinal);

            public async ValueTask DisposeAsync(MutableDataSetSourceProvider sources)
            {
                foreach (string name in Items.Keys)
                {
                    sources.Remove(name);
                    Items[name].Dispose();
                }
                Items.Clear();
                if (Coordinator is not null)
                {
                    await Coordinator.DisposeAsync().ConfigureAwait(false);
                    Coordinator = null;
                }
                if (Session is not null)
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                    Session = null;
                }
                Cyclic = null;
            }
        }

        private sealed record PublisherItemState(
            PublishedDataSetDataType Configuration,
            IPublishedDataSetSource Source,
            DataSetMetaDataBuilder MetaDataBuilder) : IDisposable
        {
            public void Dispose()
            {
                MetaDataBuilder.Dispose();
            }
        }

        private sealed class SubscriberBindingState
        {
            public ServerAdapterRuntime.ServerSessionLease? Session { get; set; }

            public ServerConnectionOptions Connection { get; set; } = new();

            public Dictionary<string, SubscriberItemState> Items { get; } = new(StringComparer.Ordinal);

            public async ValueTask DisposeAsync(MutableDataSetSinkProvider sinks)
            {
                foreach (string name in Items.Keys)
                {
                    sinks.Remove(name);
                }
                Items.Clear();
                if (Session is not null)
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                    Session = null;
                }
            }
        }

        private sealed record SubscriberItemState(
            TargetVariablesDataType Configuration,
            ISubscribedDataSetSink Sink);

        private sealed class ActionBindingState
        {
            public ServerAdapterRuntime.ServerSessionLease? Session { get; set; }

            public ServerConnectionOptions Connection { get; set; } = new();

            public ActionMethodMap? MethodMap { get; set; }

            public ServerActionHandler? Handler { get; set; }

            public HashSet<PubSubActionTarget> RegisteredTargets { get; } = [];

            public async ValueTask DisposeAsync()
            {
                RegisteredTargets.Clear();
                Handler = null;
                MethodMap = null;
                if (Session is not null)
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                    Session = null;
                }
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for ServerAdapterReloadCoordinator.
    /// </summary>
    internal static partial class ServerAdapterReloadCoordinatorLog
    {
        [LoggerMessage(EventId = PubSubAdapterEventIds.ServerAdapterReloadCoordinator + 0,
            Level = LogLevel.Error, Message = "External-server PubSub adapter hot reload failed.")]
        public static partial void HotReloadFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = PubSubAdapterEventIds.ServerAdapterReloadCoordinator + 1,
            Level = LogLevel.Error, Message = "Failed to apply external-server PubSub adapter hot reload.")]
        public static partial void ApplyHotReloadFailed(this ILogger logger, Exception exception);
    }

}
