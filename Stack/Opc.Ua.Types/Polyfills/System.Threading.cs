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

#if !NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace System.Threading
{
#if !NET9_0_OR_GREATER
    /// <summary>
    /// A backport of .NET 9.0+'s System.Threading.Lock.
    /// </summary>
    public sealed class Lock
    {
        /// <summary>
        /// Determines whether the current thread holds this lock.
        /// </summary>
        /// <returns>
        /// true if the current thread holds this lock; otherwise, false.
        /// </returns>
#pragma warning disable CS9216
        public bool IsHeldByCurrentThread => Monitor.IsEntered(this);
#pragma warning restore CS9216

        /// <summary>
        /// <inheritdoc cref="Monitor.Enter(object)"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enter()
        {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
            Monitor.Enter(this);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
        }

        /// <summary>
        /// <inheritdoc cref="Monitor.TryEnter(object)"/>
        /// </summary>
        /// <returns>
        /// <inheritdoc cref="Monitor.TryEnter(object)"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter()
        {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
            return Monitor.TryEnter(this);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter(TimeSpan timeout)
        {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
            return Monitor.TryEnter(this, timeout);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter(int millisecondsTimeout)
        {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
            return Monitor.TryEnter(this, millisecondsTimeout);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Exit()
        {
#pragma warning disable CS9216
            Monitor.Exit(this);
#pragma warning restore CS9216
        }

        /// <summary>
        /// Enters the lock and returns a <see cref="Scope"/> that may be disposed to exit the lock.
        /// Once the method returns, the calling thread would be the only thread that holds the lock.
        /// </summary>
        /// <returns>
        /// A <see cref="Scope"/> that may be disposed to exit the lock.
        /// </returns>
        /// <remarks>
        /// If the lock cannot be entered immediately, the calling thread waits for the lock to be
        /// exited. If the lock is already held by the calling thread, the lock is entered again.
        /// The calling thread should exit the lock, such as by disposing the returned
        /// <see cref="Scope"/>, as many times as it had entered the lock to fully exit the lock and
        /// allow other threads to enter the lock.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if !NET5_0_OR_GREATER
        [Obsolete("This method is a best-effort at hardening against thread aborts, " +
            "but can theoretically retain lock on pre-.NET 5.0. Use with caution.")]
        public Scope EnterScope()
        {
            bool lockTaken = false;
            try
            {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
                Monitor.Enter(this, ref lockTaken);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
                return new Scope(this);
            }
            catch (ThreadAbortException)
            {
                if (lockTaken)
                {
#pragma warning disable CS9216
                    Monitor.Exit(this);
#pragma warning restore CS9216
                }

                throw;
            }
        }
#else
        public Scope EnterScope()
        {
#pragma warning disable CS9216, CA2002 // Do not lock on objects with weak identity
            Monitor.Enter(this);
#pragma warning restore CA2002 // Do not lock on objects with weak identity
#pragma warning restore CS9216
            return new Scope(this);
        }
#endif

        /// <summary>
        /// A disposable structure that is returned by <see cref="EnterScope()"/>,
        /// which when disposed, exits the lock.
        /// </summary>
        public readonly ref struct Scope(Lock @lock)
        {
            /// <summary>
            /// Exits the lock.
            /// </summary>
            /// <remarks>
            /// If the calling thread holds the lock multiple times, such as recursively,
            /// the lock is exited only once. The calling thread should ensure that each
            /// enter is matched with an exit.
            /// </remarks>
            /// <exception cref="SynchronizationLockException">
            /// The calling thread does not hold the lock.
            /// </exception>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                @lock.Exit();
            }
        }
    }
#endif
}
