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

namespace Opc.Ua
{
    /// <summary>
    /// <para>
    /// Registry of encodeable type activators that can be retrieved
    /// using the type id or encoding ids in encoders and decoders.
    /// Used to register source generated and custom (hand crafted)
    /// types.
    /// <br/></para>
    /// <para>
    /// You can manually add types using the <see cref="Builder"/>
    /// property exposed mutator. You can also import all types from
    /// a specified assembly when you are not required to be trimmable
    /// or NativeAOT compliant.
    /// <br/></para>
    /// <para>
    /// Once the types exist within the registry they can be consumed
    /// by encoders and decoders.
    /// <br/></para>
    /// </summary>
    public sealed partial class EncodeableFactory
    {
        private EncodeableFactory? CurrentView
        {
            get
            {
                EncodeableFactory? view = Volatile.Read(ref m_viewSelector)?.Invoke();
                return ReferenceEquals(view, this) ? null : view;
            }
        }

        /// <summary>
        /// Acquires publication ownership and selects the serving registration image.
        /// </summary>
        /// <param name="selector">The current image selector.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal ViewOwner SetViewSelector(Func<EncodeableFactory?> selector)
        {
            return TryAcquireViewOwner(selector) ??
                throw new InvalidOperationException("The encodeable factory already has a publication owner.");
        }

        /// <summary>
        /// Atomically acquires publication ownership when no owner exists.
        /// </summary>
        /// <param name="selector">The current image selector.</param>
        /// <returns>The owner token, or <c>null</c> when another owner holds the factory.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal ViewOwner? TryAcquireViewOwner(Func<EncodeableFactory?> selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            lock (m_publicationGate)
            {
                if (m_viewSelector is not null)
                {
                    return null;
                }
                Volatile.Write(ref m_viewSelector, selector);
                return new ViewOwner(this, selector);
            }
        }

        /// <summary>
        /// Gets whether a publication owner currently selects the serving image.
        /// </summary>
        internal bool HasViewOwner => Volatile.Read(ref m_viewSelector) is not null;

        /// <summary>
        /// Captures independent registration maps with their source identity and revision.
        /// </summary>
        /// <param name="source">The encodeable factory.</param>
        /// <param name="revision">The captured source revision.</param>
        internal EncodeableFactory CaptureSnapshot(out EncodeableFactory source, out long revision)
        {
            lock (m_publicationGate)
            {
                if (CurrentView is { } view)
                {
                    return view.CaptureSnapshot(out source, out revision);
                }
                source = this;
                revision = m_revision;
                return new EncodeableFactory(this);
            }
        }

        /// <summary>
        /// Reserves an unchanged source image until publication completes or is abandoned.
        /// </summary>
        /// <param name="source">The encodeable factory.</param>
        /// <param name="revision">The revision observed during capture.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal Publication BeginPublication(EncodeableFactory source, long revision)
        {
            lock (m_publicationGate)
            {
                if (CurrentView is { } view)
                {
                    return view.BeginPublication(source, revision);
                }
                if (!ReferenceEquals(source, this) || revision != m_revision)
                {
                    throw new InvalidOperationException("Serving factory registrations changed after preparation.");
                }
                EnsureMutable();
                m_publicationPending = true;
                return new Publication(this);
            }
        }

        private void EnsureMutable()
        {
            if (m_publicationPending || m_retired)
            {
                throw new InvalidOperationException(
                    "The factory image belongs to a pending or completed publication and cannot be changed.");
            }
        }

        private readonly Lock m_publicationGate = new();
        private Func<EncodeableFactory?>? m_viewSelector;
        private long m_revision;
        private bool m_publicationPending;
        private bool m_retired;

        /// <summary>
        /// Retains exclusive ownership of the factory's serving-image selector.
        /// </summary>
        /// <param name="owner">The encodeable factory.</param>
        /// <param name="selector">The owned image selector.</param>
        internal sealed class ViewOwner(EncodeableFactory owner, Func<EncodeableFactory?> selector)
        {
            /// <summary>
            /// Releases ownership after transferring the selected registrations back to the factory.
            /// </summary>
            /// <param name="image">The encodeable factory.</param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Release(EncodeableFactory image)
            {
                EncodeableFactory? current = Volatile.Read(ref m_owner);
                if (current is null)
                {
                    return;
                }
                lock (current.m_publicationGate)
                {
                    lock (image.m_publicationGate)
                    {
                        if (m_owner is null)
                        {
                            return;
                        }
                        if (!ReferenceEquals(current.m_viewSelector, selector))
                        {
                            throw new InvalidOperationException("The factory publication owner changed.");
                        }
                        if (!ReferenceEquals(image, current))
                        {
                            image.EnsureMutable();
                            current.m_encodeableTypes = image.m_encodeableTypes;
                            current.m_enumeratedTypes = image.m_enumeratedTypes;
                            current.m_xmlNameToType = image.m_xmlNameToType;
                            current.m_revision++;
                            image.m_retired = true;
                        }
                        Volatile.Write(ref current.m_viewSelector, null);
                        Volatile.Write(ref m_owner, null);
                    }
                }
            }

            private EncodeableFactory? m_owner = owner;
        }

        /// <summary>
        /// Reserves factory mutations and retires the source after successful publication.
        /// </summary>
        /// <param name="owner">The encodeable factory.</param>
        internal sealed class Publication(EncodeableFactory owner) : IDisposable
        {
            /// <summary>
            /// Permanently retires the reserved source image.
            /// </summary>
            /// <exception cref="ObjectDisposedException"></exception>
            public void Complete()
            {
                EncodeableFactory current = m_owner ?? throw new ObjectDisposedException(nameof(Publication));
                lock (current.m_publicationGate)
                {
                    current.m_retired = true;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                EncodeableFactory? current = Interlocked.Exchange(ref m_owner, null);
                if (current is not null)
                {
                    lock (current.m_publicationGate)
                    {
                        current.m_publicationPending = false;
                    }
                }
            }

            private EncodeableFactory? m_owner = owner;
        }
    }
}
