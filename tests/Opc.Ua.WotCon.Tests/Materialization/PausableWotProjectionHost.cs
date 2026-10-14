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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Wraps an invocation projection host and lets tests block activation of prepared publications.
    /// </summary>
    /// <param name="inner">The WoT invocation projection host.</param>
    internal sealed class PausableWotProjectionHost(IWotInvocationProjectionHost inner) : IWotInvocationProjectionHost
    {
        /// <summary>
        /// Gets the supports prepared publication for <c>PausableWotProjectionHost</c>.
        /// </summary>
        public bool SupportsPreparedPublication => inner.SupportsPreparedPublication;

        /// <summary>
        /// Gets the supported atomicities for <c>PausableWotProjectionHost</c>.
        /// </summary>
        public ArrayOf<WoTAtomicityEnum> SupportedAtomicities => inner.SupportedAtomicities;

        /// <summary>
        /// Gets the documents for <c>PausableWotProjectionHost</c>.
        /// </summary>
        public ConcurrentQueue<WotProjectionDocument> Documents { get; } = new();

        public IWotProjectionPublicationCapture CapturePublication()
        {
            return new Capture(inner.CapturePublication(), this);
        }

        /// <inheritdoc/>
        /// <param name="views">The WoT prepared view publication.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
            ArrayOf<WotProjectionChange> changes,
            IWotPreparedViewPublication? views,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            RecordNewDocuments(changes);
            await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
            return await inner.PrepareAsync(changes, views, callerContext, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<WotProjectionHandle> AddAsync(
            WotProjectionDocument document, CancellationToken cancellationToken = default)
        {
            Documents.Enqueue(document);
            await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
            return await inner.AddAsync(document, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="current">The WoT projection handle.</param>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<WotProjectionHandle> ShadowReloadAsync(
            WotProjectionHandle current, WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
            return await inner.ShadowReloadAsync(current, document, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="current">The WoT projection handle.</param>
        /// <param name="document">The WoT projection document.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<WotProjectionHandle> ImmediateReloadAsync(
            WotProjectionHandle current, WotProjectionDocument document,
            CancellationToken cancellationToken = default)
        {
            await WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
            return await inner.ImmediateReloadAsync(current, document, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        /// <param name="handle">The WoT projection handle.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
        {
            return inner.RemoveAsync(handle, cancellationToken);
        }

        public void BlockNextActivation()
        {
            lock (m_gate)
            {
                m_entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task WaitUntilBlockedAsync(Task activation)
        {
            Task entered;
            lock (m_gate)
            {
                entered = m_entered?.Task ?? throw new InvalidOperationException("No activation is blocked.");
            }
            bool blocked = false;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                Task completed = await Task.WhenAny(entered, activation).WaitAsync(deadline.Token).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                if (!ReferenceEquals(completed, entered))
                {
                    throw new InvalidOperationException("The activation completed without reaching its barrier.");
                }
                blocked = true;
            }
            finally
            {
                if (!blocked)
                {
                    ReleaseActivation();
                }
            }
        }

        public void ReleaseActivation()
        {
            lock (m_gate)
            {
                m_release?.TrySetResult(true);
            }
        }

        private void RecordNewDocuments(ArrayOf<WotProjectionChange> changes)
        {
            foreach (WotProjectionChange change in changes)
            {
                if (change.Current is null && change.Document is { } document)
                {
                    Documents.Enqueue(document);
                }
            }
        }

        private async ValueTask WaitIfBlockedAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool>? entered;
            TaskCompletionSource<bool>? release;
            lock (m_gate)
            {
                entered = m_entered;
                release = m_release;
            }
            if (entered is null || release is null)
            {
                return;
            }
            entered.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                if (ReferenceEquals(m_release, release))
                {
                    m_entered = null;
                    m_release = null;
                }
            }
        }

        private sealed class Capture(
            IWotProjectionPublicationCapture inner, PausableWotProjectionHost owner) : IWotProjectionPublicationCapture
        {
            /// <inheritdoc/>
            /// <param name="callerContext">The executing request's context, if any.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotProjectionPublication> BeginAsync(
                IOperationContext? callerContext,
                CancellationToken cancellationToken)
            {
                return new Invocation(
                    await inner.BeginAsync(callerContext, cancellationToken).ConfigureAwait(false), owner);
            }
        }

        private sealed class Invocation(IWotProjectionPublication inner, PausableWotProjectionHost owner)
            : IWotProjectionValidationPublication
        {
            /// <summary>
            /// Gets the is current for <c>Invocation</c>.
            /// </summary>
            public bool IsCurrent => inner.IsCurrent;

            /// <inheritdoc/>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask<IWotPreparedProjectionPublication> PrepareAsync(
                ArrayOf<WotProjectionChange> changes,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                owner.RecordNewDocuments(changes);
                await owner.WaitIfBlockedAsync(cancellationToken).ConfigureAwait(false);
                return await inner.PrepareAsync(changes, views, cancellationToken).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            /// <param name="inspectAsync">The inspect async.</param>
            /// <param name="views">The WoT prepared view publication.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask ValidateAsync(
                ArrayOf<WotProjectionChange> changes,
                Func<IWotPreparedProjectionPublication, CancellationToken, ValueTask> inspectAsync,
                IWotPreparedViewPublication? views = null,
                CancellationToken cancellationToken = default)
            {
                if (inner is not IWotProjectionValidationPublication validation)
                {
                    throw new NotSupportedException("The wrapped owner cannot validate private publication.");
                }
                return validation.ValidateAsync(changes, inspectAsync, views, cancellationToken);
            }

            /// <inheritdoc/>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IWotPreparedProjectionPublication> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
            {
                return inner.PrepareReadImagesAsync(images, cancellationToken);
            }

            public ValueTask DisposeAsync()
            {
                return inner.DisposeAsync();
            }
        }

        private readonly Lock m_gate = new();
        private TaskCompletionSource<bool>? m_entered;
        private TaskCompletionSource<bool>? m_release;
    }
}
