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

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// A collection of <see cref="Certificate"/> objects that owns
    /// its elements and implements <see cref="IDisposable"/>. The collection
    /// manages the lifecycle of the certificates it contains, disposing them
    /// when they are removed from the collection or when the collection
    /// itself is disposed. When a certificate is added to the collection,
    /// the collection takes ownership by incrementing the reference count
    /// of the certificate. It is therefore simple to use the collection using
    /// a using pattern, and to share certificates between collections by
    /// adding the same certificate instance to multiple collections.
    /// </summary>
    public class CertificateCollection : IList<Certificate>, IDisposable
    {
        /// <summary>
        /// Initializes a new empty <see cref="CertificateCollection"/>.
        /// </summary>
        public CertificateCollection()
        {
            m_certificates = [];
        }

        /// <summary>
        /// Initializes a new <see cref="CertificateCollection"/>
        /// with the specified initial capacity.
        /// </summary>
        /// <param name="capacity">
        /// The number of elements the collection can initially store.
        /// </param>
        public CertificateCollection(int capacity)
        {
            m_certificates = new List<Certificate>(capacity);
        }

        /// <summary>
        /// Initializes a new <see cref="CertificateCollection"/>
        /// populated with references to the certificates in the
        /// specified enumerable. Does not copy the
        /// <see cref="Certificate"/> objects.
        /// </summary>
        /// <param name="certificates">
        /// The certificates to add to this collection.
        /// </param>
        public CertificateCollection(IEnumerable<Certificate> certificates)
        {
            if (certificates == null)
            {
                throw new ArgumentNullException(nameof(certificates));
            }

            m_certificates = [];
            foreach (Certificate cert in certificates)
            {
                Add(cert);
            }
        }

        /// <summary>
        /// Creates a <see cref="CertificateCollection"/> that takes
        /// ownership of all certificates in the supplied
        /// <see cref="X509Certificate2Collection"/>. After this call the
        /// passed collection should be considered consumed — the caller
        /// must not dispose the individual certificates.
        /// </summary>
        /// <param name="collection">
        /// The X.509 certificate collection to consume.
        /// </param>
        /// <returns>
        /// A new <see cref="CertificateCollection"/> owning the
        /// certificates.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="collection"/> is <c>null</c>.</exception>
        public static CertificateCollection From(
            X509Certificate2Collection collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }

            var result = new CertificateCollection(collection.Count);

            foreach (X509Certificate2 cert in collection)
            {
                result.m_certificates.Add(Certificate.From(cert));
            }

            return result;
        }

        /// <summary>
        /// Creates a new <see cref="X509Certificate2Collection"/>
        /// containing copies of each certificate in this collection.
        /// The caller owns the returned collection and is responsible
        /// for disposing its contents.
        /// </summary>
        /// <returns>
        /// A new <see cref="X509Certificate2Collection"/> with
        /// copied certificates.
        /// </returns>
        public X509Certificate2Collection AsX509Certificate2Collection()
        {
            ThrowIfDisposed();

            var collection = new X509Certificate2Collection();

            foreach (Certificate cert in m_certificates)
            {
                collection.Add(cert.AsX509Certificate2());
            }

            return collection;
        }

        /// <summary>
        /// Searches the collection using the specified find type and
        /// value, optionally filtering to only valid certificates.
        /// </summary>
        /// <param name="findType">
        /// The type of search to perform.
        /// </param>
        /// <param name="findValue">
        /// The value to search for.
        /// </param>
        /// <param name="validOnly">
        /// <c>true</c> to return only valid certificates;
        /// <c>false</c> to return all matches.
        /// </param>
        /// <returns>
        /// A new <see cref="CertificateCollection"/> containing the
        /// matching certificates. The returned collection holds
        /// references to the same <see cref="Certificate"/> objects —
        /// it does not take ownership.
        /// </returns>
        public CertificateCollection Find(
            X509FindType findType,
            object findValue,
            bool validOnly)
        {
            ThrowIfDisposed();

            switch (findType)
            {
                case X509FindType.FindByThumbprint:
                    return FindByMatch(
                        c => string.Equals(
                            c.Thumbprint,
                            findValue?.ToString(),
                            StringComparison.OrdinalIgnoreCase),
                        validOnly);
                case X509FindType.FindBySubjectDistinguishedName:
                    return FindByMatch(
                        c => string.Equals(
                            c.Subject,
                            findValue?.ToString(),
                            StringComparison.OrdinalIgnoreCase),
                        validOnly);
                case X509FindType.FindBySerialNumber:
                    return FindByMatch(
                        c => string.Equals(
                            c.SerialNumber,
                            findValue?.ToString(),
                            StringComparison.OrdinalIgnoreCase),
                        validOnly);
                default:
                    return FindByX509Collection(
                        findType, findValue, validOnly);
            }
        }

        /// <summary>
        /// Gets the number of certificates in the collection.
        /// </summary>
        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return m_certificates.Count;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the collection is
        /// read-only. Always returns <c>false</c>.
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets or sets the certificate at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index of the certificate.
        /// </param>
        /// <returns>The certificate at the specified index.</returns>
        public Certificate this[int index]
        {
            get
            {
                ThrowIfDisposed();
                return m_certificates[index];
            }

            set
            {
                ThrowIfDisposed();
                m_certificates[index] = value;
            }
        }

        /// <summary>
        /// Adds a certificate to the end of the collection.
        /// </summary>
        /// <param name="item">The certificate to add.</param>
        public void Add(Certificate item)
        {
            ThrowIfDisposed();
            m_certificates.Add(item.AddRef());
        }

        /// <summary>
        /// Inserts a certificate at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index at which the certificate should be
        /// inserted.
        /// </param>
        /// <param name="item">The certificate to insert.</param>
        public void Insert(int index, Certificate item)
        {
            ThrowIfDisposed();
            m_certificates.Insert(index, item.AddRef());
        }

        /// <summary>
        /// Removes the first occurrence of the specified certificate.
        /// </summary>
        /// <param name="item">The certificate to remove.</param>
        /// <returns>
        /// <c>true</c> if the certificate was found and removed;
        /// otherwise <c>false</c>.
        /// </returns>
        public bool Remove(Certificate item)
        {
            ThrowIfDisposed();
            if (m_certificates.Remove(item))
            {
                item.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the certificate at the specified index.
        /// </summary>
        /// <param name="index">
        /// The zero-based index of the certificate to remove.
        /// </param>
        public void RemoveAt(int index)
        {
            ThrowIfDisposed();
            Certificate cert = m_certificates[index];
            m_certificates.RemoveAt(index);
            cert.Dispose();
        }

        /// <summary>
        /// Determines whether the collection contains the specified
        /// certificate.
        /// </summary>
        /// <param name="item">The certificate to locate.</param>
        /// <returns>
        /// <c>true</c> if the certificate is found; otherwise
        /// <c>false</c>.
        /// </returns>
        public bool Contains(Certificate item)
        {
            ThrowIfDisposed();
            return m_certificates.Contains(item);
        }

        /// <summary>
        /// Returns the zero-based index of the first occurrence of the
        /// specified certificate, or -1 if not found.
        /// </summary>
        /// <param name="item">The certificate to locate.</param>
        /// <returns>
        /// The index of the certificate, or -1 if not found.
        /// </returns>
        public int IndexOf(Certificate item)
        {
            ThrowIfDisposed();
            return m_certificates.IndexOf(item);
        }

        /// <summary>
        /// Removes all certificates from the collection without
        /// disposing them.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            Certificate[] certificates = [.. m_certificates];
            m_certificates.Clear();
            foreach (Certificate cert in certificates)
            {
                cert.Dispose();
            }
        }

        /// <summary>
        /// Copies the elements of the collection to a Certificate array,
        /// starting at the specified index in the array.
        /// </summary>
        /// <param name="array">
        /// The destination array. References are copied as-is; the
        /// reference count is not incremented. The caller is responsible
        /// for ensuring the source collection (which owns the refs)
        /// remains alive while the array is in use.
        /// </param>
        /// <param name="arrayIndex">
        /// The zero-based index in the destination array at which
        /// copying begins.
        /// </param>
        public void CopyTo(Certificate[] array, int arrayIndex)
        {
            ThrowIfDisposed();
            m_certificates.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// An enumerator for the collection.
        /// </returns>
        public IEnumerator<Certificate> GetEnumerator()
        {
            ThrowIfDisposed();
            return m_certificates.GetEnumerator();
        }

        /// <summary>
        /// Returns a non-generic enumerator that iterates through the
        /// collection.
        /// </summary>
        /// <returns>
        /// A non-generic enumerator for the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Increments the reference count of every certificate in this
        /// collection. Call when transferring shared ownership to another
        /// lifecycle owner. Each owner independently calls
        /// <see cref="Dispose()"/>.
        /// </summary>
        /// <returns>This collection, for fluent usage.</returns>
        public CertificateCollection AddRef()
        {
            ThrowIfDisposed();
            var copy = new CertificateCollection(Count);
            foreach (Certificate cert in m_certificates)
            {
                copy.Add(cert);
            }
            return copy;
        }

        /// <summary>
        /// Releases the resources used by the collection and
        /// clears the collection.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources used by the collection.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release managed resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    foreach (Certificate cert in m_certificates)
                    {
                        cert?.Dispose();
                    }

                    m_certificates.Clear();
                }

                m_disposed = true;
            }
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if the
        /// collection has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(m_disposed, this);
