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

using System;
using System.Collections;
using System.Collections.Generic;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// A read-only snapshot of <see cref="CertificateEntry"/> objects that
    /// owns its elements and implements <see cref="IDisposable"/>.
    /// </summary>
    /// <remarks>
    /// Each entry is added as an independent owning handle (via
    /// <see cref="CertificateEntry.AddRef"/>), so the source entries are not
    /// transferred and the collection disposes its own handles when it is
    /// disposed. The collection is intended to be consumed with a
    /// <c>using</c> pattern: when it is disposed, every contained
    /// <see cref="CertificateEntry"/> — and therefore the reference it holds
    /// on the underlying certificate cores — is released.
    /// </remarks>
    public sealed class CertificateEntryCollection
        : IReadOnlyList<CertificateEntry>, IDisposable
    {
        private readonly List<CertificateEntry> m_entries;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="CertificateEntryCollection"/> that
        /// takes an independent owning handle over each supplied entry.
        /// </summary>
        /// <param name="entries">
        /// The entries to snapshot. Each is added via
        /// <see cref="CertificateEntry.AddRef"/>; the caller retains ownership
        /// of the supplied instances.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="entries"/> is <c>null</c>.
        /// </exception>
        public CertificateEntryCollection(IEnumerable<CertificateEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            m_entries = [];
            foreach (CertificateEntry entry in entries)
            {
                m_entries.Add(entry.AddRef());
            }
        }

        /// <inheritdoc/>
        public int Count => m_entries.Count;

        /// <inheritdoc/>
        public CertificateEntry this[int index] => m_entries[index];

        /// <inheritdoc/>
        public IEnumerator<CertificateEntry> GetEnumerator()
        {
            return m_entries.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Releases the owning handle on every contained
        /// <see cref="CertificateEntry"/> and clears the collection.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            foreach (CertificateEntry entry in m_entries)
            {
                entry.Dispose();
            }
            m_entries.Clear();
        }
    }
}
