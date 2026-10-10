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
    public sealed partial class EncodeableFactory
    {
        private EncodeableFactory? CurrentView
        {
            get
            {
                EncodeableFactory? view = m_viewSelector?.Invoke();
                return ReferenceEquals(view, this) ? null : view;
            }
        }

        internal ViewOwner SetViewSelector(Func<EncodeableFactory?> selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            if (Interlocked.CompareExchange(ref m_viewSelector, selector, null) is not null)
            {
                throw new InvalidOperationException("The encodeable factory already has a publication owner.");
            }
            return new ViewOwner(this, selector);
        }

        internal EncodeableFactory CaptureSnapshot(out EncodeableFactory source, out long revision)
        {
            if (CurrentView is { } view)
            {
                return view.CaptureSnapshot(out source, out revision);
            }
            lock (m_publicationGate)
            {
                source = this;
                revision = m_revision;
                return new EncodeableFactory(this);
            }
        }

        internal Publication BeginPublication(EncodeableFactory source, long revision)
        {
            if (CurrentView is { } view)
            {
                return view.BeginPublication(source, revision);
            }
            lock (m_publicationGate)
            {
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

        internal sealed class ViewOwner(EncodeableFactory owner, Func<EncodeableFactory?> selector)
        {
            public void Release(EncodeableFactory image)
            {
                EncodeableFactory? current = Volatile.Read(ref m_owner);
                if (current is null)
                {
                    return;
                }
                lock (image.m_publicationGate)
                {
                    lock (current.m_publicationGate)
                    {
                        if (m_owner is null)
                        {
                            return;
                        }
                        if (!ReferenceEquals(current.m_viewSelector, selector))
                        {
                            throw new InvalidOperationException("The factory publication owner changed.");
                        }
                        image.EnsureMutable();
                        current.m_encodeableTypes = image.m_encodeableTypes;
                        current.m_enumeratedTypes = image.m_enumeratedTypes;
                        current.m_xmlNameToType = image.m_xmlNameToType;
                        current.m_revision++;
                        image.m_retired = true;
                        Volatile.Write(ref current.m_viewSelector, null);
                        Volatile.Write(ref m_owner, null);
                    }
                }
            }

            private EncodeableFactory? m_owner = owner;
        }

        internal sealed class Publication(EncodeableFactory owner) : IDisposable
        {
            public void Complete()
            {
                EncodeableFactory current = m_owner ?? throw new ObjectDisposedException(nameof(Publication));
                lock (current.m_publicationGate)
                {
                    current.m_retired = true;
                }
            }

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
