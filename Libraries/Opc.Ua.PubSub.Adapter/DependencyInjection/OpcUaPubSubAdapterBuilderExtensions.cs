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
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.DependencyInjection;
using Opc.Ua.PubSub.Adapter.Diagnostics;
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
    /// Every extension shares a single <see cref="IServerSession"/> per
    /// registration whose lifetime is owned by a singleton
    /// <see cref="ServerAdapterRuntime"/>: subscription coordinators are
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
        /// Adds the PubSub runtime with a configuration and wires an external server as publisher and subscriber.
        /// </summary>
        /// <param name="services">Service collection.</param>
        /// <param name="configuration">PubSub configuration to apply before adapter bindings are evaluated.</param>
        /// <param name="configurePublisher">Publisher adapter options callback.</param>
        /// <param name="configureSubscriber">Subscriber adapter options callback.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        public static IServiceCollection AddServerAdapterPubSub(
            this IServiceCollection services,
            PubSubConfigurationDataType configuration,
            Action<ServerPublisherOptions> configurePublisher,
            Action<ServerSubscriberOptions> configureSubscriber)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddOpcUa().AddServerAdapterPubSub(
                configuration,
                configurePublisher,
                configureSubscriber);
            return services;
        }

        /// <summary>
        /// Adds the PubSub runtime with a configuration and wires an external server as publisher and subscriber.
        /// </summary>
        /// <param name="builder">OPC UA builder.</param>
        /// <param name="configuration">PubSub configuration to apply before adapter bindings are evaluated.</param>
        /// <param name="configurePublisher">Publisher adapter options callback.</param>
        /// <param name="configureSubscriber">Subscriber adapter options callback.</param>
        /// <returns>The same <paramref name="builder"/> instance.</returns>
        public static IOpcUaBuilder AddServerAdapterPubSub(
            this IOpcUaBuilder builder,
            PubSubConfigurationDataType configuration,
            Action<ServerPublisherOptions> configurePublisher,
            Action<ServerSubscriberOptions> configureSubscriber)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (configurePublisher is null)
            {
                throw new ArgumentNullException(nameof(configurePublisher));
            }
            if (configureSubscriber is null)
            {
                throw new ArgumentNullException(nameof(configureSubscriber));
            }

            builder.AddPubSub(pubsub => pubsub
                .UseConfiguration(configuration)
                .AddServerAsPublisher(configurePublisher)
                .AddServerAsSubscriber(configureSubscriber));
            return builder;
        }

        /// <summary>
        /// Adds an external-server PubSub publisher. A single managed session is
        /// created for the configured endpoint and reused across the publisher's
        /// PublishedDataSets. In <see cref="ReadMode.Subscription"/> mode
        /// one <see cref="SubscriptionCoordinator"/> is created for the
        /// whole configuration and started on application start; in
        /// <see cref="ReadMode.Cyclic"/> mode a shared
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
        public static IPubSubBuilder AddServerAsPublisher(
            this IPubSubBuilder builder,
            Action<ServerPublisherOptions> configure)
        {
            return AddServerAsPublisher(
                builder, Microsoft.Extensions.Options.Options.DefaultName, configure);
        }

        /// <summary>
        /// Adds an external-server PubSub publisher whose options are bound from
        /// configuration under the supplied name.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the publisher to.
        /// </param>
        /// <param name="name">
        /// The named-options registration name.
        /// </param>
        /// <param name="configuration">
        /// The configuration section that binds the publisher options. Object-typed
        /// option members are not configuration-bindable and must be supplied from
        /// code, for example through a post-configure callback.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddServerAsPublisher(
            this IPubSubBuilder builder,
            string name,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            RegisterConfigurationOptions<ServerPublisherOptions>(
                builder.Services, name, configuration, BindPublisherOptions);

            return AddServerAsPublisherCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsPublisher(
            IPubSubBuilder builder,
            string name,
            Action<ServerPublisherOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.Configure(name, configure);

            return AddServerAsPublisherCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsPublisherCore(IPubSubBuilder builder, string name)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                PubSubConfigurationDataType configuration = pb.GetConfigurationOrDefault();
                ServerAdapterReloadCoordinator coordinator =
                    sp.GetRequiredService<ServerAdapterReloadCoordinator>();
                coordinator.RegisterPublisherBinding(name);
                coordinator.ApplyInitialConfiguration(configuration, pb);
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
        public static IPubSubBuilder AddServerAsSubscriber(
            this IPubSubBuilder builder,
            Action<ServerSubscriberOptions> configure)
        {
            return AddServerAsSubscriber(
                builder, Microsoft.Extensions.Options.Options.DefaultName, configure);
        }

        /// <summary>
        /// Adds an external-server PubSub subscriber whose options are bound from
        /// configuration under the supplied name.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the subscriber to.
        /// </param>
        /// <param name="name">
        /// The named-options registration name.
        /// </param>
        /// <param name="configuration">
        /// The configuration section that binds the subscriber options. Object-typed
        /// option members are not configuration-bindable and must be supplied from
        /// code, for example through a post-configure callback.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddServerAsSubscriber(
            this IPubSubBuilder builder,
            string name,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            RegisterConfigurationOptions<ServerSubscriberOptions>(
                builder.Services, name, configuration, BindSubscriberOptions);

            return AddServerAsSubscriberCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsSubscriber(
            IPubSubBuilder builder,
            string name,
            Action<ServerSubscriberOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.Configure(name, configure);

            return AddServerAsSubscriberCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsSubscriberCore(IPubSubBuilder builder, string name)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                PubSubConfigurationDataType configuration = pb.GetConfigurationOrDefault();
                ServerAdapterReloadCoordinator coordinator =
                    sp.GetRequiredService<ServerAdapterReloadCoordinator>();
                coordinator.RegisterSubscriberBinding(name);
                coordinator.ApplyInitialConfiguration(configuration, pb);
            });

            return builder;
        }

        /// <summary>
        /// Adds an external-server PubSub action responder. A single managed
        /// session is created for the configured endpoint and an
        /// <see cref="ServerActionHandler"/> backed by the configured
        /// <see cref="ServerActionResponderOptions.MethodMap"/> is
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
        public static IPubSubBuilder AddServerAsActionResponder(
            this IPubSubBuilder builder,
            Action<ServerActionResponderOptions> configure)
        {
            return AddServerAsActionResponder(
                builder, Microsoft.Extensions.Options.Options.DefaultName, configure);
        }

        /// <summary>
        /// Adds an external-server PubSub action responder whose options are bound
        /// from configuration under the supplied name.
        /// </summary>
        /// <param name="builder">
        /// The PubSub builder to add the action responder to.
        /// </param>
        /// <param name="name">
        /// The named-options registration name.
        /// </param>
        /// <param name="configuration">
        /// The configuration section that binds the action responder options.
        /// Object-typed option members are not configuration-bindable and must be
        /// supplied from code, for example through a post-configure callback.
        /// </param>
        /// <returns>
        /// The same builder, to allow fluent composition.
        /// </returns>
        public static IPubSubBuilder AddServerAsActionResponder(
            this IPubSubBuilder builder,
            string name,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            RegisterConfigurationOptions<ServerActionResponderOptions>(
                builder.Services, name, configuration, BindActionResponderOptions);

            return AddServerAsActionResponderCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsActionResponder(
            IPubSubBuilder builder,
            string name,
            Action<ServerActionResponderOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.Configure(name, configure);

            return AddServerAsActionResponderCore(builder, name);
        }

        private static IPubSubBuilder AddServerAsActionResponderCore(
            IPubSubBuilder builder,
            string name)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            RegisterCoreServices(builder);

            builder.ConfigureApplication((sp, pb) =>
            {
                PubSubConfigurationDataType configuration = pb.GetConfigurationOrDefault();
                ServerAdapterReloadCoordinator coordinator =
                    sp.GetRequiredService<ServerAdapterReloadCoordinator>();
                coordinator.RegisterActionResponderBinding(name);
                coordinator.ApplyInitialConfiguration(configuration, pb);
            });

            return builder;
        }

        private static void RegisterCoreServices(IPubSubBuilder builder)
        {
            builder.Services.TryAddSingleton<IServerSessionFactory, ServerSessionFactory>();
            builder.Services.TryAddSingleton<AdapterMetrics>();
            builder.Services.TryAddSingleton<ServerAdapterRuntime>();
            builder.Services.TryAddSingleton<MutableDataSetSourceProvider>();
            builder.Services.TryAddSingleton<MutableDataSetSinkProvider>();
            builder.Services.RemoveAll<IDataSetSourceProvider>();
            builder.Services.RemoveAll<IDataSetSinkProvider>();
            builder.Services.AddSingleton<IDataSetSourceProvider>(
                sp => sp.GetRequiredService<MutableDataSetSourceProvider>());
            builder.Services.AddSingleton<IDataSetSinkProvider>(
                sp => sp.GetRequiredService<MutableDataSetSinkProvider>());
            builder.Services.TryAddSingleton<ServerAdapterReloadCoordinator>();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ServerAdapterHostedService>());
        }

        private static void RegisterConfigurationOptions<TOptions>(
            IServiceCollection services,
            string name,
            IConfiguration configuration,
            Action<TOptions, IConfiguration> bind)
            where TOptions : class
        {
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>>(
                new ConfigurationChangeTokenSource<TOptions>(name, configuration));
            services.AddSingleton<IConfigureOptions<TOptions>>(
                new ConfigureNamedOptions<TOptions>(
                    name, options => bind(options, configuration)));
        }

        private static void BindPublisherOptions(
            ServerPublisherOptions options,
            IConfiguration configuration)
        {
            options.Connection ??= new ServerConnectionOptions();
            BindConnectionOptions(
                options.Connection,
                configuration.GetSection(nameof(ServerPublisherOptions.Connection)));
            BindEnum<ReadMode>(
                configuration,
                nameof(ServerPublisherOptions.ReadMode),
                value => options.ReadMode = value);
            BindEnum<SubscriptionAffinity>(
                configuration,
                nameof(ServerPublisherOptions.Affinity),
                value => options.Affinity = value);
        }

        private static void BindSubscriberOptions(
            ServerSubscriberOptions options,
            IConfiguration configuration)
        {
            options.Connection ??= new ServerConnectionOptions();
            BindConnectionOptions(
                options.Connection,
                configuration.GetSection(nameof(ServerSubscriberOptions.Connection)));
        }

        private static void BindActionResponderOptions(
            ServerActionResponderOptions options,
            IConfiguration configuration)
        {
            options.Connection ??= new ServerConnectionOptions();
            BindConnectionOptions(
                options.Connection,
                configuration.GetSection(nameof(ServerActionResponderOptions.Connection)));
            BindBoolean(
                configuration,
                nameof(ServerActionResponderOptions.AllowUnsecured),
                value => options.AllowUnsecured = value);
        }

        private static void BindConnectionOptions(
            ServerConnectionOptions options,
            IConfiguration configuration)
        {
            BindString(
                configuration,
                nameof(ServerConnectionOptions.EndpointUrl),
                value => options.EndpointUrl = value);
            BindEnum<MessageSecurityMode>(
                configuration,
                nameof(ServerConnectionOptions.SecurityMode),
                value => options.SecurityMode = value);
            BindString(
                configuration,
                nameof(ServerConnectionOptions.SecurityPolicyUri),
                value => options.SecurityPolicyUri = value);
            BindString(
                configuration,
                nameof(ServerConnectionOptions.UserName),
                value => options.UserName = value);
            BindString(
                configuration,
                nameof(ServerConnectionOptions.Password),
                value => options.Password = value);
            BindString(
                configuration,
                nameof(ServerConnectionOptions.SessionName),
                value => options.SessionName = value);
            BindUnsignedInteger(
                configuration,
                nameof(ServerConnectionOptions.SessionTimeout),
                value => options.SessionTimeout = value);
            BindString(
                configuration,
                nameof(ServerConnectionOptions.ApplicationName),
                value => options.ApplicationName = value);
        }

        private static void BindString(
            IConfiguration configuration,
            string key,
            Action<string> assign)
        {
            string? value = configuration[key];
            if (value is not null)
            {
                assign(value);
            }
        }

        private static void BindEnum<TEnum>(
            IConfiguration configuration,
            string key,
            Action<TEnum> assign)
            where TEnum : struct, Enum
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }

            if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid {typeof(TEnum).Name}.");
            }

            assign(parsed);
        }

        private static void BindUnsignedInteger(
            IConfiguration configuration,
            string key,
            Action<uint> assign)
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }

            if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid unsigned integer.");
            }

            assign(parsed);
        }

        private static void BindBoolean(
            IConfiguration configuration,
            string key,
            Action<bool> assign)
        {
            string? value = configuration[key];
            if (value is null)
            {
                return;
            }

            if (!bool.TryParse(value, out bool parsed))
            {
                throw new InvalidOperationException(
                    $"Configuration value '{value}' for '{key}' is not a valid boolean.");
            }

            assign(parsed);
        }

        private static IServerSession CreateSession(
            IServiceProvider sp,
            ServerConnectionOptions connection,
            ITelemetryContext telemetry)
        {
            IServerSessionFactory factory =
                sp.GetRequiredService<IServerSessionFactory>();
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
