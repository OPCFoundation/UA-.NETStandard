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

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Marks the domain state a strategy wants tracked alongside a live
    /// projection generation.
    /// </summary>
    /// <remarks>
    /// The engine only stores it and disposes it when the generation is replaced
    /// or retired, which is how a domain keeps hold of the protocol bindings and
    /// secondary artifacts a generation owns without the engine knowing what they
    /// are. Disposal always runs before the projection itself is removed, so a
    /// binding is torn down while the nodes it drives still exist.
    /// </remarks>
    public interface IXRegistryClosureState : IAsyncDisposable
    {
    }

    /// <summary>
    /// What one member of a closure materialized.
    /// </summary>
    public sealed class XRegistryMemberProjection
    {
        /// <summary>
        /// Initializes a member projection.
        /// </summary>
        /// <param name="xid">The member xid.</param>
        /// <exception cref="ArgumentException"><paramref name="xid"/> is null or empty.</exception>
        public XRegistryMemberProjection(string xid)
        {
            ArgumentException.ThrowIfNullOrEmpty(xid);
            Xid = xid;
        }

        /// <summary>
        /// Gets the member xid.
        /// </summary>
        public string Xid { get; }

        /// <summary>
        /// Gets the number of nodes the member materialized.
        /// </summary>
        public int MaterializedNodeCount { get; init; }

        /// <summary>
        /// Gets the root node the member materialized, resolved against the live
        /// server namespace table.
        /// </summary>
        public NodeId RootNodeId { get; init; } = NodeId.Null;
    }

    /// <summary>
    /// The result of preparing one closure for projection.
    /// </summary>
    /// <remarks>
    /// Preparation is where every domain-specific decision is made - parsing,
    /// validation, conversion and binding planning - so the engine can treat the
    /// commit itself as mechanical.
    /// </remarks>
    public sealed class XRegistryClosurePreparation
    {
        private XRegistryClosurePreparation(bool succeeded)
        {
            Succeeded = succeeded;
        }

        /// <summary>
        /// Creates a successful preparation.
        /// </summary>
        /// <param name="document">
        /// The projection document, or <c>null</c> when the closure materializes
        /// nothing through the projection host.
        /// </param>
        /// <returns>The preparation.</returns>
        public static XRegistryClosurePreparation Ready(XRegistryProjectionDocument? document)
        {
            return new XRegistryClosurePreparation(true) { Document = document };
        }

        /// <summary>
        /// Creates a failed preparation.
        /// </summary>
        /// <param name="phase">The phase the failure occurred in.</param>
        /// <param name="reason">The human-readable reason.</param>
        /// <param name="eventKind">The event kind the failure should raise.</param>
        /// <returns>The preparation.</returns>
        public static XRegistryClosurePreparation Failed(
            XRegistryRefreshPhase phase,
            string reason,
            XRegistryRefreshEventKind eventKind = XRegistryRefreshEventKind.LoadFailure)
        {
            return new XRegistryClosurePreparation(false)
            {
                FailurePhase = phase,
                FailureReason = reason ?? string.Empty,
                FailureEventKind = eventKind
            };
        }

        /// <summary>
        /// Gets whether the closure can be committed.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the projection document, when one was produced.
        /// </summary>
        public XRegistryProjectionDocument? Document { get; private init; }

        /// <summary>
        /// Gets the phase a failed preparation reached.
        /// </summary>
        public XRegistryRefreshPhase FailurePhase { get; private init; }

        /// <summary>
        /// Gets the reason a preparation failed.
        /// </summary>
        public string FailureReason { get; private init; } = string.Empty;

        /// <summary>
        /// Gets the event kind a failed preparation raises.
        /// </summary>
        public XRegistryRefreshEventKind FailureEventKind { get; private init; }

        /// <summary>
        /// Gets or sets whether the closure projects in a degraded state.
        /// </summary>
        /// <remarks>
        /// A degraded closure still commits; its members report
        /// <see cref="XRegistryRefreshOutcome.Warning"/> instead of
        /// <see cref="XRegistryRefreshOutcome.Success"/>.
        /// </remarks>
        public bool Degraded { get; init; }

        /// <summary>
        /// Gets or sets the message describing why the closure is degraded.
        /// </summary>
        public string DegradedMessage { get; init; } = string.Empty;

        /// <summary>
        /// Gets or sets what each member materialized.
        /// </summary>
        public ArrayOf<XRegistryMemberProjection> MemberProjections { get; init; } = [];

        /// <summary>
        /// Gets or sets the members the strategy reports itself.
        /// </summary>
        /// <remarks>
        /// The engine produces no result for these, which lets a domain report a
        /// member that materializes through a path of its own rather than as part
        /// of the closure's NodeSet.
        /// </remarks>
        public ArrayOf<string> DeferredXids { get; init; } = [];
    }

    /// <summary>
    /// The context handed to a strategy while it prepares a closure.
    /// </summary>
    public sealed class XRegistryClosurePreparationContext
    {
        /// <summary>
        /// Initializes a preparation context.
        /// </summary>
        /// <param name="closure">The closure being prepared.</param>
        /// <param name="generation">The candidate generation.</param>
        /// <param name="dryRun">Whether the refresh commits anything.</param>
        /// <param name="raise">The event sink.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public XRegistryClosurePreparationContext(
            XRegistryDependencyClosure closure,
            uint generation,
            bool dryRun,
            Action<XRegistryRefreshEventArgs> raise)
        {
            Closure = closure ?? throw new ArgumentNullException(nameof(closure));
            Generation = generation;
            DryRun = dryRun;
            m_raise = raise ?? throw new ArgumentNullException(nameof(raise));
        }

        /// <summary>
        /// Gets the closure being prepared.
        /// </summary>
        public XRegistryDependencyClosure Closure { get; }

        /// <summary>
        /// Gets the candidate generation.
        /// </summary>
        public uint Generation { get; }

        /// <summary>
        /// Gets whether the refresh will commit anything.
        /// </summary>
        public bool DryRun { get; }

        /// <summary>
        /// Raises a refresh event.
        /// </summary>
        /// <remarks>
        /// A strategy uses this for the events only it can classify - a document
        /// that failed compatibility validation rather than loading, or a binding
        /// that degraded without failing the closure.
        /// </remarks>
        /// <param name="args">The event to raise.</param>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is <c>null</c>.</exception>
        public void Raise(XRegistryRefreshEventArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            m_raise(args);
        }

        private readonly Action<XRegistryRefreshEventArgs> m_raise;
    }

    /// <summary>
    /// The context handed to a strategy after a closure's projection committed.
    /// </summary>
    public sealed class XRegistryClosureCommitContext
    {
        /// <summary>
        /// Initializes a commit context.
        /// </summary>
        /// <param name="closure">The committed closure.</param>
        /// <param name="preparation">The preparation that produced it.</param>
        /// <param name="generation">The generation being committed.</param>
        /// <param name="raise">The event sink.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        public XRegistryClosureCommitContext(
            XRegistryDependencyClosure closure,
            XRegistryClosurePreparation preparation,
            uint generation,
            Action<XRegistryRefreshEventArgs> raise)
        {
            Closure = closure ?? throw new ArgumentNullException(nameof(closure));
            Preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
            Generation = generation;
            m_raise = raise ?? throw new ArgumentNullException(nameof(raise));
        }

        /// <summary>
        /// Gets the committed closure.
        /// </summary>
        public XRegistryDependencyClosure Closure { get; }

        /// <summary>
        /// Gets the preparation that produced the projection.
        /// </summary>
        public XRegistryClosurePreparation Preparation { get; }

        /// <summary>
        /// Gets the generation being committed.
        /// </summary>
        public uint Generation { get; }

        /// <summary>
        /// Gets the handle to the live generation, when one was created.
        /// </summary>
        public XRegistryProjectionHandle? Handle { get; init; }

        /// <summary>
        /// Gets the state tracked for the generation being replaced.
        /// </summary>
        public IXRegistryClosureState? PreviousState { get; init; }

        /// <summary>
        /// Raises a refresh event.
        /// </summary>
        /// <param name="args">The event to raise.</param>
        /// <exception cref="ArgumentNullException"><paramref name="args"/> is <c>null</c>.</exception>
        public void Raise(XRegistryRefreshEventArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            m_raise(args);
        }

        private readonly Action<XRegistryRefreshEventArgs> m_raise;
    }

    /// <summary>
    /// What a strategy produced while committing a closure.
    /// </summary>
    public sealed class XRegistryClosureCommitResult
    {
        /// <summary>
        /// Gets or sets the domain state to track with the new generation.
        /// </summary>
        public IXRegistryClosureState? State { get; init; }

        /// <summary>
        /// Gets or sets results the strategy produced itself.
        /// </summary>
        public ArrayOf<XRegistryRefreshItemResult> AdditionalResults { get; init; } = [];

        /// <summary>
        /// Gets or sets whether the commit degraded the closure.
        /// </summary>
        public bool Degraded { get; init; }

        /// <summary>
        /// Gets or sets an additional message for the committed members.
        /// </summary>
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Supplies the domain-specific half of a refresh to
    /// <see cref="XRegistryRefreshEngine"/>.
    /// </summary>
    /// <remarks>
    /// The engine owns everything that is the same for every companion
    /// specification - generations, unchanged detection, commit and rollback,
    /// retirement, aggregation and events - and calls out here for everything
    /// that is not.
    /// </remarks>
    public interface IXRegistryRefreshStrategy
    {
        /// <summary>
        /// Enumerates the dependency closures this refresh should consider.
        /// </summary>
        /// <param name="request">The refresh request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The closures, in a deterministic order.</returns>
        ValueTask<ArrayOf<XRegistryDependencyClosure>> EnumerateClosuresAsync(
            XRegistryRefreshRequest request,
            CancellationToken ct);

        /// <summary>
        /// Determines whether a selector selects a member.
        /// </summary>
        /// <param name="member">The candidate member.</param>
        /// <param name="selector">The selector to test.</param>
        /// <returns><c>true</c> when the selector selects the member.</returns>
        bool Matches(XRegistryRefreshMember member, XRegistryResourceSelector selector);

        /// <summary>
        /// Prepares a projectable closure for commit.
        /// </summary>
        /// <param name="context">The preparation context.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The preparation.</returns>
        ValueTask<XRegistryClosurePreparation> PrepareClosureAsync(
            XRegistryClosurePreparationContext context,
            CancellationToken ct);

        /// <summary>
        /// Runs the domain work that follows a successful projection commit.
        /// </summary>
        /// <param name="context">The commit context.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The commit result.</returns>
        ValueTask<XRegistryClosureCommitResult> CommitClosureAsync(
            XRegistryClosureCommitContext context,
            CancellationToken ct);

        /// <summary>
        /// Persists the outcome of a committed refresh.
        /// </summary>
        /// <param name="results">The per-resource results.</param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask ApplyResultsAsync(
            ArrayOf<XRegistryRefreshItemResult> results,
            CancellationToken ct);
    }
}
