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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.ISA95.Server.Builders
{
    /// <summary>
    /// Binds an OPC-10030 <see cref="GeoSpatialLocationState"/> variable to an
    /// <see cref="IGeoLocationProvider"/>. Reads are served through the
    /// asynchronous value hook (never blocking the stack on the provider), and,
    /// when the provider supports push, a background loop keeps the cached
    /// value, status code and source timestamp in sync.
    /// </summary>
    /// <remarks>
    /// The variable carries text rather than coordinates, so an
    /// <see cref="IGeoLocationTextFormatter"/> projects each sample into the
    /// literals it publishes. This is what lets one provider implementation
    /// serve both this model and the OPC 10000-211 coordinate model.
    /// </remarks>
    public static class Isa95GeoSpatialLocationBinder
    {
        /// <summary>
        /// Binds <paramref name="state"/> to <paramref name="provider"/>.
        /// </summary>
        /// <param name="context">
        /// The system context used to clear change masks when updates arrive.
        /// </param>
        /// <param name="state">
        /// The geospatial location variable to bind.
        /// </param>
        /// <param name="provider">
        /// The provider backing the variable.
        /// </param>
        /// <param name="sourceId">
        /// The provider-local source whose location the variable publishes.
        /// </param>
        /// <param name="formatter">
        /// Projects a sample into location literals; defaults to
        /// <see cref="WktGeoLocationTextFormatter"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// A token that stops the optional update loop when cancelled.
        /// </param>
        /// <returns>
        /// A handle that stops the update loop when disposed.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/>, <paramref name="state"/> or
        /// <paramref name="provider"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="sourceId"/> is empty.
        /// </exception>
        public static IDisposable Bind(
            ISystemContext context,
            GeoSpatialLocationState state,
            IGeoLocationProvider provider,
            string sourceId,
            IGeoLocationTextFormatter? formatter = null,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException(
                    "A stable source identifier is required.",
                    nameof(sourceId));
            }

            IGeoLocationTextFormatter effectiveFormatter =
                formatter ?? WktGeoLocationTextFormatter.Instance;

            state.OnReadValueAsync = async (ctx, node, indexRange, dataEncoding, ct) =>
            {
                GeoLocationSample sample = await provider
                    .ReadAsync(sourceId, ct).ConfigureAwait(false);
                return new AttributeReadResult(
                    ServiceResult.Good,
                    ToVariant(effectiveFormatter, sample),
                    sample.StatusCode,
                    sample.GetEffectiveSourceTimestamp());
            };

            ILogger logger = context.Telemetry.CreateLogger(
                typeof(Isa95GeoSpatialLocationBinder).FullName!);
            return new Binding(
                context,
                state,
                provider,
                sourceId,
                effectiveFormatter,
                logger,
                cancellationToken);
        }

        private static void Apply(
            ISystemContext context,
            GeoSpatialLocationState state,
            IGeoLocationTextFormatter formatter,
            GeoLocationSample sample)
        {
            state.Value = ToVariant(formatter, sample);
            state.StatusCode = sample.StatusCode;
            state.Timestamp = sample.GetEffectiveSourceTimestamp();
            state.ClearChangeMasks(context, false);
        }

        /// <summary>
        /// Projects a sample into the value the variable publishes.
        /// </summary>
        /// <remarks>
        /// OPC 10030 declares GeoSpatialLocationType with ValueRank
        /// OneOrMoreDimensions, so the value is an array of literals rather
        /// than a single string.
        /// </remarks>
        private static Variant ToVariant(
            IGeoLocationTextFormatter formatter,
            GeoLocationSample sample)
        {
            ArrayOf<string> literals = formatter.Format(sample);
            return literals.Count == 0 ? Variant.Null : new Variant(literals);
        }

        private sealed class Binding : IDisposable
        {
            public Binding(
                ISystemContext context,
                GeoSpatialLocationState state,
                IGeoLocationProvider provider,
                string sourceId,
                IGeoLocationTextFormatter formatter,
                ILogger logger,
                CancellationToken cancellationToken)
            {
                m_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (provider.SupportsPush)
                {
                    _ = RunAsync(
                        context,
                        state,
                        provider.WatchAsync(sourceId, m_cts.Token),
                        formatter,
                        logger,
                        m_cts.Token);
                }
            }

            private static async Task RunAsync(
                ISystemContext context,
                GeoSpatialLocationState state,
                IAsyncEnumerable<GeoLocationSample> updates,
                IGeoLocationTextFormatter formatter,
                ILogger logger,
                CancellationToken cancellationToken)
            {
                try
                {
                    await foreach (GeoLocationSample sample in updates
                        .WithCancellation(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        Apply(context, state, formatter, sample);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on disposal or cancellation.
                }
                catch (Exception exception)
                {
                    // Surface the provider failure so it is observable; the loop
                    // then stops. Reads continue to report the fault through the
                    // asynchronous value hook.
                    logger.GeoSpatialLocationUpdateLoopFailed(exception);
                }
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_cts.Cancel();
                m_cts.Dispose();
            }

            private readonly CancellationTokenSource m_cts;
            private bool m_disposed;
        }
    }

    /// <summary>
    /// Source-generated log messages for
    /// <see cref="Isa95GeoSpatialLocationBinder"/>. A literal event id is used
    /// because the containing assembly does not yet declare a shared
    /// <c>EventIds</c> class; the value sits in a dedicated high range.
    /// </summary>
    internal static partial class Isa95GeoSpatialLocationBinderLog
    {
        [LoggerMessage(
            EventId = 9500,
            Level = LogLevel.Error,
            Message = "The ISA-95 geospatial location update stream failed; the " +
                "background update loop has stopped.")]
        public static partial void GeoSpatialLocationUpdateLoopFailed(
            this ILogger logger,
            Exception exception);
    }
}