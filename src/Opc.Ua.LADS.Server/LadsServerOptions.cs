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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Redundancy;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Configures the LADS server runtime.
    /// </summary>
    public sealed class LadsServerOptions
    {
        /// <summary>
        /// Gets or sets the clock for LADS execution and counters.
        /// Null selects the registered clock; this override does not change transport or session timing.
        /// </summary>
        public TimeProvider? TimeProvider { get; set; }

        /// <summary>
        /// Gets or sets the stored-data provider. Null selects the registered provider or memory default.
        /// </summary>
        public ILadsStore? Store { get; set; }

        /// <summary>
        /// Gets the lease group used by the optional authority-backed store.
        /// </summary>
        public string? LeasePublicationGroupIdentity { get; private set; }

        /// <summary>
        /// Gets the provider for the current lease token used by the optional authority-backed store.
        /// </summary>
        public Func<LeasePublicationToken>? LeasePublicationTokenProvider { get; private set; }

        /// <summary>
        /// Gets or sets equipment-readiness confirmation before starting a recovered interrupted device.
        /// Missing confirmation or a false result blocks the start.
        /// Exceptions and cancellation propagate to the caller.
        /// The token observes caller cancellation, server shutdown and device disposal.
        /// The callback must not issue another start or dispose the device while its confirmation is pending.
        /// </summary>
        public Func<ILadsDevice, CancellationToken, ValueTask<bool>>? ConfirmInterruptedDeviceReadinessAsync
        { get; set; }

        /// <summary>
        /// Gets or sets the standalone owner's application namespace.
        /// Composing into an existing Machinery owner uses that owner's namespace instead.
        /// </summary>
        public string InstanceNamespaceUri { get; set; } = "urn:opcua-netstandard:lads:instances";

        /// <summary>
        /// Gets or sets the largest result file, in bytes, that clients can read through the <c>FileType</c>
        /// methods. The default is 16 MiB.
        /// </summary>
        public int MaxResultFileBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets how many results each program manager keeps. When a new result exceeds the limit the
        /// oldest result is removed. The default is 100.
        /// </summary>
        public int MaxRetainedResults { get; set; } = 100;

        /// <summary>
        /// Gets the Machinery parts LADS needs from its owner.
        /// </summary>
        public MachineryParts RequiredMachineryParts => MachineryParts.BuildingBlocks;

        /// <summary>
        /// Selects the authority-backed LADS store for a lease group.
        /// </summary>
        /// <param name="groupIdentity">The lease group that scopes LADS snapshots.</param>
        /// <param name="tokenProvider">Returns the caller's current fencing identity.</param>
        /// <returns>This options instance.</returns>
        public LadsServerOptions UseLeasePublicationStore(
            string groupIdentity,
            Func<LeasePublicationToken> tokenProvider)
        {
            if (string.IsNullOrWhiteSpace(groupIdentity))
            {
                throw new ArgumentException("A lease group is required.", nameof(groupIdentity));
            }
            if (tokenProvider == null)
            {
                throw new ArgumentNullException(nameof(tokenProvider));
            }
            LeasePublicationGroupIdentity = groupIdentity;
            LeasePublicationTokenProvider = tokenProvider;
            return this;
        }

        /// <summary>
        /// Rejects invalid limits and application namespaces.
        /// </summary>
        /// <exception cref="ArgumentException">A namespace or lease group is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        /// <exception cref="InvalidOperationException">
        /// A custom store conflicts with strict lease publication selection.
        /// </exception>
        public void Validate()
        {
            if (MaxResultFileBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxResultFileBytes));
            }
            if (MaxRetainedResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRetainedResults));
            }
            if (!Uri.TryCreate(InstanceNamespaceUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException(
                    "An absolute application namespace URI is required.",
                    nameof(InstanceNamespaceUri));
            }
            foreach (string model in LadsModel.GetNamespaceUris())
            {
                if (string.Equals(model, InstanceNamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "The application namespace must not be a model namespace.",
                        nameof(InstanceNamespaceUri));
                }
            }
            if (string.Equals(InstanceNamespaceUri, global::Opc.Ua.Namespaces.OpcUa, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The application namespace must not be the OPC UA namespace.",
                    nameof(InstanceNamespaceUri));
            }
            if ((LeasePublicationGroupIdentity == null) != (LeasePublicationTokenProvider == null))
            {
                throw new InvalidOperationException(
                    "Lease publication selection requires both a group and a token provider.");
            }
            if (LeasePublicationGroupIdentity != null && Store != null)
            {
                throw new InvalidOperationException(
                    "A custom LADS store cannot be combined with lease publication selection.");
            }
        }

        /// <summary>
        /// Copies the settings to another instance.
        /// </summary>
        /// <param name="target">The instance to update.</param>
        internal void CopyTo(LadsServerOptions target)
        {
            target.InstanceNamespaceUri = InstanceNamespaceUri;
            target.MaxResultFileBytes = MaxResultFileBytes;
            target.MaxRetainedResults = MaxRetainedResults;
            target.Store = Store;
            target.LeasePublicationGroupIdentity = LeasePublicationGroupIdentity;
            target.LeasePublicationTokenProvider = LeasePublicationTokenProvider;
            target.ConfirmInterruptedDeviceReadinessAsync = ConfirmInterruptedDeviceReadinessAsync;
        }
    }
}
