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

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One private source-projection addition, replacement or retirement.
    /// </summary>
    public sealed class WotProjectionChange
    {
        private WotProjectionChange(
            WotProjectionHandle? current,
            WotProjectionDocument? document,
            WotProjectionRetirementPolicy retirementPolicy)
        {
            Current = current;
            Document = document;
            RetirementPolicy = retirementPolicy;
        }

        /// <summary>
        /// Gets the exact current owner, or null for an addition.
        /// </summary>
        public WotProjectionHandle? Current { get; }

        /// <summary>
        /// Gets the desired document, or null for retirement.
        /// </summary>
        public WotProjectionDocument? Document { get; }

        /// <summary>
        /// Gets the previous owner's post-publication retirement policy.
        /// </summary>
        public WotProjectionRetirementPolicy RetirementPolicy { get; }

        /// <summary>
        /// Adds a new private source owner.
        /// </summary>
        public static WotProjectionChange Add(WotProjectionDocument document)
        {
            return new WotProjectionChange(
                null, document ?? throw new ArgumentNullException(nameof(document)),
                WotProjectionRetirementPolicy.Graceful);
        }

        /// <summary>
        /// Replaces an exact source owner.
        /// </summary>
        public static WotProjectionChange Replace(
            WotProjectionHandle current,
            WotProjectionDocument document,
            WotProjectionRetirementPolicy retirementPolicy = WotProjectionRetirementPolicy.Graceful)
        {
            return new WotProjectionChange(
                current ?? throw new ArgumentNullException(nameof(current)),
                document ?? throw new ArgumentNullException(nameof(document)), retirementPolicy);
        }

        /// <summary>
        /// Retires an exact source owner in the aggregate publication.
        /// </summary>
        public static WotProjectionChange Remove(
            WotProjectionHandle current,
            WotProjectionRetirementPolicy retirementPolicy = WotProjectionRetirementPolicy.Graceful)
        {
            return new WotProjectionChange(
                current ?? throw new ArgumentNullException(nameof(current)), null, retirementPolicy);
        }
    }

    /// <summary>
    /// Optional source host support for a single privately prepared lifecycle publication.
    /// </summary>
    public interface IWotPreparedProjectionHost : IWotProjectionHost
    {
        /// <summary>
        /// Gets whether this owner can publish one aggregate source/View routing image.
        /// </summary>
        bool SupportsPreparedPublication { get; }

        /// <summary>
        /// Privately prepares source changes together with a graph-wide View participant.
        /// The View participant remains caller-owned and contributes no independent decision.
        /// </summary>
        ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The private source/View routing candidate for one coordinator-owned publication decision.
    /// </summary>
    public interface IWotPreparedProjectionPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets prepared source handles in addition/replacement order, excluding removals.
        /// </summary>
        ArrayOf<WotProjectionHandle> Projections { get; }

        /// <summary>
        /// Gets the prepared complete View graph, or null when this unit changes no Views.
        /// </summary>
        WotPreparedViewGraphState? ViewGraph { get; }

        /// <summary>
        /// Gets whether the routing publication has committed.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Gets a post-publication readiness/retirement reconciliation failure.
        /// </summary>
        Exception? CleanupFailure { get; }

        /// <summary>
        /// Executes the coordinator's durable decision, publishes one routing image and its prepared
        /// in-memory state, then performs post-commit reconciliation without rolling the unit back.
        /// </summary>
        ValueTask CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);
    }
}
