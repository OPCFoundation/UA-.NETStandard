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
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// A row of the DPP SSSOM mapping set defined by Annex A.
    /// </summary>
    /// <param name="SubjectId">The identifier as written in the template, after mandatory trimming.</param>
    /// <param name="SubjectLabel">The metamodel classes that carry the identifier.</param>
    /// <param name="PredicateId">The SKOS predicate, <c>skos:exactMatch</c> or <c>skos:closeMatch</c>.</param>
    /// <param name="ObjectId">The constructed RDF IRI.</param>
    /// <param name="MappingJustification">The construction justification.</param>
    /// <param name="SubjectSource">The source repository.</param>
    /// <param name="SubjectSourceVersion">The source repository version.</param>
    /// <param name="ObjectSource">The identifier scheme.</param>
    /// <param name="Confidence">The confidence required by clause 4.</param>
    /// <param name="SubjectType">The AAS key types carrying the identifier.</param>
    /// <param name="Comment">The members carrying the identifier and whether the result dereferences.</param>
    public sealed record AasDppMappingRow(
        string SubjectId,
        string SubjectLabel,
        string PredicateId,
        string ObjectId,
        string MappingJustification,
        string SubjectSource,
        string SubjectSourceVersion,
        string ObjectSource,
        double Confidence,
        string SubjectType,
        string Comment);

    /// <summary>
    /// Reads and writes the DPP SSSOM mapping set.
    /// </summary>
    /// <remarks>
    /// The embedded set contains only identifiers that need clause 3 rule 2 or rule 3 construction.
    /// Identifiers usable as written are deliberately absent. For a lookup against the pinned template
    /// set, a miss is therefore a normal outcome meaning that the caller should apply rule 1 with
    /// <see cref="AasDppIdentifier.Construct"/> rather than report an error.
    /// </remarks>
    public static class AasDppMappingSet
    {
        /// <summary>
        /// Gets the compressed embedded resource name.
        /// </summary>
        public static string EmbeddedResourceName => "Opc.Ua.Aas.Dpp.mappings.sssom.tsv.gz";

        /// <summary>
        /// Gets the Annex A column order emitted by <see cref="WriteTsv"/>.
        /// </summary>
        public static ArrayOf<string> Columns => s_columns;

        /// <summary>
        /// Gets the number of rows in the pinned set.
        /// </summary>
        public static int PinnedRowCount => 185;

        /// <summary>
        /// Reads the embedded compressed mapping set.
        /// </summary>
        /// <returns>The mapping rows, materialized on demand.</returns>
        public static IEnumerable<AasDppMappingRow> ReadEmbedded()
        {
            Stream? stream = typeof(AasDppMappingSet).GetTypeInfo().Assembly.GetManifestResourceStream(
                EmbeddedResourceName);
            if (stream is null)
            {
                throw new InvalidOperationException("The embedded DPP mapping set resource was not found.");
            }

            return ReadCompressed(stream);
        }

        /// <summary>
        /// Reads an SSSOM TSV mapping set.
        /// </summary>
        /// <param name="reader">The text reader containing the TSV.</param>
        /// <returns>The mapping rows, materialized on demand.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
        public static IEnumerable<AasDppMappingRow> ReadTsv(TextReader reader)
        {
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            return ReadTsvCore(reader);
        }

        /// <summary>
        /// Writes rows in Annex A TSV column order.
        /// </summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="rows">The rows to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="rows"/> is <c>null</c>.</exception>
        public static void WriteTsv(TextWriter writer, IEnumerable<AasDppMappingRow> rows)
        {
            if (writer is null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (rows is null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            writer.WriteLine(string.Join("\t", s_columnNames));
            foreach (AasDppMappingRow row in rows)
            {
                if (row is null)
                {
                    throw new ArgumentException("The mapping row collection contains a null row.", nameof(rows));
                }

                WriteField(writer, row.SubjectId);
                WriteField(writer, row.SubjectLabel);
                WriteField(writer, row.PredicateId);
                WriteField(writer, row.ObjectId);
                WriteField(writer, row.MappingJustification);
                WriteField(writer, row.SubjectSource);
                WriteField(writer, row.SubjectSourceVersion);
                WriteField(writer, row.ObjectSource);
                WriteField(writer, row.Confidence.ToString("0.0################", CultureInfo.InvariantCulture));
                WriteField(writer, row.SubjectType);
                WriteField(writer, row.Comment, last: true);
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Looks up an identifier in the embedded set.
        /// </summary>
        /// <param name="subjectId">The template identifier, before or after clause 3 trimming.</param>
        /// <param name="row">The mapping row when the return value is <c>true</c>.</param>
        /// <returns>
        /// <c>true</c> when the identifier needs a listed construction; <c>false</c> when no row exists.
        /// For the pinned DPP templates, a miss means the identifier is used as written by rule 1.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="subjectId"/> is <c>null</c>.</exception>
        public static bool TryFindEmbedded(string subjectId, out AasDppMappingRow? row)
        {
            if (subjectId is null)
            {
                throw new ArgumentNullException(nameof(subjectId));
            }

            string trimmed = subjectId.Trim();
            foreach (AasDppMappingRow candidate in ReadEmbedded())
            {
                if (string.Equals(candidate.SubjectId, trimmed, StringComparison.Ordinal))
                {
                    row = candidate;
                    return true;
                }
            }

            row = null;
            return false;
        }

        private static IEnumerable<AasDppMappingRow> ReadCompressed(Stream stream)
        {
            using (stream)
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                foreach (AasDppMappingRow row in ReadTsv(reader))
                {
                    yield return row;
                }
            }
        }

        private static IEnumerable<AasDppMappingRow> ReadTsvCore(TextReader reader)
        {
            string? line;
            string[]? header = null;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                if (header is null)
                {
                    header = line.Split('\t');
                    continue;
                }

                string[] columns = line.Split('\t');
                yield return CreateRow(header, columns, lineNumber);
            }

            if (header is null)
            {
                throw new FormatException("The DPP mapping TSV does not contain a header row.");
            }
        }

        private static AasDppMappingRow CreateRow(string[] header, string[] columns, int lineNumber)
        {
            return new AasDppMappingRow(
                GetColumn(header, columns, "subject_id", lineNumber),
                GetColumn(header, columns, "subject_label", lineNumber),
                GetColumn(header, columns, "predicate_id", lineNumber),
                GetColumn(header, columns, "object_id", lineNumber),
                GetColumn(header, columns, "mapping_justification", lineNumber),
                GetColumn(header, columns, "subject_source", lineNumber),
                GetColumn(header, columns, "subject_source_version", lineNumber),
                GetColumn(header, columns, "object_source", lineNumber),
                ParseConfidence(GetColumn(header, columns, "confidence", lineNumber), lineNumber),
                GetColumn(header, columns, "subject_type", lineNumber),
                GetColumn(header, columns, "comment", lineNumber));
        }

        private static string GetColumn(string[] header, string[] columns, string name, int lineNumber)
        {
            int index = Array.IndexOf(header, name);
            if (index < 0)
            {
                throw new FormatException("The DPP mapping TSV header is missing column '" + name + "'.");
            }
            if (index >= columns.Length)
            {
                throw new FormatException("The DPP mapping TSV row " + lineNumber.ToString(CultureInfo.InvariantCulture) +
                    " is missing column '" + name + "'.");
            }

            return columns[index];
        }

        private static double ParseConfidence(string value, int lineNumber)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double confidence))
            {
                throw new FormatException("The DPP mapping TSV row " + lineNumber.ToString(CultureInfo.InvariantCulture) +
                    " has an invalid confidence value.");
            }

            return confidence;
        }

        private static void WriteField(TextWriter writer, string value, bool last = false)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (ContainsTsvSeparator(value))
            {
                throw new FormatException("DPP mapping TSV fields must not contain tab or newline characters.");
            }

            writer.Write(value);
            if (!last)
            {
                writer.Write('\t');
            }
        }

        private static bool ContainsTsvSeparator(string value)
        {
            for (int ii = 0; ii < value.Length; ii++)
            {
                if (value[ii] == '\t' || value[ii] == '\r' || value[ii] == '\n')
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly string[] s_columnNames =
        {
            "subject_id",
            "subject_label",
            "predicate_id",
            "object_id",
            "mapping_justification",
            "subject_source",
            "subject_source_version",
            "object_source",
            "confidence",
            "subject_type",
            "comment"
        };

        private static readonly ArrayOf<string> s_columns = new ArrayOf<string>(s_columnNames.AsMemory());
    }
}
