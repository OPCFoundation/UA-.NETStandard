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
 *
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
using System.IO;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The result of a NodeSet2 to WoT to NodeSet2 round trip.
    /// </summary>
    public sealed class WotNodeSetRoundtripReport
    {
        internal WotNodeSetRoundtripReport(
            bool nativeProjectionPreserved,
            bool envelopePreserved,
            bool usedPreservationEnvelope,
            NodeSetComparisonResult comparison,
            IReadOnlyList<WotDiagnostic> diagnostics)
        {
            NativeProjectionPreserved = nativeProjectionPreserved;
            EnvelopePreserved = envelopePreserved;
            UsedPreservationEnvelope = usedPreservationEnvelope;
            Comparison = comparison;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Gets a value indicating whether the structured native projection,
        /// without an envelope, reproduced an equivalent NodeSet2.
        /// </summary>
        public bool NativeProjectionPreserved { get; }

        /// <summary>
        /// Gets a value indicating whether the envelope reproduced a byte-identical NodeSet2.
        /// </summary>
        public bool EnvelopePreserved { get; }

        /// <summary>
        /// Gets a value indicating whether the conversion used a
        /// <c>uav:nodeSet</c> preservation envelope.
        /// </summary>
        public bool UsedPreservationEnvelope { get; }

        /// <summary>
        /// Gets the canonical comparison of the source and restored NodeSet2.
        /// </summary>
        public NodeSetComparisonResult Comparison { get; }

        /// <summary>
        /// Gets the diagnostics produced during the round trip.
        /// </summary>
        public IReadOnlyList<WotDiagnostic> Diagnostics { get; }
    }

    /// <summary>
    /// Converts a NodeSet2 document to a WoT document and back, and reports
    /// what survived.
    /// </summary>
    /// <remarks>
    /// The round trip lives with the conversion it exercises rather than with
    /// <see cref="NodeSetComparer"/>, which compares any two NodeSet2
    /// documents and has no business knowing that WoT exists. It uses that
    /// comparison to state its result, which is the only direction the
    /// dependency may run.
    /// </remarks>
    public static class WotNodeSetRoundtrip
    {
        /// <summary>
        /// Converts a NodeSet2 document to a WoT document and back. By default,
        /// the report uses native-only mode so completeness is never proved by
        /// the preservation envelope.
        /// </summary>
        /// <param name="source">The NodeSet2 document to round trip.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The round-trip report.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="source"/> is <c>null</c>.
        /// </exception>
        public static WotNodeSetRoundtripReport Run(
            UANodeSet source,
            WotNodeSetConverterOptions? options = null)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var diagnostics = new List<WotDiagnostic>();
            byte[] sourceBytes = Serialize(source);
            WotNodeSetConverterOptions effectiveOptions = options ??
                new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Never
                };

            WotConversionResult<WotDocument> forward =
                WotNodeSetConverter.FromNodeSetResult(source, null, effectiveOptions);
            AddRange(diagnostics, forward.Diagnostics);
            if (forward.Value is null)
            {
                return new WotNodeSetRoundtripReport(
                    false,
                    false,
                    false,
                    new NodeSetComparisonResult(
                        false,
                        ["The NodeSet could not be converted to a WoT document."]),
                    diagnostics);
            }

            using WotDocument document = forward.Value;
            bool usedEnvelope = document.TryGetEnvelope(out _);
            WotConversionResult<UANodeSet> backward =
                WotNodeSetConverter.ToNodeSetResult(document, effectiveOptions);
            AddRange(diagnostics, backward.Diagnostics);
            if (backward.Value is null)
            {
                return new WotNodeSetRoundtripReport(
                    false,
                    false,
                    usedEnvelope,
                    new NodeSetComparisonResult(
                        false,
                        ["The WoT document could not be converted back to a NodeSet."]),
                    diagnostics);
            }

            byte[] restoredBytes = Serialize(backward.Value);
            NodeSetComparisonResult comparison = NodeSetComparer.CompareXml(
                sourceBytes,
                restoredBytes,
                effectiveOptions.ToComparisonOptions());
            bool byteIdentical = ByteEquals(sourceBytes, restoredBytes);
            return new WotNodeSetRoundtripReport(
                !usedEnvelope && comparison.AreEquivalent,
                usedEnvelope && byteIdentical,
                usedEnvelope,
                comparison,
                diagnostics);
        }

        private static byte[] Serialize(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static bool ByteEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            return left.SequenceEqual(right);
        }

        private static void AddRange(List<WotDiagnostic> target, IReadOnlyList<WotDiagnostic> source)
        {
            for (int ii = 0; ii < source.Count; ii++)
            {
                target.Add(source[ii]);
            }
        }
    }
}
