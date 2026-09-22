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
    /// Resolves an explicit registry recovery requirement through the existing deciding store owner.
    /// </summary>
    public interface IWotRegistryRecoveryResolver : IWotRegistryService
    {
        /// <summary>
        /// Reloads authoritative evidence only when recovery is required.
        /// Returns whether evidence was reloaded or still awaits runtime publication.
        /// Unresolved evidence remains an error and keeps mutation blocked.
        /// </summary>
        ValueTask<bool> ResolveRecoveryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Admits recovery of resolved evidence while conflicting mutations remain excluded.
        /// The returned owner cannot prepare a new durable decision.
        /// </summary>
        ValueTask<IWotRegistryRecoveryPublication> BeginRecoveryPublicationAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a hosted projection that must acknowledge recovered metadata before mutation resumes.
        /// Disposing the registration detaches that projection from subsequent recovery completions.
        /// </summary>
        IDisposable RegisterRecoveryProjection(IWotRegistryRecoveryProjection projection);
    }

    /// <summary>
    /// Reconciles an existing browseable registry projection without repeating committed event intent.
    /// </summary>
    public interface IWotRegistryRecoveryProjection
    {
        /// <summary>
        /// Applies the exact recovered snapshot while conflicting registry mutation remains blocked.
        /// Implementations use the supplied metadata and must not reload or mutate its deciding store.
        /// </summary>
        ValueTask SynchronizeAsync(WotRegistrySnapshot snapshot, CancellationToken cancellationToken = default);
    }

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
    /// Optional invocation-wide publication admission on the authoritative registry owner.
    /// </summary>
    public interface IWotInvocationRegistryPublicationService : IWotPreparedRegistryPublicationService
    {
        /// <summary>
        /// Excludes registry mutations until every unit and the final invocation bookkeeping are complete.
        /// </summary>
        ValueTask<IWotRegistryPublication> BeginPublicationAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Holds the registry side of one invocation, reusing the existing deciding store owner.
    /// </summary>
    public interface IWotRegistryPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets the complete current authoritative snapshot, including this invocation's accepted units.
        /// </summary>
        WotRegistrySnapshot Current { get; }

        /// <summary>
        /// Prepares the next sequential unit against the exact current snapshot.
        /// </summary>
        ValueTask<IWotPreparedRegistryPublication> PrepareAsync(
            WotRegistrySnapshot expectedSnapshot,
            ArrayOf<WotResourceProjection> projections,
            uint refreshGeneration,
            ByteString canonicalViewGraphState = default,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Recovers a durable image through the same registry invocation owner without deciding another generation.
    /// </summary>
    public interface IWotRegistryRecoveryPublication : IWotRegistryPublication
    {
        /// <summary>
        /// Prepares the local runtime representation of an existing committed snapshot.
        /// Only derived Resource root NodeIds may be rebased; authoritative metadata remains unchanged.
        /// </summary>
        ValueTask<IWotPreparedRegistryRecovery> PrepareRecoveryAsync(
            WotRegistrySnapshot expectedSnapshot,
            WotRegistrySnapshot runtimeSnapshot,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns validation and local publication of an already committed registry image.
    /// </summary>
    public interface IWotPreparedRegistryRecovery : IAsyncDisposable
    {
        /// <summary>
        /// Gets the local representation of the durable image, including rebased Resource root NodeIds.
        /// </summary>
        WotRegistrySnapshot RuntimeSnapshot { get; }

        /// <summary>
        /// Revalidates the existing deciding record and retains its authority through the runtime switch.
        /// </summary>
        ValueTask ValidateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the validated local representation without a store write, generation change or repeated event.
        /// Mutation remains blocked until <see cref="CompleteAsync"/> acknowledges its hosted projections.
        /// </summary>
        void Publish();

        /// <summary>
        /// Completes notification-silent hosted projection reconciliation before mutation resumes.
        /// Post-publication completion cannot be cancelled; failures retain the recovery requirement.
        /// </summary>
        ValueTask CompleteAsync();
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
        /// It never makes another store decision. The caller installs its prepared bookkeeping before
        /// calling this method so Changed observers see the committed unit. Observer failures surface
        /// as committed warnings after publication and do not prevent the other observers from running.
        /// </summary>
        void Publish();
    }
}
