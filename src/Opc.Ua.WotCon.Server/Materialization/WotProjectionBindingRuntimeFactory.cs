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
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The default <see cref="IWotProjectionBindingRuntimeFactory"/>. It builds
    /// a <see cref="WotProjectionBindingRuntime"/> for the closure's prepared
    /// binding plans and wires it against the freshly imported predefined
    /// nodes. Always available via direct construction (no dependency
    /// injection container required) given an
    /// <see cref="IWotBindingChannelFactory"/> — typically the same
    /// <c>WotProtocolBinderRegistry</c> instance exposed as
    /// <see cref="IWotBinderRegistry"/>.
    /// </summary>
    public sealed class WotProjectionBindingRuntimeFactory : IWotProjectionBindingRuntimeFactory
    {
        /// <summary>
        /// Initializes a new projection binding runtime factory.
        /// </summary>
        /// <param name="channelFactory">The channel factory used to open live channels.</param>
        /// <param name="resolver">
        /// The target-variable resolver. Defaults to a new
        /// <see cref="WotTargetVariableResolver"/> when <c>null</c>.
        /// </param>
        public WotProjectionBindingRuntimeFactory(
            IWotBindingChannelFactory channelFactory,
            IWotTargetVariableResolver? resolver = null)
            : this(channelFactory, resolver, new WotProjectionEventPublisher(),
                new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions())
        {
        }

        /// <summary>
        /// Initializes a runtime factory with injectable event publication,
        /// Condition materialization and generation bounds.
        /// </summary>
        public WotProjectionBindingRuntimeFactory(
            IWotBindingChannelFactory channelFactory,
            IWotTargetVariableResolver? resolver,
            IWotProjectionEventPublisher eventPublisher,
            IWotProjectionConditionFactory conditionFactory,
            WotProjectionBindingRuntimeOptions options)
        {
            m_channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
            m_resolver = resolver ?? new WotTargetVariableResolver();
            m_eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
            m_conditionFactory = conditionFactory ?? throw new ArgumentNullException(nameof(conditionFactory));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            WotBindingBounds.EnsurePositive(options.MaxQueuedEvents, nameof(options.MaxQueuedEvents));
            WotBindingBounds.EnsurePositive(options.MaxEventRoutes, nameof(options.MaxEventRoutes));
        }

        /// <inheritdoc/>
        public async ValueTask<IAsyncDisposable?> CreateAsync(
            INodeManagerBuilder builder,
            ArrayOf<WotBindingPlan> bindingPlans,
            CancellationToken cancellationToken = default)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (bindingPlans.IsEmpty)
            {
                return null;
            }
            return await CreateWiredRuntimeAsync(builder, bindingPlans, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the runtime and wires it, disposing it again if wiring
        /// fails so ownership never escapes unwired: the returned instance is
        /// always the fully-wired runtime the caller is meant to own.
        /// </summary>
        private async ValueTask<WotProjectionBindingRuntime> CreateWiredRuntimeAsync(
            INodeManagerBuilder builder, ArrayOf<WotBindingPlan> bindingPlans, CancellationToken cancellationToken)
        {
            var runtime = new WotProjectionBindingRuntime(
                builder, m_channelFactory, m_resolver, m_eventPublisher, m_conditionFactory, m_options, m_eventRoutes);
            try
            {
                await runtime.WireAsync(bindingPlans, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception activationError) when (activationError is not OutOfMemoryException)
            {
                try
                {
                    await runtime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposalError) when (disposalError is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Projection binding activation and cleanup both failed.", activationError, disposalError);
                }
                throw;
            }
            return runtime;
        }

        private readonly IWotBindingChannelFactory m_channelFactory;
        private readonly IWotTargetVariableResolver m_resolver;
        private readonly IWotProjectionEventPublisher m_eventPublisher;
        private readonly IWotProjectionConditionFactory m_conditionFactory;
        private readonly WotProjectionBindingRuntimeOptions m_options;
        private readonly WotProjectedEventRouteRegistry m_eventRoutes = new();
    }
}
