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
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// Allocates the BrowseNames of an environment's top-level Identifiables
    /// per clause 6.1.3, deterministically and without making source array
    /// order significant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Referable with a non-empty <c>idShort</c> uses that exact short name.
    /// The three top-level Identifiables permit <c>idShort</c> to be absent, in
    /// which case the name is <c>&lt;kind&gt;_&lt;digest&gt;</c> where
    /// <c>&lt;digest&gt;</c> is the lowercase hexadecimal SHA-256 of the exact,
    /// non-normalized UTF-8 bytes of <c>id</c>. The raw identifier is never
    /// part of the BrowseName — it is arbitrary text of up to 2048 characters.
    /// </para>
    /// <para>
    /// Every authored short name is reserved before any derived name is
    /// allocated, and identifiers producing one derived base are processed in
    /// ascending lexicographic order of their UTF-8 bytes. That is what makes
    /// the result independent of the order the source document happened to
    /// list them in.
    /// </para>
    /// </remarks>
    public sealed class AasBrowseNameAllocator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasBrowseNameAllocator"/> class.
        /// </summary>
        public AasBrowseNameAllocator()
        {
            m_used = new HashSet<string>(StringComparer.Ordinal);
            m_pending = [];
        }

        /// <summary>
        /// Reserves the BrowseName of one top-level Identifiable that carries a
        /// short name.
        /// </summary>
        /// <remarks>
        /// Reservation happens for every authored short name before any derived
        /// name is allocated, so a derived name never takes a name an author
        /// supplied.
        /// </remarks>
        /// <param name="idShort">The authored short name.</param>
        /// <returns>The reserved BrowseName, which is the short name itself.</returns>
        /// <exception cref="ArgumentException"><paramref name="idShort"/> is <c>null</c> or empty.</exception>
        public string Reserve(string idShort)
        {
            if (string.IsNullOrEmpty(idShort))
            {
                throw new ArgumentException(
                    "A reserved BrowseName requires a non-empty idShort.",
                    nameof(idShort));
            }

            m_used.Add(idShort);
            return idShort;
        }

        /// <summary>
        /// Registers a top-level Identifiable whose <c>idShort</c> is absent,
        /// so that its derived BrowseName is allocated by
        /// <see cref="Allocate"/>.
        /// </summary>
        /// <param name="kind">The Identifiable's kind.</param>
        /// <param name="id">The Identifiable's authored identifier, verbatim.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
        public void RegisterDerived(AasNodeKind kind, string id)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }


            if (kind == AasNodeKind.SubmodelElement)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    "Only the three top-level Identifiables may omit an idShort.");
            }

            m_pending.Add(new PendingDerivation(kind, id));
        }

        /// <summary>
        /// Allocates the derived BrowseNames of every Identifiable registered
        /// by <see cref="RegisterDerived"/>.
        /// </summary>
        /// <remarks>
        /// Identifiers that produce one derived base name are processed in
        /// ascending lexicographic order of their UTF-8 bytes, and the first
        /// available name is taken: the unsuffixed base where it is free,
        /// otherwise the base followed by <c>_n</c> with the smallest
        /// non-negative integer that makes the name unused. This resolves both
        /// a SHA-256 collision and a collision with an authored short name.
        /// </remarks>
        /// <returns>The allocated BrowseName of each registered identifier, keyed by identifier.</returns>
        public IReadOnlyDictionary<string, string> Allocate()
        {
            var allocated = new Dictionary<string, string>(StringComparer.Ordinal);

            m_pending.Sort(static (left, right) =>
            {
                int baseOrder = string.CompareOrdinal(left.BaseName, right.BaseName);
                return baseOrder != 0
                    ? baseOrder
                    : CompareUtf8Bytes(left.Id, right.Id);
            });

            foreach (PendingDerivation pending in m_pending)
            {
                string name = FirstAvailable(pending.BaseName);
                m_used.Add(name);
                allocated[pending.Id] = name;
            }

            m_pending.Clear();
            return allocated;
        }

        /// <summary>
        /// Computes the unsuffixed derived base name of one Identifiable.
        /// </summary>
        /// <param name="kind">The Identifiable's kind.</param>
        /// <param name="id">The Identifiable's authored identifier, verbatim.</param>
        /// <returns>The base name, <c>&lt;kind&gt;_&lt;digest&gt;</c>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is <c>null</c>.</exception>
        public static string DeriveBaseName(AasNodeKind kind, string id)
        {
            if (id is null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            return string.Concat(KindNameOf(kind), "_", Sha256Hex(id));
        }

        /// <summary>
        /// Returns the exact metamodel class name a derived BrowseName is
        /// prefixed with.
        /// </summary>
        /// <param name="kind">The Identifiable's kind.</param>
        /// <returns><c>AssetAdministrationShell</c>, <c>Submodel</c> or <c>ConceptDescription</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a top-level Identifiable kind.</exception>
        public static string KindNameOf(AasNodeKind kind)
        {
            return kind switch
            {
                AasNodeKind.Shell => "AssetAdministrationShell",
                AasNodeKind.Submodel => "Submodel",
                AasNodeKind.ConceptDescription => "ConceptDescription",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        /// <summary>
        /// Returns the BrowseName of a member of a
        /// <c>SubmodelElementList</c>, which is its index rendered as a decimal
        /// string.
        /// </summary>
        /// <remarks>
        /// The metamodel gives a list member no short name. Order itself is
        /// carried by the <c>Index</c> Property rather than by the BrowseName,
        /// because a BrowseName is a name and not a position.
        /// </remarks>
        /// <param name="index">The member's zero-based position.</param>
        /// <returns>The BrowseName.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        public static string ForListMember(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns the DisplayName of a Referable: the short name where one
        /// exists, otherwise the index for a list member or the derived
        /// BrowseName for a top-level Identifiable.
        /// </summary>
        /// <param name="idShort">The authored short name, or <c>null</c> where absent.</param>
        /// <param name="browseName">The allocated BrowseName.</param>
        /// <returns>The DisplayName.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="browseName"/> is <c>null</c>.</exception>
        public static string DisplayNameFor(string? idShort, string browseName)
        {
            if (browseName is null)
            {
                throw new ArgumentNullException(nameof(browseName));
            }

            return string.IsNullOrEmpty(idShort) ? browseName : idShort;
        }

        private string FirstAvailable(string baseName)
        {
            if (!m_used.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 0; ; suffix++)
            {
                string candidate = string.Concat(
                    baseName,
                    "_",
                    suffix.ToString(CultureInfo.InvariantCulture));

                if (!m_used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static string Sha256Hex(string id)
        {
            // The exact, non-normalized UTF-8 bytes: two identifiers that
            // differ only by Unicode normalization form are different
            // identifiers and must not share a BrowseName.
            byte[] source = Encoding.UTF8.GetBytes(id);
#if NET5_0_OR_GREATER
            byte[] digest = SHA256.HashData(source);
#else
            using var sha = SHA256.Create();
            byte[] digest = sha.ComputeHash(source);
#endif

            var builder = new StringBuilder(digest.Length * 2);
            foreach (byte octet in digest)
            {
                builder.Append(octet.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static int CompareUtf8Bytes(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);

            int shared = Math.Min(leftBytes.Length, rightBytes.Length);
            for (int i = 0; i < shared; i++)
            {
                int order = leftBytes[i].CompareTo(rightBytes[i]);
                if (order != 0)
                {
                    return order;
                }
            }

            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

        private readonly struct PendingDerivation
        {
            public PendingDerivation(AasNodeKind kind, string id)
            {
                Id = id;
                BaseName = DeriveBaseName(kind, id);
            }

            public string Id { get; }

            public string BaseName { get; }
        }

        private readonly HashSet<string> m_used;
        private readonly List<PendingDerivation> m_pending;
    }
}