#else
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(CertificateCollection));
            }
#endif
        }

        /// <summary>
        /// Filters the collection using a predicate and optional
        /// validity check.
        /// </summary>
        private CertificateCollection FindByMatch(
            Func<Certificate, bool> predicate,
            bool validOnly)
        {
            var result = new CertificateCollection();
            DateTime now = DateTime.UtcNow;

            foreach (Certificate cert in m_certificates)
            {
                if (!predicate(cert))
                {
                    continue;
                }

                if (validOnly &&
                    (now < cert.NotBefore || now > cert.NotAfter))
                {
                    continue;
                }

                result.Add(cert);
            }

            return result;
        }

        /// <summary>
        /// Falls back to using <see cref="X509Certificate2Collection"/>
        /// for unsupported find types.
        /// </summary>
        private CertificateCollection FindByX509Collection(
            X509FindType findType,
            object findValue,
            bool validOnly)
        {
            X509Certificate2Collection temp =
                AsX509Certificate2Collection();
            X509Certificate2Collection found =
                temp.Find(findType, findValue, validOnly);

            // Build a set of matching thumbprints from
            // the X509 find results.
            var thumbprints = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (X509Certificate2 cert in found)
            {
                thumbprints.Add(cert.Thumbprint);
            }

            // Return references to the original Certificate
            // objects that match.
            var result = new CertificateCollection();

            foreach (Certificate cert in m_certificates)
            {
                if (thumbprints.Contains(cert.Thumbprint))
                {
                    result.Add(cert);
                }
            }

            return result;
        }

        private readonly List<Certificate> m_certificates;
        private bool m_disposed;
    }
}
