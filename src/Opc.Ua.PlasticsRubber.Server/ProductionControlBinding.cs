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
    /// Commits production permissions and supervision at the machine's authoritative control boundary.
    /// </summary>
    /// <remarks>
    /// The provider owns the actual watchdog, MES connection lifecycle and single MES control lease.
    /// It must withdraw production and interrupt an active production job on supervision loss.
    /// Keeping these operations at the authority avoids a second, independent production clock
    /// in an OPC UA view and permits a PLC or a durable coordinator to enforce the deadline.
    /// </remarks>
    public interface IProductionControlProvider
    {
        /// <summary>
        /// Acquires or verifies the one MES controller for connection supervision and watchdog operations.
        /// </summary>
        /// <remarks>
        /// Reject a different live controller and release its lease when its session ends.
        /// Read-only observers must remain allowed.
        /// </remarks>
        ValueTask EnsureMesControlAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits automatic-run permission, stopping a running machine when permission is withdrawn.
        /// </summary>
        ValueTask SetAutomaticRunAsync(
            PlasticsRubberRequestContext context, bool enabled, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits production release and the associated production/job status changes.
        /// </summary>
        /// <remarks>
        /// Withdrawing release changes PRODUCTION to READY_FOR_PRODUCTION and a production job
        /// to JOB_INTERRUPTED. The provider must publish the committed absolute statuses.
        /// </remarks>
        ValueTask SetProductionReleaseAsync(
            PlasticsRubberRequestContext context, bool released, CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits whether loss of the controlling MES connection withdraws production.
        /// </summary>
        ValueTask SetProductionOnlyWithMesAsync(
            PlasticsRubberRequestContext context, bool required, CancellationToken cancellationToken = default);

        /// <summary>
        /// Arms the authoritative watchdog in seconds, or disables it when seconds equals minus one.
        /// </summary>
        ValueTask SetWatchdogTimeAsync(
            PlasticsRubberRequestContext context, int seconds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts the watchdog using the last configured interval.
        /// </summary>
        ValueTask ResetWatchdogAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests separation of a test sample using the device's configured sample size.
        /// </summary>
        ValueTask RequestTestSampleAsync(
            PlasticsRubberRequestContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds the complete common production-control protocol to an explicit control authority.
    /// </summary>
    public sealed class ProductionControlBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds mandatory permissions and all selected optional supervision/sample operations.
        /// </summary>
        public ProductionControlBinding(
            ProductionControlState node,
            ISystemContext context,
            IProductionControlProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node.EnableAutomaticRun == null || node.DisableAutomaticRun == null ||
                node.AutomaticRunEnabled == null || node.ProductionReleasedByMES == null ||
                node.ProductionStatus == null)
            {
                throw new ArgumentException("Mandatory production-control members are missing.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            m_binding.Bind(node.EnableAutomaticRun, async (request, ct) =>
            {
                await provider.SetAutomaticRunAsync(request, true, ct).ConfigureAwait(false);
                node.AutomaticRunEnabled.Value = true;
                node.AutomaticRunEnabled.ClearChangeMasks(context, false);
            });
            m_binding.Bind(node.DisableAutomaticRun, async (request, ct) =>
            {
                await provider.SetAutomaticRunAsync(request, false, ct).ConfigureAwait(false);
                node.AutomaticRunEnabled.Value = false;
                node.AutomaticRunEnabled.ClearChangeMasks(context, false);
            });
            m_binding.BindWrite(node.ProductionReleasedByMES, (request, value, ct) =>
                provider.SetProductionReleaseAsync(request, BooleanValue(value), ct));
            if (node.ProductionOnlyWithMES != null)
            {
                m_binding.BindWrite(node.ProductionOnlyWithMES, async (request, value, ct) =>
                {
                    bool required = BooleanValue(value);
                    await provider.EnsureMesControlAsync(request, ct).ConfigureAwait(false);
                    await provider.SetProductionOnlyWithMesAsync(request, required, ct).ConfigureAwait(false);
                });
            }
            if (node.SetWatchDogTime != null)
            {
                node.SetWatchDogTime.OnCallAsync = async (call, method, target, seconds, ct) =>
                    new SetWatchDogTimeMethodStateResult
                    {
                        ServiceResult = await m_binding.ExecuteAsync(
                            call, target, method.NodeId,
                            async (request, token) =>
                            {
                                if (seconds < -1)
                                {
                                    throw ServiceResultException.Create(
                                        StatusCodes.BadOutOfRange, "The watchdog accepts -1 or nonnegative seconds.");
                                }
                                await provider.EnsureMesControlAsync(request, token).ConfigureAwait(false);
                                await provider.SetWatchdogTimeAsync(request, seconds, token).ConfigureAwait(false);
                            },
                            ct).ConfigureAwait(false)
                    };
                m_binding.Track(node.SetWatchDogTime);
            }
            if (node.ResetWatchDog != null)
            {
                m_binding.Bind(node.ResetWatchDog, async (request, ct) =>
                {
                    await provider.EnsureMesControlAsync(request, ct).ConfigureAwait(false);
                    await provider.ResetWatchdogAsync(request, ct).ConfigureAwait(false);
                });
            }
            if (node.RequestTestSample != null)
            {
                m_binding.Bind(node.RequestTestSample, provider.RequestTestSampleAsync);
            }
        }

        /// <summary>
        /// Gets the generated control object whose production status is owned by the provider.
        /// </summary>
        public ProductionControlState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Enforces the exact scalar wire type of a production permission.
        /// </summary>
        private static bool BooleanValue(Variant value)
        {
            return value.TryGetValue(out bool result)
                ? result
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A Boolean is required.");
        }

        /// <summary>
        /// The common authorized operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
