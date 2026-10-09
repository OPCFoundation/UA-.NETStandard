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
using System.Linq;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// The category of an explicit NodeSet identifier sidecar validation error.
    /// </summary>
    public enum NodesetIdentifierValidationErrorKind
    {
        /// <summary>
        /// The configured sidecar was not provided as an additional file.
        /// </summary>
        MissingFile,

        /// <summary>
        /// A CSV symbolic name occurs more than once.
        /// </summary>
        DuplicateSymbolicName,

        /// <summary>
        /// A CSV numeric identifier occurs more than once.
        /// </summary>
        DuplicateNumericId,

        /// <summary>
        /// A CSV symbol was not imported from the NodeSet.
        /// </summary>
        UnknownSymbol,

        /// <summary>
        /// A CSV numeric identifier differs from the NodeSet node identifier.
        /// </summary>
        NumericIdMismatch,

        /// <summary>
        /// A CSV node class differs from the NodeSet node class.
        /// </summary>
        NodeClassMismatch,

        /// <summary>
        /// An explicit sidecar was configured for more than one NodeSet.
        /// </summary>
        AssignedToMultipleModels,

        /// <summary>
        /// A CSV row does not follow the identifier CSV format.
        /// </summary>
        InvalidRow
    }

    /// <summary>
    /// A validation error for an explicit NodeSet identifier CSV sidecar.
    /// </summary>
    public sealed record class NodesetIdentifierValidationError
    {
        /// <summary>
        /// The error category.
        /// </summary>
        public NodesetIdentifierValidationErrorKind Kind { get; init; }

        /// <summary>
        /// The NodeSet XML file declaring the sidecar.
        /// </summary>
        public string? NodeSetFilePath { get; init; }

        /// <summary>
        /// The configured or resolved sidecar path.
        /// </summary>
        public string? IdentifierFilePath { get; init; }

        /// <summary>
        /// The related symbolic name, if applicable.
        /// </summary>
        public string? SymbolicName { get; init; }

        /// <summary>
        /// The related value, if applicable.
        /// </summary>
        public string? Value { get; init; }

        /// <summary>
        /// Creates a missing-sidecar validation error.
        /// </summary>
        public static NodesetIdentifierValidationError MissingFile(
            string nodeSetFilePath,
            string identifierFilePath)
        {
            return new NodesetIdentifierValidationError
            {
                Kind = NodesetIdentifierValidationErrorKind.MissingFile,
                NodeSetFilePath = nodeSetFilePath,
                IdentifierFilePath = identifierFilePath
            };
        }

        /// <summary>
        /// Creates a shared-sidecar validation error.
        /// </summary>
        public static NodesetIdentifierValidationError AssignedToMultipleModels(
            string nodeSetFilePath,
            string identifierFilePath)
        {
            return new NodesetIdentifierValidationError
            {
                Kind = NodesetIdentifierValidationErrorKind.AssignedToMultipleModels,
                NodeSetFilePath = nodeSetFilePath,
                IdentifierFilePath = identifierFilePath
            };
        }
    }

    internal static class NodesetIdentifierFileValidator
    {
        public static string? ResolvePath(
            string nodeSetFilePath,
            string identifierFilePath,
            IReadOnlyList<string> csvFiles)
        {
            if (csvFiles == null || csvFiles.Count == 0)
            {
                return null;
            }

            string relativePath = identifierFilePath.Trim();
            string adjacentPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(Path.GetDirectoryName(nodeSetFilePath) ?? string.Empty, relativePath);

            // AdditionalFiles paths are full, normalized paths; the metadata
            // is whatever the project author typed ("./x.csv", "..\x.csv",
            // "sub/x.csv"). Compare both sides in the same lexical normal form.
            // Lexical, not Path.GetFullPath: the inputs need not be file system
            // paths at all (in-memory AdditionalTexts).
            string normalizedAdjacent = NormalizePath(adjacentPath);
            string? directMatch = csvFiles.FirstOrDefault(path =>
                string.Equals(NormalizePath(path), normalizedAdjacent, s_pathComparison));
            if (directMatch != null)
            {
                return directMatch;
            }

            // Fall back to a unique AdditionalFile whose path ends with the
            // given one - on a directory boundary, or "NodeIds.csv" would also
            // match "Opc.Ua.Di.NodeIds.csv". Leading "./" and "../" segments
            // cannot be part of an AdditionalFile path, so they are dropped.
            string suffix = NormalizePath(relativePath);
            while (suffix.StartsWith("../", StringComparison.Ordinal) ||
                suffix.StartsWith("./", StringComparison.Ordinal) ||
                suffix.StartsWith('/'))
            {
                suffix = suffix[(suffix.IndexOf('/', StringComparison.Ordinal) + 1)..];
            }
            if (suffix.Length == 0)
            {
                return null;
            }
            string[] matches = [.. csvFiles
                .Where(path => EndsWithPathSegments(NormalizePath(path), suffix))
                .OrderBy(path => path, StringComparer.Ordinal)];
            return matches.Length == 1 ? matches[0] : null;
        }

        /// <summary>
        /// True when <paramref name="path"/> ends with <paramref name="suffix"/>
        /// and the suffix starts at a directory boundary.
        /// </summary>
        private static bool EndsWithPathSegments(string path, string suffix)
        {
            if (!path.EndsWith(suffix, s_pathComparison))
            {
                return false;
            }
            return path.Length == suffix.Length ||
                path[path.Length - suffix.Length - 1] == '/';
        }

        /// <summary>
        /// Lexically normalizes a path: both separators become '/', "."
        /// segments are removed and ".." segments cancel the segment before
        /// them. Roots ("C:", "memory:", a leading "/") are kept as they are.
        /// </summary>
        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string[] segments = path.Replace('\\', '/').Split('/');
            var result = new List<string>(segments.Length);
            for (int ii = 0; ii < segments.Length; ii++)
            {
                string segment = segments[ii];
                if (segment == "." && ii > 0)
                {
                    continue;
                }
                if (segment == ".." &&
                    result.Count > 0 &&
                    result[^1].Length > 0 &&
                    result[^1] != ".." &&
                    result[^1] != "." &&
                    !result[^1].EndsWith(':'))
                {
                    result.RemoveAt(result.Count - 1);
                    continue;
                }
                result.Add(segment);
            }
            return string.Join("/", result);
        }

        /// <summary>
        /// Windows file systems are case insensitive; a path typed in the
        /// project with another casing still names the same file there.
        /// </summary>
        private static readonly StringComparison s_pathComparison =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        public static IEnumerable<NodesetIdentifierValidationError> Validate(
            IFileSystem fileSystem,
            string nodeSetFilePath,
            string modelUri,
            string identifierFilePath,
            ITelemetryContext telemetry)
        {
            var settings = new NodeSetReaderSettings();
            var importer = new NodeSetToModelDesign(
                fileSystem,
                nodeSetFilePath,
                settings,
                telemetry);
            var symbols = importer
                .GetImportedSymbols(modelUri)
                .ToDictionary(symbol => symbol.SymbolicName, StringComparer.Ordinal);
            List<NodesetIdentifierCsvRow> rows = Parse(identifierFilePath, fileSystem);
            var errors = new List<NodesetIdentifierValidationError>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<uint>();

            foreach (NodesetIdentifierCsvRow row in rows)
            {
                if (!row.IsValid)
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.InvalidRow,
                        nodeSetFilePath,
                        identifierFilePath,
                        null,
                        row.LineNumber.ToString(CultureInfo.InvariantCulture)));
                    continue;
                }

                if (!names.Add(row.SymbolicName!))
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.DuplicateSymbolicName,
                        nodeSetFilePath,
                        identifierFilePath,
                        row.SymbolicName,
                        null));
                }

                if (!ids.Add(row.NumericId))
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.DuplicateNumericId,
                        nodeSetFilePath,
                        identifierFilePath,
                        row.SymbolicName,
                        row.NumericId.ToString(CultureInfo.InvariantCulture)));
                }

                if (!symbols.TryGetValue(row.SymbolicName!, out NodesetImportedSymbol symbol))
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.UnknownSymbol,
                        nodeSetFilePath,
                        identifierFilePath,
                        row.SymbolicName,
                        null));
                    continue;
                }

                if (symbol.NumericId != row.NumericId)
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.NumericIdMismatch,
                        nodeSetFilePath,
                        identifierFilePath,
                        row.SymbolicName,
                        row.NumericId.ToString(CultureInfo.InvariantCulture)));
                }

                if (symbol.NodeClass != row.NodeClass)
                {
                    errors.Add(CreateError(
                        NodesetIdentifierValidationErrorKind.NodeClassMismatch,
                        nodeSetFilePath,
                        identifierFilePath,
                        row.SymbolicName,
                        row.NodeClass.ToString()));
                }
            }

            return errors;
        }

        private static List<NodesetIdentifierCsvRow> Parse(
            string identifierFilePath,
            IFileSystem fileSystem)
        {
            var rows = new List<NodesetIdentifierCsvRow>();
            using Stream stream = fileSystem.OpenRead(identifierFilePath);
            using var reader = new StreamReader(stream);
            int lineNumber = 0;

            while (reader.ReadLine() is string line)
            {
                lineNumber++;
                line = line.TrimStart('\uFEFF').Trim();
                if (string.IsNullOrEmpty(line) ||
                    line.StartsWith('#'))
                {
                    continue;
                }

                string[] columns = line.Split(',');
                if (IsHeader(columns))
                {
                    continue;
                }

                if (columns.Length != 3 ||
                    !uint.TryParse(
                        columns[1].Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out uint numericId) ||
                    !Enum.TryParse(columns[2].Trim(), true, out NodeClass nodeClass) ||
                    nodeClass == NodeClass.Unspecified ||
                    string.IsNullOrWhiteSpace(columns[0]))
                {
                    rows.Add(new NodesetIdentifierCsvRow { LineNumber = lineNumber });
                    continue;
                }

                rows.Add(new NodesetIdentifierCsvRow
                {
                    LineNumber = lineNumber,
                    SymbolicName = columns[0].Trim(),
                    NumericId = numericId,
                    NodeClass = nodeClass,
                    IsValid = true
                });
            }

            return rows;
        }

        private static bool IsHeader(string[] columns)
        {
            return columns.Length >= 3 &&
                string.Equals(
                    columns[0].TrimStart('\uFEFF').Trim(),
                    "SymbolicName",
                    StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(columns[1].Trim(), "Id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(columns[1].Trim(), "NodeId", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(columns[1].Trim(), "NumericId", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(columns[2].Trim(), "NodeClass", StringComparison.OrdinalIgnoreCase);
        }

        private static NodesetIdentifierValidationError CreateError(
            NodesetIdentifierValidationErrorKind kind,
            string nodeSetFilePath,
            string identifierFilePath,
            string? symbolicName,
            string? value)
        {
            return new NodesetIdentifierValidationError
            {
                Kind = kind,
                NodeSetFilePath = nodeSetFilePath,
                IdentifierFilePath = identifierFilePath,
                SymbolicName = symbolicName,
                Value = value
            };
        }

        private sealed record class NodesetIdentifierCsvRow
        {
            public int LineNumber { get; init; }

            public string? SymbolicName { get; init; }

            public uint NumericId { get; init; }

            public NodeClass NodeClass { get; init; }

            public bool IsValid { get; init; }
        }
    }
}
