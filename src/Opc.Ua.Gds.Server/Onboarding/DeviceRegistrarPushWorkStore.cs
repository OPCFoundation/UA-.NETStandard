/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// The persisted state of accepted push onboarding work.
    /// </summary>
    public enum DeviceRegistrarPushWorkState : uint
    {
        /// <summary>
        /// The work has been accepted but no processing attempt has started.
        /// </summary>
        Pending,

        /// <summary>
        /// A processing attempt started and may have been interrupted before completion was recorded.
        /// </summary>
        Processing,

        /// <summary>
        /// An attempt ended without a durable completion record and requires reconciliation before retry.
        /// </summary>
        Interrupted
    }

    /// <summary>
    /// One accepted registrar announcement retained until processing is durably complete.
    /// </summary>
    public sealed record DeviceRegistrarPushWorkItem(
        Guid Id,
        ApplicationDescription Application,
        DeviceRegistrarPushWorkState State);

    /// <summary>
    /// Persists accepted registrar announcements and attempt transitions across process restarts.
    /// </summary>
    public interface IDeviceRegistrarPushWorkStore : IDisposable
    {
        /// <summary>
        /// Reads all accepted work, including attempts whose completion was not recorded.
        /// </summary>
        ValueTask<ArrayOf<DeviceRegistrarPushWorkItem>> ReadAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists an accepted announcement unless the pending-work capacity is full.
        /// </summary>
        ValueTask EnqueueAsync(
            Guid id,
            ApplicationDescription application,
            int capacity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks an accepted item as processing and returns its prior state.
        /// </summary>
        ValueTask<DeviceRegistrarPushWorkState> BeginAttemptAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records that processing ended without a durable completion record.
        /// </summary>
        ValueTask MarkInterruptedAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a completed item from durable acceptance.
        /// </summary>
        ValueTask CompleteAsync(
            Guid id,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reconciles the observable remote and retained state of interrupted push work before it is retried.
    /// </summary>
    public interface IDeviceRegistrarPushRecoveryProcessor
    {
        /// <summary>
        /// Inspects the retained and remote state before the scheduler resumes processing.
        /// Implementations must throw when the state cannot safely be reconciled.
        /// </summary>
        ValueTask ReconcileInterruptedAsync(
            ApplicationDescription application,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Creates a validated, owned application-description snapshot without trusting virtual Clone implementations.
    /// </summary>
    internal static class DeviceRegistrarPushApplicationSnapshot
    {
        /// <summary>
        /// Copies each schema field, array, and localized application name into a standard application description.
        /// </summary>
        public static ApplicationDescription Create(ApplicationDescription application)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            var snapshot = new ApplicationDescription
            {
                ApplicationName = application.ApplicationName.Copy(),
                ApplicationUri = application.ApplicationUri,
                ApplicationType = application.ApplicationType,
                ProductUri = application.ProductUri,
                GatewayServerUri = application.GatewayServerUri,
                DiscoveryProfileUri = application.DiscoveryProfileUri,
                DiscoveryUrls = application.DiscoveryUrls.IsNull
                    ? default
                    : application.DiscoveryUrls.Span.ToArray().ToArrayOf()
            };
            if (!Uri.TryCreate(snapshot.ApplicationUri, UriKind.Absolute, out _) ||
                snapshot.DiscoveryUrls.IsEmpty ||
                snapshot.ApplicationType is not (ApplicationType.Server or ApplicationType.ClientAndServer))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            for (int index = 0; index < snapshot.DiscoveryUrls.Count; index++)
            {
                if (!Uri.TryCreate(snapshot.DiscoveryUrls[index], UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
            }
            return snapshot;
        }

    }
}
