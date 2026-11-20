/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// The runtime-neutral seam a projection binding runtime uses to open a live
    /// channel for a compiled, executable form. It is deliberately narrower than
    /// <see cref="IWotBinderRegistry"/>: consumers that only drive live value
    /// exchange (for example an OPC UA target-mapping runtime wired onto a
    /// materialized NodeSet) depend on this interface instead of the full
    /// registry so they cannot accidentally reach into Prepare/Activate/Deactivate
    /// lifecycle concerns that belong to the materialization coordinator.
    /// </summary>
    public interface IWotBindingChannelFactory
    {
        /// <summary>
        /// Opens a live channel for an executable compiled form using the
        /// implementation's credential provider, codecs and safety bounds.
        /// </summary>
        /// <param name="form">The compiled, executable form to activate.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="form"/> is <c>null</c>.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// No executor is registered for the form's binding.
        /// </exception>
        ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional factory capability for consumers whose local value factories are
    /// available only after materialization. Existing channel factories remain compatible.
    /// </summary>
    public interface IWotContextualBindingChannelFactory : IWotBindingChannelFactory
    {
        /// <summary>
        /// Opens a channel with the consumer's resolved namespace and DataType context.
        /// </summary>
        /// <param name="form">The WoT compiled form.</param>
        /// <param name="context">The service message context.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<IWotBindingChannel> OpenChannelAsync(
            WotCompiledForm form, IServiceMessageContext context, CancellationToken cancellationToken = default);
    }
}
