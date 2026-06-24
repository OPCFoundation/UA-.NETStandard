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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Publisher;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Adapter.Subscriber;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent <see cref="IPubSubBuilder"/> extensions that wire the external
    /// OPC UA server PubSub adapters into the dependency-injection container:
    /// a publisher that reads an external server's nodes and publishes them as
    /// PubSub DataSets, a subscriber that writes received DataSet values back to
    /// an external server, and an action responder that maps inbound PubSub
    /// Action requests to external server method calls.
    /// </summary>
    /// <remarks>
    /// Every extension shares a single <see cref="IExternalServerSession"/> per
    /// registration whose lifetime is owned by a singleton
    /// <see cref="ExternalServerAdapterRuntime"/>: subscription coordinators are
    /// started on application start and every session is closed on shutdown.
    /// The configured PubSub configuration must be supplied (via
    /// <see cref="IPubSubBuilder.UseConfiguration"/>,
    /// <see cref="IPubSubBuilder.UseConfigurationFile"/> or
    /// <c>InlineConfiguration</c> options) before the adapter is added so the
    /// composition step can enumerate the configured datasets and readers.
    /// </remarks>
    public static class OpcUaPubSubAdapterBuilderExtensions
    {
        /// <summary>
        /// Adds an external-server PubSub publisher. A single managed session is
        /// created for the configured endpoint and reused across the publisher's
        /// PublishedDataSets. In <see cref="ExternalReadMode.Subscription"/> mode
        /// one <see cref="ExternalSubscriptionCoordinator"/> is created for the
        /// whole configuration and started on application start; in
        /// <see cref="ExternalReadMode.Cyclic"/> mode a shared
        /// <see cref="CyclicReadStrategy"/> issues Read calls each publish cycle.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the publisher to.
        /// </param>
        /// <param name="configure">
        /// Callback that configures the publisher options.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddExternalServerPublisher(
            this IPubSubBuilder builder,
            Action<ExternalServerPublisherOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new ExternalServerPublisherOptions();
            configure(options);

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                ILogger logger =
                    telemetry.CreateLogger<ExternalServerAdapterRuntime>();
                ExternalServerAdapterRuntime runtime =
                    sp.GetRequiredService<ExternalServerAdapterRuntime>();

                IExternalServerSession session = CreateSession(sp, options.Connection, telemetry);
                runtime.AddSession(session);

                PubSubConfigurationDataType configuration = pb.GetConfigurationOrDefault();

                ExternalSubscriptionCoordinator? coordinator = null;
                CyclicReadStrategy? cyclic = null;
                HashSet<string>? referenced = null;
                if (options.ReadMode == ExternalReadMode.Subscription)
                {
                    coordinator = new ExternalSubscriptionCoordinator(
                        configuration, session, options.Affinity, telemetry);
                    runtime.AddCoordinator(coordinator);
                    referenced = CollectWriterDataSetNames(configuration);
                }
                else
                {
                    cyclic = new CyclicReadStrategy(session, telemetry);
                }

                foreach (PublishedDataSetDataType dataSet in EnumeratePublishedDataSets(configuration))
                {
                    string name = dataSet.Name ?? string.Empty;
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    IExternalReadStrategy strategy;
                    if (coordinator is not null)
                    {
                        if (referenced is null || !referenced.Contains(name))
                        {
                            logger.LogDebug(
                                "PublishedDataSet '{Name}' is not referenced by any "
                                + "DataSetWriter; skipping external subscription source.",
                                name);
                            continue;
                        }
                        strategy = coordinator.GetReadStrategy(name);
                    }
                    else
                    {
                        strategy = cyclic!;
                    }

                    var metaDataBuilder = new ExternalDataSetMetaDataBuilder(
                        dataSet, session, telemetry);
                    var source = new ExternalServerPublishedDataSetSource(
                        dataSet, strategy, metaDataBuilder, telemetry);
                    pb.AddDataSetSource(name, source);
                }
            });

            return builder;
        }

        /// <summary>
        /// Adds an external-server PubSub subscriber. A single managed session is
        /// created for the configured endpoint and a
        /// <see cref="ISubscribedDataSetSink"/> is registered for every
        /// DataSetReader whose SubscribedDataSet is a
        /// <see cref="TargetVariablesDataType"/>.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the subscriber to.
        /// </param>
        /// <param name="configure">
        /// Callback that configures the subscriber options.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddExternalServerSubscriber(
            this IPubSubBuilder builder,
            Action<ExternalServerSubscriberOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new ExternalServerSubscriberOptions();
            configure(options);

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                ExternalServerAdapterRuntime runtime =
                    sp.GetRequiredService<ExternalServerAdapterRuntime>();

                IExternalServerSession session = CreateSession(sp, options.Connection, telemetry);
                runtime.AddSession(session);

                PubSubConfigurationDataType configuration = pb.GetConfigurationOrDefault();
                foreach (DataSetReaderDataType reader in EnumerateDataSetReaders(configuration))
                {
                    string name = reader.Name ?? string.Empty;
                    if (name.Length == 0)
                    {
                        continue;
                    }
                    if (reader.SubscribedDataSet.IsNull
                        || !reader.SubscribedDataSet.TryGetValue(
                            out TargetVariablesDataType? targetVariables)
                        || targetVariables is null)
                    {
                        continue;
                    }

                    ISubscribedDataSetSink sink = ExternalServerSubscribedDataSetSink.Create(
                        targetVariables, session, telemetry);
                    pb.AddSubscribedDataSetSink(name, sink);
                }
            });

            return builder;
        }

        /// <summary>
        /// Adds an external-server PubSub action responder. A single managed
        /// session is created for the configured endpoint and an
        /// <see cref="ExternalServerActionHandler"/> backed by the configured
        /// <see cref="ExternalServerActionResponderOptions.MethodMap"/> is
        /// registered for every configured target.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the action responder to.
        /// </param>
        /// <param name="configure">
        /// Callback that configures the action responder options.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddExternalServerActionResponder(
            this IPubSubBuilder builder,
            Action<ExternalServerActionResponderOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new ExternalServerActionResponderOptions();
            configure(options);

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                ExternalServerAdapterRuntime runtime =
                    sp.GetRequiredService<ExternalServerAdapterRuntime>();

                IExternalServerSession session = CreateSession(sp, options.Connection, telemetry);
                runtime.AddSession(session);

                var handler = new ExternalServerActionHandler(
                    session, options.MethodMap, telemetry);
                if (options.Targets is null)
                {
                    return;
                }
                foreach (PubSubActionTarget target in options.Targets)
                {
                    if (target is null)
                    {
                        continue;
                    }
                    pb.AddActionResponder(target, handler, options.AllowUnsecured);
                }
            });

            return builder;
        }

        private static void RegisterCoreServices(IPubSubBuilder builder)
        {
            builder.Services.TryAddSingleton<IExternalServerSessionFactory, ExternalServerSessionFactory>();
            builder.Services.TryAddSingleton<ExternalServerAdapterRuntime>();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ExternalServerAdapterHostedService>());
        }

        private static IExternalServerSession CreateSession(
            IServiceProvider sp,
            ExternalServerConnectionOptions connection,
            ITelemetryContext telemetry)
        {
            IExternalServerSessionFactory factory =
                sp.GetRequiredService<IExternalServerSessionFactory>();
            return factory.Create(connection, telemetry);
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

        private static HashSet<string> CollectWriterDataSetNames(
            PubSubConfigurationDataType configuration)
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
    }
}
