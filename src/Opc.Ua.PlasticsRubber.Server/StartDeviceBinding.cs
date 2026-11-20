// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Commits requests for an OPC 40083 StartDevice capability.
    /// </summary>
    /// <remarks>
    /// Completion acknowledges the request, not physical completion of the transition.
    /// The device authority publishes Status separately; the binding never fabricates STARTED.
    /// </remarks>
    public interface IStartDeviceProvider
    {
        /// <summary>
        /// Requests a device start using the original authenticated caller.
        /// </summary>
        ValueTask StartAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests a device stop using the original authenticated caller.
        /// </summary>
        ValueTask StopAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits a change to the client-controlled start interlock.
        /// </summary>
        ValueTask SetStartBlockedAsync(
            PlasticsRubberRequestContext context,
            bool blocked,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds selected StartDevice methods and its optional client interlock to a device authority.
    /// </summary>
    public sealed class StartDeviceBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds members already selected on the generated instance.
        /// </summary>
        public StartDeviceBinding(
            StartDeviceState node,
            ISystemContext context,
            IStartDeviceProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            if (node.StartRequest != null)
            {
                m_binding.Bind(node.StartRequest, async (request, ct) =>
                {
                    if (node.StartBlockedByClient?.Value == true)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "The client start interlock is active.");
                    }
                    await provider.StartAsync(request, ct).ConfigureAwait(false);
                });
            }
            if (node.StopRequest != null)
            {
                m_binding.Bind(node.StopRequest, provider.StopAsync);
            }
            if (node.StartBlockedByClient != null)
            {
                m_binding.BindWrite(node.StartBlockedByClient, (request, value, ct) =>
                {
                    if (!value.TryGetValue(out bool blocked))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTypeMismatch,
                            "The start interlock requires a Boolean.");
                    }
                    return provider.SetStartBlockedAsync(request, blocked, ct);
                });
            }
        }

        /// <summary>
        /// Gets the generated instance whose status is published by the device authority.
        /// </summary>
        public StartDeviceState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// The shared operation lifetime and authorization binding.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
