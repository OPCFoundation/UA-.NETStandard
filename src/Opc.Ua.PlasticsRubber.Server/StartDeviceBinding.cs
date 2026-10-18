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
