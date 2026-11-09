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
    /// Supplies device-specific closed-loop settings and self-optimization.
    /// </summary>
    public interface IClosedLoopControlProvider
    {
        /// <summary>
        /// Commits the complete set of PID parameters.
        /// </summary>
        ValueTask SetParametersAsync(
            PlasticsRubberRequestContext context,
            ArrayOf<PIDParametersDataType> parameters,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits whether automatic tuning owns the controller settings.
        /// </summary>
        ValueTask SetAutomaticModeAsync(
            PlasticsRubberRequestContext context,
            bool automatic,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Requests self-optimization; the authority publishes activity and successful final parameters.
        /// </summary>
        ValueTask StartTuningAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops self-optimization without changing the existing PID parameters.
        /// </summary>
        ValueTask StopTuningAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds optional closed-loop control features without simulating a device's tuning algorithm.
    /// </summary>
    public sealed class ClosedLoopControlBinding : IAsyncDisposable
    {
        /// <summary>
        /// Connects selected methods and settings to their asynchronous authority.
        /// </summary>
        public ClosedLoopControlBinding(
            ClosedLoopControlState node,
            ISystemContext context,
            IClosedLoopControlProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
            : this(node, context, provider, accessPolicy, readCommittedAutomaticMode: null)
        {
        }

        /// <summary>
        /// Binds commands while optionally retaining externally published PID settings and controller mode.
        /// </summary>
        /// <param name="node">The selected generated controller.</param>
        /// <param name="context">The owning address-space context.</param>
        /// <param name="provider">The asynchronous command authority.</param>
        /// <param name="accessPolicy">The original-caller authorization policy.</param>
        /// <param name="readCommittedAutomaticMode">
        /// Supplies the committed automatic-mode state for validation. A non-null reader suppresses
        /// automatic cache updates after writes; null preserves standalone write-through behavior.
        /// </param>
        public ClosedLoopControlBinding(
            ClosedLoopControlState node,
            ISystemContext context,
            IClosedLoopControlProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            Func<bool>? readCommittedAutomaticMode)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_binding = new PlasticsRubberMethodBinding(
                node, context, accessPolicy, suppressCacheUpdate: readCommittedAutomaticMode != null);
            if (node.AutoTuningOn != null)
            {
                m_binding.Bind(node.AutoTuningOn, provider.StartTuningAsync);
            }
            if (node.AutoTuningOff != null)
            {
                m_binding.Bind(node.AutoTuningOff, provider.StopTuningAsync);
            }
            if (node.AutomaticControllerMode != null)
            {
                m_binding.BindWrite(node.AutomaticControllerMode, (request, value, ct) =>
                {
                    if (!value.TryGetValue(out bool automatic))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTypeMismatch,
                            "AutomaticControllerMode requires a Boolean.");
                    }
                    return provider.SetAutomaticModeAsync(request, automatic, ct);
                });
            }
            if (node.PIDParameters != null)
            {
                m_binding.BindWrite(node.PIDParameters, (request, value, ct) =>
                {
                    if (readCommittedAutomaticMode?.Invoke() ?? node.AutomaticControllerMode?.Value == true)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotWritable,
                            "Automatic tuning owns the PID parameters.");
                    }
                    if (!value.TryGetStructure(out ArrayOf<PIDParametersDataType> parameters))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTypeMismatch,
                            "An array of PIDParametersDataType is required.");
                    }
                    foreach (PIDParametersDataType parameter in parameters)
                    {
                        if (parameter == null ||
                            !IsFinite(parameter.P) || !IsFinite(parameter.I) || !IsFinite(parameter.D))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadOutOfRange,
                                "PID parameters must be non-null and finite.");
                        }
                    }
                    return provider.SetParametersAsync(request, parameters, ct);
                });
            }
        }

        /// <summary>
        /// Gets the generated control instance and its independently published activity.
        /// </summary>
        public ClosedLoopControlState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Determines whether a controller coefficient is finite without restricting its sign.
        /// </summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// The common authorization and lifetime binding.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
