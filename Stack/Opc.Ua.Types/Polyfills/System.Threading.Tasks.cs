/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace System.Threading.Tasks
{
    /// <summary>
    /// Polyfills for <see cref="Task"/> and <see cref="Task{TResult}"/>.
    /// </summary>
    public static class PolyFills
    {
#if !NET8_0_OR_GREATER
        // Copyright Stephen Cleary Nito.AsyncEx

        /// <summary>
        /// Asynchronously waits for the task to complete, or for the
        /// cancellation token to be canceled.
        /// </summary>
        /// <param name="task">The task to wait for.
        /// May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that
        /// cancels the wait.</param>
        /// <exception cref="ArgumentNullException"><paramref name="task"/> is <c>null</c>.</exception>
        public static Task WaitAsync(
            this Task task,
            CancellationToken cancellationToken)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return DoWaitAsync(task, cancellationToken);
        }

        private static async Task DoWaitAsync(
            Task task,
            CancellationToken cancellationToken)
        {
            var cancelTaskSource =
                new CancellationTokenTaskSource<object>(cancellationToken);
            await (await Task.WhenAny(task, cancelTaskSource.Task).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously waits for the task to complete, or for the
        /// cancellation token to be canceled.
        /// </summary>
        /// <typeparam name="TResult">The type of the task result.</typeparam>
        /// <param name="task">The task to wait for.
        /// May not be <c>null</c>.</param>
        /// <param name="cancellationToken">The cancellation token that
        /// cancels the wait.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> is <c>null</c>.
        /// </exception>
        public static Task<TResult> WaitAsync<TResult>(
            this Task<TResult> task,
            CancellationToken cancellationToken)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<TResult>(cancellationToken);
            }

            return DoWaitAsync(task, cancellationToken);
        }

        private static async Task<TResult> DoWaitAsync<TResult>(
            Task<TResult> task, CancellationToken cancellationToken)
        {
            using var cancelTaskSource =
                new CancellationTokenTaskSource<TResult>(cancellationToken);
            return await (await Task.WhenAny(task, cancelTaskSource.Task).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Holds the task for a cancellation token, as well as the token
        /// registration. The registration is disposed when this instance
        /// is disposed.
        /// </summary>
        /// <typeparam name="T">The type of the task source.</typeparam>
        internal sealed class CancellationTokenTaskSource<T> : IDisposable
        {
            /// <summary>
            /// Creates a task for the specified cancellation token,
            /// registering with the token if necessary.
            /// </summary>
            /// <param name="cancellationToken">The cancellation token
            /// to observe.</param>
            public CancellationTokenTaskSource(CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Task = Tasks.Task.FromCanceled<T>(cancellationToken);
                    return;
                }
                var tcs = new TaskCompletionSource<T>();
                m_registration = cancellationToken.Register(
                    () => tcs.TrySetCanceled(cancellationToken),
                    useSynchronizationContext: false);
                Task = tcs.Task;
            }

            /// <summary>
            /// Gets the task for the source cancellation token.
            /// </summary>
            public Task<T> Task { get; }

            /// <summary>
            /// Disposes the cancellation token registration, if any.
            /// Note that this may cause <see cref="Task"/> to never complete.
            /// </summary>
            public void Dispose()
            {
                m_registration.Dispose();
            }

            /// <summary>
            /// The cancellation token registration, if any.
            /// This is <c>null</c> if the registration was not necessary.
            /// </summary>
            private readonly CancellationTokenRegistration m_registration;
        }
#endif
    }
}
