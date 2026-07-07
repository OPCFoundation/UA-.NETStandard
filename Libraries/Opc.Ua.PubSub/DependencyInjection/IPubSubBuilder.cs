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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Fluent builder used to compose OPC UA PubSub services.
    /// </summary>
    public interface IPubSubBuilder
    {
        /// <summary>
        /// Gets the service collection used by the parent OPC UA builder.
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// Gets the parent OPC UA builder.
        /// </summary>
        IOpcUaBuilder OpcUaBuilder { get; }

        /// <summary>
        /// Configures the application as a publisher.
        /// </summary>
        IPubSubBuilder AddPublisher();

        /// <summary>
        /// Configures the application as a subscriber.
        /// </summary>
        IPubSubBuilder AddSubscriber();

        /// <summary>
        /// Adds an application builder configuration callback.
        /// </summary>
        /// <param name="configure">The application builder callback.</param>
        IPubSubBuilder ConfigureApplication(Action<PubSubApplicationBuilder> configure);

        /// <summary>
        /// Adds a service-provider-aware application builder configuration
        /// callback. The callback runs as a deferred composition step after the
        /// configured PubSub configuration has been applied to the
        /// <see cref="PubSubApplicationBuilder"/>, so it can enumerate the
        /// configured datasets and readers via
        /// <see cref="PubSubApplicationBuilder.GetConfigurationOrDefault"/> and
        /// resolve services from the supplied <see cref="IServiceProvider"/>.
        /// Unlike <see cref="ConfigureApplication(Action{PubSubApplicationBuilder})"/>
        /// it does not suppress option-based configuration.
        /// </summary>
        /// <param name="configure">The application builder callback.</param>
        IPubSubBuilder ConfigureApplication(
            Action<IServiceProvider, PubSubApplicationBuilder> configure);

        /// <summary>
        /// Adds a PubSub security key provider.
        /// </summary>
        /// <param name="keyProvider">The security key provider.</param>
        IPubSubBuilder AddSecurityKeyProvider(IPubSubSecurityKeyProvider keyProvider);

        /// <summary>
        /// Uses a custom PubSub configuration store.
        /// </summary>
        /// <param name="store">Configuration store.</param>
        IPubSubBuilder WithConfigurationStore(IPubSubConfigurationStore store);

        /// <summary>
        /// Uses a custom PubSub id allocator.
        /// </summary>
        /// <param name="allocator">Id allocator.</param>
        IPubSubBuilder WithIdAllocator(IPubSubIdAllocator allocator);

        /// <summary>
        /// Uses a custom PubSub runtime-state store.
        /// </summary>
        /// <param name="store">Runtime-state store.</param>
        IPubSubBuilder WithRuntimeStateStore(IPubSubRuntimeStateStore store);

        /// <summary>
        /// Uses a custom PubSub security-key store.
        /// </summary>
        /// <param name="store">Security-key store.</param>
        IPubSubBuilder WithSecurityKeyStore(IPubSubSecurityKeyStore store);

        /// <summary>
        /// Uses a custom PubSub activation coordinator that decides, per
        /// component, whether this instance is active or on standby in a
        /// redundant (high-availability) deployment (Part 14 §9.1.6).
        /// </summary>
        /// <param name="coordinator">Activation coordinator.</param>
        IPubSubBuilder WithActivationCoordinator(IPubSubActivationCoordinator coordinator);

        /// <summary>
        /// Uses a custom PubSub lease store for leader election in a redundant
        /// deployment. A distributed store enables genuine multi-instance
        /// failover.
        /// </summary>
        /// <param name="leaseStore">Lease store.</param>
        IPubSubBuilder WithLeaseStore(IPubSubLeaseStore leaseStore);

        /// <summary>
        /// Adds a responder-side PubSub Action handler.
        /// </summary>
        /// <param name="target">Action target handled by <paramref name="handler"/>.</param>
        /// <param name="handler">Action handler.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null);

        /// <summary>
        /// Adds a responder-side PubSub Action handler factory.
        /// </summary>
        /// <param name="target">Action target handled by the resolved handler.</param>
        /// <param name="handlerFactory">Action handler factory.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            Func<IServiceProvider, IPubSubActionHandler> handlerFactory,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null);

        /// <summary>
        /// Adds a responder-side PubSub Action handler from DI.
        /// </summary>
        /// <typeparam name="THandler">Action handler type.</typeparam>
        /// <param name="target">Action target handled by the resolved handler.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        IPubSubBuilder AddActionResponder<THandler>(
            PubSubActionTarget target,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null)
            where THandler : class, IPubSubActionHandler;

        /// <summary>
        /// Adds a delegate-backed responder-side PubSub Action handler.
        /// </summary>
        /// <param name="target">Action target handled by <paramref name="handler"/>.</param>
        /// <param name="handler">Delegate action handler.</param>
        /// <param name="allowUnsecured">Allow serving the Action on an unsecured connection.</param>
        /// <param name="responseAddressPolicy">
        /// Optional policy validating the requestor-supplied response address (SA-ACT-03).
        /// Defaults to <see cref="PubSubResponseAddressPolicy.Default"/>.
        /// </param>
        IPubSubBuilder AddActionResponder(
            PubSubActionTarget target,
            Func<PubSubActionInvocation, CancellationToken, ValueTask<PubSubActionHandlerResult>> handler,
            bool allowUnsecured = false,
            PubSubResponseAddressPolicy? responseAddressPolicy = null);

        /// <summary>
        /// Adds a published dataset source.
        /// </summary>
        /// <param name="publishedDataSetName">The published dataset name.</param>
        /// <param name="source">The published dataset source.</param>
        IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            IPublishedDataSetSource source);

        /// <summary>
        /// Adds a published dataset source factory.
        /// </summary>
        /// <param name="publishedDataSetName">The published dataset name.</param>
        /// <param name="sourceFactory">The published dataset source factory.</param>
        IPubSubBuilder AddDataSetSource(
            string publishedDataSetName,
            Func<IServiceProvider, IPublishedDataSetSource> sourceFactory);

        /// <summary>
        /// Adds a subscribed dataset sink.
        /// </summary>
        /// <param name="dataSetReaderName">The dataset reader name.</param>
        /// <param name="sink">The subscribed dataset sink.</param>
        IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            ISubscribedDataSetSink sink);

        /// <summary>
        /// Adds a subscribed dataset sink factory.
        /// </summary>
        /// <param name="dataSetReaderName">The dataset reader name.</param>
        /// <param name="sinkFactory">The subscribed dataset sink factory.</param>
        IPubSubBuilder AddSubscribedDataSetSink(
            string dataSetReaderName,
            Func<IServiceProvider, ISubscribedDataSetSink> sinkFactory);

        /// <summary>
        /// Uses the supplied PubSub configuration.
        /// </summary>
        /// <param name="configuration">The PubSub configuration.</param>
        IPubSubBuilder UseConfiguration(PubSubConfigurationDataType configuration);

        /// <summary>
        /// Uses a PubSub configuration file.
        /// </summary>
        /// <param name="path">The configuration file path.</param>
        IPubSubBuilder UseConfigurationFile(string path);

        /// <summary>
        /// Configures PubSub application options.
        /// </summary>
        /// <param name="configure">The options configuration callback.</param>
        IPubSubBuilder Configure(Action<PubSubApplicationOptions> configure);
    }
}
