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
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// The result of attempting to convert one WoT AdditionalFile input into
    /// an in-memory NodeSet2 additional text. Conversion never throws: parse,
    /// bounds, missing preservation/native mapping, dependency/resolver and
    /// general conversion problems are all captured as <see cref="Diagnostic"/>s
    /// instead, so a single malformed or unsupported WoT input can never fail
    /// the whole generator run.
    /// </summary>
    internal sealed class WotConversionOutcome
    {
        /// <summary>
        /// The path of the original WoT AdditionalFile.
        /// </summary>
        public string SourcePath { get; }

        /// <summary>
        /// The synthesized in-memory NodeSet2 additional text, or
        /// <c>null</c> when conversion did not produce a usable result.
        /// </summary>
        public AdditionalText NodeSetText { get; }

        /// <summary>
        /// The AdditionalFiles options (Prefix, Name, ModelUri, Version,
        /// Ignore) captured from the original WoT input, to be preserved
        /// after wrapping it as a NodeSet2 file.
        /// </summary>
        public NodesetFileOptions Options { get; }

        /// <summary>
        /// The diagnostics produced while attempting the conversion, in the
        /// order they occurred. Does not include virtual-path collision
        /// diagnostics, which require the full set of inputs to detect.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public WotConversionOutcome(
            string sourcePath,
            AdditionalText nodeSetText,
            NodesetFileOptions options,
            ImmutableArray<Diagnostic> diagnostics)
        {
            SourcePath = sourcePath;
            NodeSetText = nodeSetText;
            Options = options;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// Presents a WoT model input to the existing generator as an in-memory
    /// NodeSet2 file, using the completed <see cref="Opc.Ua.Wot"/> converter
    /// entirely in memory (no file or network I/O beyond the supplied
    /// <see cref="AdditionalText"/> content).
    /// </summary>
    internal sealed class WotNodeSetAdditionalText : AdditionalText
    {
        private WotNodeSetAdditionalText(string path, SourceText text)
        {
            Path = path;
            m_text = text;
        }

        /// <inheritdoc/>
        public override string Path { get; }

        /// <inheritdoc/>
        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return m_text;
        }

        /// <summary>
        /// Attempts to convert a WoT AdditionalFile into an in-memory
        /// NodeSet2 additional text. Never throws (other than on
        /// cancellation): every failure mode is reported as a diagnostic on
        /// the returned <see cref="WotConversionOutcome"/> instead, so a
        /// malformed or unsupported document can never crash the generator.
        /// </summary>
        /// <param name="source">The original WoT AdditionalFile.</param>
        /// <param name="options">The AdditionalFiles options to preserve.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public static WotConversionOutcome Convert(
            AdditionalText source,
            NodesetFileOptions options,
            CancellationToken cancellationToken)
        {
            string sourcePath = source.Path;
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            SourceText sourceText;
            try
            {
                sourceText = source.GetText(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(CreateParseDiagnostic(sourcePath, null, ex.Message));
                return Failed(sourcePath, options, diagnostics);
            }
            if (sourceText is null)
            {
                diagnostics.Add(CreateParseDiagnostic(
                    sourcePath, null, "The WoT model source text could not be read."));
                return Failed(sourcePath, options, diagnostics);
            }

            byte[] utf8Json;
            try
            {
                utf8Json = Encoding.UTF8.GetBytes(sourceText.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(CreateParseDiagnostic(sourcePath, sourceText, ex.Message));
                return Failed(sourcePath, options, diagnostics);
            }

            WotDocument document;
            try
            {
                document = WotDocument.Parse(utf8Json);
            }
            catch (JsonException ex)
            {
                diagnostics.Add(CreateParseDiagnostic(sourcePath, sourceText, ex));
                return Failed(sourcePath, options, diagnostics);
            }
            catch (FormatException ex)
            {
                // Thrown when the document exceeds a configured size bound.
                diagnostics.Add(CreateParseDiagnostic(sourcePath, sourceText, ex.Message));
                return Failed(sourcePath, options, diagnostics);
            }

            UANodeSet nodeSet;
            try
            {
                using (document)
                {
                    // No external resolver: source generation performs no
                    // file or network I/O beyond the supplied AdditionalText
                    // content, so referenced TD/TM documents are left
                    // unresolved (reported as a WotDiagnosticCode.
                    // UnresolvedReference warning by the converter).
                    WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(
                        document,
                        options: null,
                        thingResolver: null,
                        resolutionContext: null);
                    AppendConversionDiagnostics(diagnostics, sourcePath, result.Diagnostics);
                    // Exclude any result that produced an error diagnostic even
                    // when a (partial or inconsistent) NodeSet value was still
                    // produced. This decision is independent of whether the
                    // MODELGEN031 error diagnostic is later reported or suppressed,
                    // so a suppressed conversion error can never emit a model.
                    nodeSet = result.Success ? result.Value : null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Defence in depth: the converter is documented to report
                // diagnostics rather than throw. Guard against any residual
                // exception so a single malformed input can never take down
                // the whole generator run.
                diagnostics.Add(CreateConversionExceptionDiagnostic(sourcePath, ex));
                return Failed(sourcePath, options, diagnostics);
            }

            if (nodeSet is null)
            {
                // An error diagnostic explaining why was already appended.
                return Failed(sourcePath, options, diagnostics);
            }

            string xml;
            try
            {
                using var stream = new MemoryStream();
                nodeSet.Write(stream);
                xml = Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(CreateConversionExceptionDiagnostic(sourcePath, ex));
                return Failed(sourcePath, options, diagnostics);
            }
            if (xml.Length > 0 && xml[0] == '\uFEFF')
            {
                xml = xml.Substring(1);
            }

            var nodeSetText = new WotNodeSetAdditionalText(
                GetNodeSetPath(sourcePath),
                SourceText.From(xml, Encoding.UTF8));
            return new WotConversionOutcome(
                sourcePath, nodeSetText, options, diagnostics.ToImmutable());
        }

        private static WotConversionOutcome Failed(
            string sourcePath,
            NodesetFileOptions options,
            ImmutableArray<Diagnostic>.Builder diagnostics)
        {
            return new WotConversionOutcome(sourcePath, null, options, diagnostics.ToImmutable());
        }

        private static void AppendConversionDiagnostics(
            ImmutableArray<Diagnostic>.Builder builder,
            string sourcePath,
            IReadOnlyList<WotDiagnostic> wotDiagnostics)
        {
            if (wotDiagnostics.Count == 0)
            {
                return;
            }
            Location location = CreateFileLocation(sourcePath);
            for (int ii = 0; ii < wotDiagnostics.Count; ii++)
            {
                WotDiagnostic diagnostic = wotDiagnostics[ii];
                DiagnosticDescriptor descriptor = diagnostic.Severity switch
                {
                    WotDiagnosticSeverity.Error => SourceGenerator.WotConversionError,
                    WotDiagnosticSeverity.Warning => SourceGenerator.WotConversionWarning,
                    _ => SourceGenerator.WotConversionInfo
                };
                builder.Add(Diagnostic.Create(descriptor, location, sourcePath, diagnostic.ToString()));
            }
        }

        private static Diagnostic CreateParseDiagnostic(
            string sourcePath, SourceText sourceText, JsonException ex)
        {
            Location location = CreateLocation(sourcePath, sourceText, ex.LineNumber, ex.BytePositionInLine);
            return Diagnostic.Create(SourceGenerator.WotParseError, location, sourcePath, ex.Message);
        }

        private static Diagnostic CreateParseDiagnostic(
            string sourcePath, SourceText sourceText, string message)
        {
            Location location = CreateLocation(sourcePath, sourceText, null, null);
            return Diagnostic.Create(SourceGenerator.WotParseError, location, sourcePath, message);
        }

        private static Diagnostic CreateConversionExceptionDiagnostic(string sourcePath, Exception ex)
        {
            Location location = CreateFileLocation(sourcePath);
            return Diagnostic.Create(
                SourceGenerator.WotConversionError,
                location,
                sourcePath,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// Creates a location anchored at the start of the given file. Used
        /// when no more precise position is available.
        /// </summary>
        internal static Location CreateFileLocation(string path)
        {
            return Location.Create(path, default, default);
        }

        private static Location CreateLocation(
            string path,
            SourceText text,
            long? lineNumber,
            long? bytePositionInLine)
        {
            if (text is null || text.Lines.Count == 0)
            {
                return CreateFileLocation(path);
            }
            int line = 0;
            int character = 0;
            if (lineNumber.HasValue && lineNumber.Value >= 0 && lineNumber.Value < text.Lines.Count)
            {
                line = (int)lineNumber.Value;
                if (bytePositionInLine.HasValue && bytePositionInLine.Value >= 0)
                {
                    character = Math.Min((int)bytePositionInLine.Value, text.Lines[line].Span.Length);
                }
            }
            var position = new LinePosition(line, character);
            int offset = text.Lines[line].Start + character;
            var span = new TextSpan(offset, 0);
            return Location.Create(path, span, new LinePositionSpan(position, position));
        }

        private static string GetNodeSetPath(string sourcePath)
        {
            string directory = System.IO.Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string name = System.IO.Path.GetFileName(sourcePath);
            // The canonical suffixes are checked before the bare ".jsonld"
            // fallback used by opted-in plain JSON-LD inputs: none of the
            // canonical suffixes is itself a suffix of another, but every one
            // of them ends with ".jsonld" or ".json" as plain text, so the
            // untyped fallback must come last.
            foreach (string suffix in Extensions.WotFileExtensions)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    return System.IO.Path.Combine(directory, name + ".NodeSet2.xml");
                }
            }
            const string plainJsonLd = ".jsonld";
            if (name.EndsWith(plainJsonLd, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - plainJsonLd.Length);
            }
            return System.IO.Path.Combine(directory, name + ".NodeSet2.xml");
        }

        private readonly SourceText m_text;
    }
}
