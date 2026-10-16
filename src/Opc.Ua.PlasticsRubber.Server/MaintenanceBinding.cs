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
    /// Commits a maintenance reset at the authoritative device or persistence boundary.
    /// </summary>
    public interface IMaintenanceProvider
    {
        /// <summary>
        /// Completes only after the reset has committed, or throws a service-result exception.
        /// </summary>
        ValueTask ResetAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements the common maintenance reset while preserving lifetime operation counters.
    /// </summary>
    public sealed class MaintenanceBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the mandatory Reset method of a generated Maintenance instance.
        /// </summary>
        public MaintenanceBinding(
            MaintenanceState node,
            ISystemContext context,
            IMaintenanceProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            MethodState reset = node.Reset ??
                throw new ArgumentException("The mandatory maintenance Reset method is missing.", nameof(node));
            if (node.Status == null)
            {
                throw new ArgumentException("The mandatory maintenance Status property is missing.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            m_binding.Bind(reset, async (request, ct) =>
            {
                await provider.ResetAsync(request, ct).ConfigureAwait(false);
                if (node.RemainingInterval != null && node.Interval != null)
                {
                    node.RemainingInterval.Value = node.Interval.Value;
                    node.RemainingInterval.StatusCode = StatusCodes.Good;
                    node.RemainingInterval.Timestamp = DateTimeUtc.Now;
                }
                node.Status.Value = MaintenanceStatusEnumeration.NOT_DUE;
                node.Status.StatusCode = StatusCodes.Good;
                node.Status.Timestamp = DateTimeUtc.Now;
                node.ClearChangeMasks(context, true);
            });
        }

        /// <summary>
        /// Gets the generated instance; its interval and total-operation values share one unit.
        /// </summary>
        public MaintenanceState Node { get; }

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
