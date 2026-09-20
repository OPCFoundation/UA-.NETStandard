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

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Optional preparation of registry projection metadata on the actual registry owner.
    /// </summary>
    public interface IWotPreparedRegistryPublicationService : IWotRegistryService
    {
        /// <summary>
        /// Gets whether this owner can prepare an authoritative store decision without publishing it.
        /// </summary>
        bool SupportsPreparedPublication { get; }

        /// <summary>
        /// Builds a private registry/graph image against the exact captured registry snapshot.
        /// The publication generation is assigned by the coordinator, not by this method.
        /// </summary>
        ValueTask<IWotPreparedRegistryPublication> PreparePublicationAsync(
            WotRegistrySnapshot expectedSnapshot,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns one prepared registry decision and its deferred in-memory publication.
    /// </summary>
    public interface IWotPreparedRegistryPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact registry snapshot captured before preparation.
        /// </summary>
        WotRegistrySnapshot PreviousSnapshot { get; }

        /// <summary>
        /// Gets the complete intended metadata and graph image.
        /// </summary>
        WotRegistrySnapshot IntendedSnapshot { get; }

        /// <summary>
        /// Gets whether the store conclusively committed the intended generation.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Gets a committed durability warning retained from the authoritative store outcome.
        /// </summary>
        WotRegistryCommitDurabilityUncertainException? DurabilityWarning { get; }

        /// <summary>
        /// Rechecks the captured owner state and executes the durable decision.
        /// Confirmed noncommit and indeterminate outcomes escape; committed durability warnings are retained.
        /// </summary>
        ValueTask DecideAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the already-committed metadata when the coordinator switches the runtime image.
        /// It never makes another store decision.
        /// </summary>
        void Publish();
    }
}
