/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Default <see cref="IUdpTransportBuilder"/> decorator.
    /// </summary>
    internal sealed class UdpTransportBuilder : IUdpTransportBuilder
    {
        /// <summary>
        /// Initializes a new <see cref="UdpTransportBuilder"/>.
        /// </summary>
        /// <param name="pubSubBuilder">The underlying PubSub builder.</param>
        public UdpTransportBuilder(IPubSubBuilder pubSubBuilder)
        {
            PubSubBuilder = pubSubBuilder ?? throw new ArgumentNullException(nameof(pubSubBuilder));
        }

        /// <inheritdoc/>
        public IPubSubBuilder PubSubBuilder { get; }

        /// <inheritdoc/>
        public IServiceCollection Services => PubSubBuilder.Services;

        /// <inheritdoc/>
        public IOpcUaBuilder OpcUaBuilder => PubSubBuilder.OpcUaBuilder;

        /// <inheritdoc/>
        public IPubSubBuilder AddPublisher()
        {
            return PubSubBuilder.AddPublisher();
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscriber()
        {
            return PubSubBuilder.AddSubscriber();
        }

        /// <inheritdoc/>
        public IPubSubBuilder ConfigureApplication(Action<PubSubApplicationBuilder> configure)
        {
            return PubSubBuilder.ConfigureApplication(configure);
        }

        /// <inheritdoc/>
        public IPubSubBuilder ConfigureApplication(Action<IServiceProvider, PubSubApplicationBuilder> configure)
        {
            return PubSubBuilder.ConfigureApplication(configure);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSecurityKeyProvider(IPubSubSecurityKeyProvider keyProvider)
        {
            return PubSubBuilder.AddSecurityKeyProvider(keyProvider);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithConfigurationStore(IPubSubConfigurationStore store)
        {
            return PubSubBuilder.WithConfigurationStore(store);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithIdAllocator(IPubSubIdAllocator allocator)
        {
            return PubSubBuilder.WithIdAllocator(allocator);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithRuntimeStateStore(IPubSubRuntimeStateStore store)
        {
            return PubSubBuilder.WithRuntimeStateStore(store);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithSecurityKeyStore(IPubSubSecurityKeyStore store)
        {
            return PubSubBuilder.WithSecurityKeyStore(store);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithActivationCoordinator(
            Opc.Ua.PubSub.Redundancy.IPubSubActivationCoordinator coordinator)
        {
            return PubSubBuilder.WithActivationCoordinator(coordinator);
        }

        /// <inheritdoc/>
        public IPubSubBuilder WithLeaseStore(Opc.Ua.PubSub.Redundancy.IPubSubLeaseStore leaseStore)
        {
            return PubSubBuilder.WithLeaseStore(leaseStore);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
        {
            return PubSubBuilder.AddActionResponder(target, handler, allowUnsecured, responseAddressPolicy);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            Func<IServiceProvider, IPubSubActionHandler> handlerFactory,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
        {
            return PubSubBuilder.AddActionResponder(target, handlerFactory, allowUnsecured, responseAddressPolicy);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddActionResponder<THandler>(
            PubSubActionTarget target,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
            where THandler : class, IPubSubActionHandler
        {
            return PubSubBuilder.AddActionResponder<THandler>(target, allowUnsecured, responseAddressPolicy);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            Func<PubSubActionInvocation, CancellationToken, ValueTask<PubSubActionHandlerResult>> handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
        {
            return PubSubBuilder.AddActionResponder(target, handler, allowUnsecured, responseAddressPolicy);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            IPublishedDataSetSource source)
        {
            return PubSubBuilder.AddDataSetSource(publishedDataSetName, source);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            Func<IServiceProvider, IPublishedDataSetSource> sourceFactory)
        {
            return PubSubBuilder.AddDataSetSource(publishedDataSetName, sourceFactory);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            ISubscribedDataSetSink sink)
        {
            return PubSubBuilder.AddSubscribedDataSetSink(dataSetReaderName, sink);
        }

        /// <inheritdoc/>
        public IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            Func<IServiceProvider, ISubscribedDataSetSink> sinkFactory)
        {
            return PubSubBuilder.AddSubscribedDataSetSink(dataSetReaderName, sinkFactory);
        }

        /// <inheritdoc/>
        public IPubSubBuilder UseConfiguration(PubSubConfigurationDataType configuration)
        {
            return PubSubBuilder.UseConfiguration(configuration);
        }

        /// <inheritdoc/>
        public IPubSubBuilder UseConfigurationFile(string path)
        {
            return PubSubBuilder.UseConfigurationFile(path);
        }

        /// <inheritdoc/>
        public IPubSubBuilder Configure(Action<PubSubApplicationOptions> configure)
        {
            return PubSubBuilder.Configure(configure);
        }
    }
}
