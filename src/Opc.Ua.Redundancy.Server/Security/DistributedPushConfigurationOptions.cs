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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for the distributed
    /// (high-availability) PushManagement transaction coordinator and the
    /// shared pending-certificate-key store. These control the shared-store
    /// lease that enforces a single server-wide PushManagement transaction
    /// (OPC 10000-12 §§7.10.2-7.10.11) across a <c>RedundantServerSet</c>.
    /// </summary>
    public sealed class DistributedPushConfigurationOptions
    {
        /// <summary>
        /// Gets or sets a factory that creates the record protector applied to
        /// every pending private key written to the shared store (authenticated
        /// encryption).
        /// </summary>
        /// <remarks>
        /// When this value is <c>null</c> and no <see cref="IRecordProtector"/>
        /// is otherwise registered, an external (non in-memory) shared store
        /// causes the pending-key store to fail closed rather than persist
        /// private keys without confidentiality or integrity protection.
        /// Production deployments backed by a network store MUST configure an
        /// <see cref="AesCbcHmacRecordProtector"/>; see
        /// <c>docs/HighAvailability.md</c>.
        /// </remarks>
        public Func<IServiceProvider, IRecordProtector>? RecordProtectorFactory { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for this server replica. It is
        /// written into the shared transaction lease so a standby replica can
        /// tell whether the lease is its own (safe to renew) or another
        /// replica's (must wait until it expires). Defaults to the machine
        /// name; give every replica in a set a distinct value.
        /// </summary>
        public string ReplicaId { get; set; } = Environment.MachineName;

        /// <summary>
        /// Gets or sets the shared-store key prefix under which all
        /// distributed PushManagement state is stored (the transaction lease
        /// and every pending regenerated private key). Keeps the
        /// PushManagement keyspace separate from other mirrored state (session,
        /// subscription, address-space).
        /// </summary>
        public string KeyPrefix { get; set; } = "pushconfig/";

        /// <summary>
        /// Gets or sets how long an acquired transaction lease remains valid in
        /// the shared store without renewal. A replica that stops renewing (for
        /// example, because it crashed) loses ownership once the lease expires,
        /// allowing a standby replica to start a new transaction. Must be
        /// greater than <see cref="RenewInterval"/>.
        /// </summary>
        public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets how often the background loop renews the transaction
        /// lease while this replica owns the transaction. Should be well below
        /// <see cref="LeaseDuration"/> so the lease never lapses under normal
        /// operation.
        /// </summary>
        public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets how long a successful ownership reservation keeps the
        /// lease alive before the first <c>Stage</c> makes the transaction
        /// active. This bridges the asynchronous window between the ownership
        /// acquisition (at the async boundary) and the synchronous
        /// <c>Stage</c> call, and bounds how long an aborted operation (one
        /// that reserves ownership but never stages) can hold the lease.
        /// Defaults to <see cref="LeaseDuration"/>.
        /// </summary>
        /// <remarks>
        /// When <see cref="TimeSpan.Zero"/> (the default sentinel), the
        /// coordinator uses <see cref="LeaseDuration"/>.
        /// </remarks>
        public TimeSpan ReservationTimeout { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Gets or sets a value indicating whether ownership of a PushManagement
        /// transaction additionally requires that this replica is the elected
        /// leader of the redundant server set.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to <c>false</c>: the shared-store transaction lease alone
        /// already enforces a single server-wide transaction across replicas,
        /// so any replica a client connects to may own it.
        /// </para>
        /// <para>
        /// Set to <c>true</c> to funnel every PushManagement transaction through
        /// the leader replica. When the Kubernetes extension is configured
        /// (<c>UseKubernetesLeaderElection</c>), leadership is decided by the
        /// Kubernetes-native <c>Lease</c> already registered as the
        /// <see cref="ILeaderElection"/> service, reused here without creating a
        /// second Kubernetes client. The same seam composes with the Raft and
        /// shared-store leader-election providers.
        /// </para>
        /// </remarks>
        public bool RequireLeadership { get; set; }

        /// <summary>
        /// Gets the effective reservation timeout, falling back to
        /// <see cref="LeaseDuration"/> when <see cref="ReservationTimeout"/> is
        /// not positive.
        /// </summary>
        internal TimeSpan EffectiveReservationTimeout
            => ReservationTimeout > TimeSpan.Zero ? ReservationTimeout : LeaseDuration;

        /// <summary>
        /// Gets the shared-store key that holds the single transaction lease.
        /// </summary>
        internal string TransactionLeaseKey => KeyPrefix + "transaction/owner";

        /// <summary>
        /// Gets the shared-store key prefix under which pending regenerated
        /// private keys are stored, scoped per certificate group and type.
        /// </summary>
        internal string PendingKeyPrefix => KeyPrefix + "pendingkey/";

        /// <summary>
        /// Validates the option values and throws when they are inconsistent.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A required value is missing or the lease/renew intervals are not
        /// positive or are inconsistent.
        /// </exception>
        internal void Validate()
        {
            if (string.IsNullOrEmpty(ReplicaId))
            {
                throw new ArgumentException(
                    "DistributedPushConfigurationOptions.ReplicaId must not be null or empty.");
            }
            if (string.IsNullOrEmpty(KeyPrefix))
            {
                throw new ArgumentException(
                    "DistributedPushConfigurationOptions.KeyPrefix must not be null or empty.");
            }
            if (LeaseDuration <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "DistributedPushConfigurationOptions.LeaseDuration must be positive.");
            }
            if (RenewInterval <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "DistributedPushConfigurationOptions.RenewInterval must be positive.");
            }
            if (RenewInterval >= LeaseDuration)
            {
                throw new ArgumentException(
                    "DistributedPushConfigurationOptions.RenewInterval must be less than LeaseDuration.");
            }
        }
    }
}
