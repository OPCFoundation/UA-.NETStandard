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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Shares native transaction and exact-registration inspection helpers without retries or legacy API adapters.
    /// </summary>
    internal static class LifecycleInspection
    {
        /// <summary>
        /// Captures publication evidence before checking the expected generation's membership.
        /// Subsequent changes invalidate this evidence instead of rebasing a replacement or removal.
        /// </summary>
        internal static ILifecycleCapture Capture(
            INodeManagerLifecycle lifecycle, params NodeManagerRegistration[] registrations)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            foreach (NodeManagerRegistration registration in registrations)
            {
                if (registration is null)
                {
                    throw new ArgumentNullException(nameof(registrations));
                }
                if (!(lifecycle.Registrations.ToArray() ?? []).Any(current =>
                    ReferenceEquals(current.Identity, registration.Identity) &&
                    ReferenceEquals(current.Slot, registration.Slot)))
                {
                    throw new InvalidOperationException(
                        "The inspected registration is stale or belongs to another owner.");
                }
            }
            return capture;
        }

        /// <summary>
        /// Obtains a strong inspection handle only when a test explicitly requires its original owner.
        /// </summary>
        internal static NodeManagerRegistration Registration(NodeManagerRegistrationSnapshot snapshot)
        {
            Assert.That(snapshot.TryGetRegistration(out NodeManagerRegistration? registration), Is.True);
            return registration ?? throw new AssertionException("The registration inspection handle is unavailable.");
        }

        /// <summary>
        /// Captures before reacquiring and validating a weak exact-registration descriptor.
        /// </summary>
        internal static ILifecycleCapture Capture(
            INodeManagerLifecycle lifecycle, NodeManagerRegistrationSnapshot snapshot)
        {
            ILifecycleCapture capture = lifecycle.CaptureChanges();
            NodeManagerRegistration registration = Registration(snapshot);
            if (!(lifecycle.Registrations.ToArray() ?? []).Any(current =>
                ReferenceEquals(current.Identity, registration.Identity) &&
                ReferenceEquals(current.Slot, registration.Slot)))
            {
                throw new InvalidOperationException("The inspected registration is stale or belongs to another owner.");
            }
            return capture;
        }

        /// <summary>
        /// Publishes one test unit through the native capture, writer and prepared-owner contract.
        /// </summary>
        internal static Task<LifecycleResult> CommitChangeAsync(
            INodeManagerLifecycle lifecycle,
            LifecycleChange change,
            CancellationToken cancellationToken = default)
        {
            return CommitChangeAsync(lifecycle.CaptureChanges(), change, cancellationToken);
        }

        /// <summary>
        /// Publishes on evidence captured before exact registration inspection, retaining the full result.
        /// </summary>
        internal static async Task<LifecycleResult> CommitChangeAsync(
            ILifecycleCapture capture,
            LifecycleChange change,
            CancellationToken cancellationToken = default)
        {
            await using ILifecycleWriter writer = await capture.BeginAsync(cancellationToken).ConfigureAwait(false);
            await using IPreparedChanges prepared = await writer.PrepareAsync([change], cancellationToken)
                .ConfigureAwait(false);
            LifecycleResult result = await prepared.CommitAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            Assert.That(result.CleanupFailure, Is.Null);
            Assert.That(result.Issues, Is.Empty);
            return result;
        }
    }
}
