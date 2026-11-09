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
    /// Identifies an OPC 40083 active-job request without conflating it with another job protocol.
    /// </summary>
    public enum ActiveJobOperation
    {
        /// <summary>
        /// Requests production for the current job.
        /// </summary>
        Start,

        /// <summary>
        /// Requests interruption of the current job.
        /// </summary>
        Interrupt,

        /// <summary>
        /// Requests completion, honoring the active job's ContinueAtJobEnd setting.
        /// </summary>
        Finish,

        /// <summary>
        /// Requests a stop after the current cycle.
        /// </summary>
        StopAtCycleEnd,

        /// <summary>
        /// Resets only the current job's cycle and part counters.
        /// </summary>
        ResetJobCounters,

        /// <summary>
        /// Resets only the current box's cycle and part counters.
        /// </summary>
        ResetBoxCounters,

        /// <summary>
        /// Starts a new averaging interval for cycle time.
        /// </summary>
        ResetAverageCycleTime
    }

    /// <summary>
    /// Commits common active-job requests and publishes authoritative absolute job values.
    /// </summary>
    public interface IActiveJobProvider
    {
        /// <summary>
        /// Commits the requested operation, including hardware interlocks and counter resets.
        /// </summary>
        /// <remarks>
        /// Finish honors ContinueAtJobEnd: stop when false, otherwise notify the machine operator.
        /// Counter resets never reset MachineCycleCounter. Completion acknowledges committed
        /// authority state; the provider publishes that state rather than the binding inventing it.
        /// </remarks>
        ValueTask ExecuteAsync(
            PlasticsRubberRequestContext context,
            ActiveJobOperation operation,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the current production lot; the empty string is explicitly valid.
        /// </summary>
        ValueTask SetLotAsync(
            PlasticsRubberRequestContext context,
            string? lot,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the current box identifier.
        /// </summary>
        ValueTask SetBoxAsync(
            PlasticsRubberRequestContext context,
            string? box,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds common and cyclic active-job operations to one authoritative provider.
    /// </summary>
    public sealed class ActiveJobBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds generated job members and optionally links their production-release interlock.
        /// </summary>
        public ActiveJobBinding(
            ActiveJobValuesState node,
            ISystemContext context,
            IActiveJobProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            ProductionControlState? productionControl = null)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node.StartJob == null || node.InterruptJob == null || node.FinishJob == null ||
                node.CurrentLotName == null || node.JobStatus == null)
            {
                throw new ArgumentException("Mandatory active-job members are missing.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            m_binding.Bind(node.StartJob, (request, ct) =>
            {
                if (productionControl?.ProductionReleasedByMES?.Value == false ||
                    productionControl?.AutomaticRunEnabled?.Value == false)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState, "Production has not been released by MES.");
                }
                return provider.ExecuteAsync(request, ActiveJobOperation.Start, ct);
            });
            m_binding.Bind(node.InterruptJob, (request, ct) =>
                provider.ExecuteAsync(request, ActiveJobOperation.Interrupt, ct));
            m_binding.Bind(node.FinishJob, (request, ct) =>
                provider.ExecuteAsync(request, ActiveJobOperation.Finish, ct));
            m_binding.BindWrite(node.CurrentLotName, (request, value, ct) =>
                provider.SetLotAsync(request, StringValue(value), ct));
            if (node.BoxId != null)
            {
                m_binding.BindWrite(node.BoxId, (request, value, ct) =>
                    provider.SetBoxAsync(request, StringValue(value), ct));
            }
            if (node is ActiveCyclicJobValuesState cyclic)
            {
                if (cyclic.StopAtCycleEnd == null || cyclic.ResetJobCounters == null)
                {
                    throw new ArgumentException("Mandatory cyclic-job methods are missing.", nameof(node));
                }
                m_binding.Bind(cyclic.StopAtCycleEnd, (request, ct) =>
                    provider.ExecuteAsync(request, ActiveJobOperation.StopAtCycleEnd, ct));
                m_binding.Bind(cyclic.ResetJobCounters, (request, ct) =>
                    provider.ExecuteAsync(request, ActiveJobOperation.ResetJobCounters, ct));
                if (cyclic.ResetBoxCounters != null)
                {
                    m_binding.Bind(cyclic.ResetBoxCounters, (request, ct) =>
                        provider.ExecuteAsync(request, ActiveJobOperation.ResetBoxCounters, ct));
                }
                if (cyclic.ResetAverageCycleTime != null)
                {
                    m_binding.Bind(cyclic.ResetAverageCycleTime, (request, ct) =>
                        provider.ExecuteAsync(request, ActiveJobOperation.ResetAverageCycleTime, ct));
                }
            }
        }

        /// <summary>
        /// Gets the generated job values, including cyclic values for the corresponding subtype.
        /// </summary>
        public ActiveJobValuesState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Reads a nullable OPC UA String without coercion.
        /// </summary>
        private static string? StringValue(Variant value)
        {
            if (value.IsNull)
            {
                return null;
            }
            return value.TryGetValue(out string? text)
                ? text
                : throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A String is required.");
        }

        /// <summary>
        /// The shared authorized operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
