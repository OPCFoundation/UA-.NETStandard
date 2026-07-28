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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Persists the immutable registry snapshot behind a <em>transactional
    /// commit</em> contract. A store owns exactly one committed generation and
    /// exposes it through <see cref="LoadAsync"/>; a mutation is made durable by
    /// committing a complete replacement generation through
    /// <see cref="CommitAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CommitAsync"/> stages the entire <see cref="WotRegistrySnapshot"/>
    /// durably before atomically switching the committed generation. Persistence
    /// failures whose outcome can be established are reported as
    /// <see cref="WotRegistryCommitNotCommittedException"/> or
    /// <see cref="WotRegistryCommitDurabilityUncertainException"/>. An
    /// <see cref="WotRegistryCommitIndeterminateException"/> means the store cannot
    /// establish which generation is active.
    /// </para>
    /// <para>
    /// The <see cref="WotRegistryService"/> publishes a validated committed snapshot
    /// even when its final durability is uncertain, leaves the previous snapshot
    /// published for a confirmed not-committed outcome, and blocks mutation after
    /// an indeterminate result until a successful reload.
    /// </para>
    /// </remarks>
    public interface IWotRegistryStore
    {
        /// <summary>
        /// Loads the last committed registry generation into an immutable
        /// snapshot. Returns <see cref="WotRegistrySnapshot.Empty"/> when no
        /// generation has ever been committed. Never observes a partially
        /// written (staged, not-yet-committed) generation.
        /// </summary>
        ValueTask<WotRegistrySnapshot> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Durably and atomically commits <paramref name="snapshot"/> as the new
        /// committed generation, replacing the previous one in full. Stages all
        /// backing state first and only then switches the committed generation.
        /// See <see cref="WotRegistryCommitNotCommittedException"/>,
        /// <see cref="WotRegistryCommitDurabilityUncertainException"/>, and
        /// <see cref="WotRegistryCommitIndeterminateException"/> for explicit
        /// persistence outcomes.
        /// </summary>
        /// <param name="snapshot">The complete registry generation to commit.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        ValueTask CommitAsync(
            WotRegistrySnapshot snapshot,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Indicates that the requested generation is the validated committed generation,
    /// but a persistence operation still reported a failure.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "This outcome is meaningful only with its committed snapshot.")]
    public sealed class WotRegistryCommitDurabilityUncertainException : IOException
    {
        /// <summary>
        /// Initializes a committed, durability-uncertain outcome that external
        /// <see cref="IWotRegistryStore"/> implementations can report.
        /// </summary>
        public WotRegistryCommitDurabilityUncertainException(
            WotRegistrySnapshot committedSnapshot,
            Exception persistenceFailure)
            : base(
                $"WoT registry generation " +
                $"{(committedSnapshot ?? throw new ArgumentNullException(
                    nameof(committedSnapshot))).Generation} was committed, " +
                "but its final durability is uncertain.",
                persistenceFailure ?? throw new ArgumentNullException(nameof(persistenceFailure)))
        {
            CommittedSnapshot = committedSnapshot;
            PersistenceFailure = persistenceFailure;
        }

        /// <summary>
        /// Gets the validated snapshot that is now the primary generation.
        /// </summary>
        public WotRegistrySnapshot CommittedSnapshot { get; }

        /// <summary>
        /// Gets the committed registry generation.
        /// </summary>
        public long CommittedGeneration => CommittedSnapshot.Generation;

        /// <summary>
        /// Gets the persistence failure reported after the generation became active.
        /// </summary>
        public Exception PersistenceFailure { get; }
    }

    /// <summary>
    /// Indicates that the requested generation was conclusively not committed. The
    /// previous generation remains active and the mutation may be retried.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "This outcome requires its intended snapshot and persistence failure.")]
    public sealed class WotRegistryCommitNotCommittedException : IOException
    {
        /// <summary>
        /// Initializes a confirmed not-committed outcome that external
        /// <see cref="IWotRegistryStore"/> implementations can report.
        /// </summary>
        public WotRegistryCommitNotCommittedException(
            WotRegistrySnapshot intendedSnapshot,
            Exception persistenceFailure,
            string? recoveryArtifactPath = null)
            : base(
                $"WoT registry generation " +
                $"{(intendedSnapshot ?? throw new ArgumentNullException(
                    nameof(intendedSnapshot))).Generation} was not committed; " +
                "the previous generation remains active.",
                persistenceFailure ?? throw new ArgumentNullException(nameof(persistenceFailure)))
        {
            IntendedSnapshot = intendedSnapshot;
            PersistenceFailure = persistenceFailure;
            RecoveryArtifactPath = recoveryArtifactPath;
        }

        /// <summary>
        /// Gets the snapshot that the failed operation intended to commit.
        /// </summary>
        public WotRegistrySnapshot IntendedSnapshot { get; }

        /// <summary>
        /// Gets the intended registry generation.
        /// </summary>
        public long IntendedGeneration => IntendedSnapshot.Generation;

        /// <summary>
        /// Gets the persistence failure that prevented the commit.
        /// </summary>
        public Exception PersistenceFailure { get; }

        /// <summary>
        /// Gets an optional preserved recovery artifact containing the intended
        /// manifest.
        /// </summary>
        public string? RecoveryArtifactPath { get; }
    }

    /// <summary>
    /// Indicates that a manifest switch may have occurred, but the resulting primary
    /// generation could not be validated. Reload or operator recovery is required.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "This outcome is meaningful only with its intended snapshot and failures.")]
    public sealed class WotRegistryCommitIndeterminateException : IOException
    {
        /// <summary>
        /// Initializes an indeterminate outcome that external
        /// <see cref="IWotRegistryStore"/> implementations can report.
        /// </summary>
        public WotRegistryCommitIndeterminateException(
            WotRegistrySnapshot intendedSnapshot,
            Exception persistenceFailure,
            Exception validationFailure)
            : base(
                $"The WoT registry commit outcome for generation " +
                $"{(intendedSnapshot ?? throw new ArgumentNullException(
                    nameof(intendedSnapshot))).Generation} is indeterminate; reload or " +
                "operator recovery is required.",
                new AggregateException(
                    persistenceFailure ??
                        throw new ArgumentNullException(nameof(persistenceFailure)),
                    validationFailure ??
                        throw new ArgumentNullException(nameof(validationFailure))))
        {
            IntendedSnapshot = intendedSnapshot;
            PersistenceFailure = persistenceFailure;
            ValidationFailure = validationFailure;
        }

        /// <summary>
        /// Gets the snapshot that the failed operation intended to commit.
        /// </summary>
        public WotRegistrySnapshot IntendedSnapshot { get; }

        /// <summary>
        /// Gets the intended registry generation.
        /// </summary>
        public long IntendedGeneration => IntendedSnapshot.Generation;

        /// <summary>
        /// Gets the persistence failure whose outcome could not be established.
        /// </summary>
        public Exception PersistenceFailure { get; }

        /// <summary>
        /// Gets the failure encountered while validating the actual primary.
        /// </summary>
        public Exception ValidationFailure { get; }
    }
}
