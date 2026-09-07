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
        {
            m_channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
            m_resolver = resolver ?? new WotTargetVariableResolver();
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
            if (bindingPlans.IsEmpty)
            {
                return null;
            }
            return await CreateWiredRuntimeAsync(builder, bindingPlans).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the runtime and wires it, disposing it again if wiring
        /// fails so ownership never escapes unwired: the returned instance is
        /// always the fully-wired runtime the caller is meant to own.
        /// </summary>
        private async ValueTask<WotProjectionBindingRuntime> CreateWiredRuntimeAsync(
            INodeManagerBuilder builder, ArrayOf<WotBindingPlan> bindingPlans)
        {
            var runtime = new WotProjectionBindingRuntime(builder, m_channelFactory, m_resolver);
            try
            {
                runtime.Wire(bindingPlans);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return runtime;
        }

        private readonly IWotBindingChannelFactory m_channelFactory;
        private readonly IWotTargetVariableResolver m_resolver;
    }
}
