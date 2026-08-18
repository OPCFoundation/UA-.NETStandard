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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// Constructs the RDF IRI for a DPP template semantic identifier.
    /// </summary>
    /// <remarks>
    /// Clause 3 is deliberately one-way. The result retains the original
    /// identifier verbatim for <c>aas:Key/value</c>, records the trimmed value
    /// used for rule matching, and exposes whether trimming occurred so a
    /// caller can report the two whitespace defects listed in Annex C. No
    /// inverse is provided because ECLASS and hash constructions must not be
    /// inverted.
    /// </remarks>
    public static class AasDppIdentifier
    {
        /// <summary>
        /// Constructs an IRI by applying the first matching clause 3 rule.
        /// </summary>
        /// <param name="identifier">The identifier as written in the template.</param>
        /// <returns>The constructed identifier result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <c>null</c>.</exception>
        public static AasDppIdentifierResult Construct(string identifier)
        {
            if (identifier is null)
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            string trimmed = identifier.Trim();
            bool wasTrimmed = !string.Equals(identifier, trimmed, StringComparison.Ordinal);

            if (IsAbsoluteIriWithAtMostOneHash(trimmed, out Uri? iri) && iri is not null)
            {
                return new AasDppIdentifierResult(
                    identifier,
                    trimmed,
                    trimmed,
                    AasDppIdentifierRule.AlreadyIri,
                    wasTrimmed,
                    IsDereferenceable(iri));
            }

            if (trimmed.StartsWith("0173-1#", StringComparison.Ordinal))
            {
                return new AasDppIdentifierResult(
                    identifier,
                    trimmed,
                    "https://rdf.eclass.eu/resource/" + trimmed.Replace('#', '_'),
                    AasDppIdentifierRule.EclassIrdi,
                    wasTrimmed,
                    ExpectedToDereference: true);
            }

            return new AasDppIdentifierResult(
                identifier,
                trimmed,
                "https://w3id.org/aas-dpp/id/" + Sha256Hex(trimmed),
                AasDppIdentifierRule.Hash,
                wasTrimmed,
                ExpectedToDereference: false);
        }

        private static bool IsAbsoluteIriWithAtMostOneHash(string identifier, out Uri? iri)
        {
            iri = null;
            if (CountHashes(identifier) > 1)
            {
                return false;
            }

            return Uri.TryCreate(identifier, UriKind.Absolute, out iri);
        }

        private static int CountHashes(string value)
        {
            int count = 0;
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == '#')
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsDereferenceable(Uri iri)
        {
            return string.Equals(iri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256Hex(string identifier)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
#else
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identifier));
#endif
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// The result of constructing a DPP identifier IRI.
    /// </summary>
    /// <param name="OriginalIdentifier">The identifier exactly as it appeared in the template.</param>
    /// <param name="TrimmedIdentifier">The identifier after mandatory leading and trailing whitespace trim.</param>
    /// <param name="Iri">The IRI to use as the RDF resource.</param>
    /// <param name="Rule">The first clause 3 rule that matched.</param>
    /// <param name="WasTrimmed">Whether whitespace was trimmed before rule matching.</param>
    /// <param name="ExpectedToDereference">Whether the IRI is expected to retrieve a resource.</param>
    public sealed record AasDppIdentifierResult(
        string OriginalIdentifier,
        string TrimmedIdentifier,
        string Iri,
        AasDppIdentifierRule Rule,
        bool WasTrimmed,
        bool ExpectedToDereference);
}
