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
using System.Text.Json;
using System.Threading;
using System.Xml;
using Opc.Ua.Export;
using Opc.Ua.Types;

namespace Opc.Ua.Wot
{
    public static partial class WotNodeSetConverter
    {
        private static WotResolutionContext? ValidateDocumentSetInputs(
            WotDocumentSet documents,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WotResolutionContext? context = ImportsIndependentReadableModels(documents, options)
                ? new WotResolutionContext(options.ToResolverOptions())
                : null;
            var hrefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotDocumentSetEntry entry in documents.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.DocumentSetMode == WotDocumentSetMode.IndependentReadableModels && !hrefs.Add(entry.Href))
                {
                    ReportDocumentSetLimit(
                        diagnostics, WotDiagnosticCode.ValidationError, entry.Href,
                        "Each independent document must have an unambiguous source href.");
                    return context;
                }
                if (entry.Document.Utf8Json.Length > options.MaxJsonDocumentSize)
                {
                    ReportDocumentSetLimit(
                        diagnostics, WotDiagnosticCode.JsonDocumentTooLarge, entry.Href,
                        "A document exceeds the configured JSON byte bound.");
                    return context;
                }
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(
                        entry.Document.Utf8Json,
                        new JsonDocumentOptions { MaxDepth = options.MaxJsonDepth });
                }
                catch (JsonException exception)
                {
                    ReportDocumentSetLimit(diagnostics, WotDiagnosticCode.DepthExceeded, entry.Href, exception.Message);
                    return context;
                }
                if (context is null)
                {
                    continue;
                }
                if (!context.TryEnter(WotResolutionKind.Thing, entry.Href, out WotDiagnostic? blocked))
                {
                    AddResolutionFailure(blocked, entry.Href);
                    return context;
                }
                try
                {
                    if (!context.TryAddBytes(entry.Href, entry.Document.Utf8Json.Length, out blocked))
                    {
                        AddResolutionFailure(blocked, entry.Href);
                        return context;
                    }
                }
                finally
                {
                    context.Leave(entry.Href);
                }
            }
            return context;

            void AddResolutionFailure(WotDiagnostic? blocked, string href)
            {
                ReportDocumentSetLimit(
                    diagnostics, blocked?.Code ?? WotDiagnosticCode.ResolverLimitExceeded,
                    href, blocked?.Message ?? "The document set exceeds the configured resolution bounds.");
            }
        }

        private static void ValidateDocumentSetNodeCount(
            WotDocumentSet documents,
            List<WotConversionResult<UANodeSet>> parts,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics)
        {
            long count = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                count += parts[index].Value?.Items?.Length ?? 0;
                if (count > options.MaxNodeCount)
                {
                    ReportDocumentSetLimit(
                        diagnostics, WotDiagnosticCode.NodeCountExceeded, documents.Entries[index].Href,
                        "The combined partitions exceed the configured Node count.");
                    return;
                }
            }
        }

        private static void ValidateDocumentSetOutput(
            UANodeSet? result,
            string href,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null || HasErrors(diagnostics))
            {
                return;
            }
            if ((result.Items?.Length ?? 0) > options.MaxNodeCount)
            {
                ReportDocumentSetLimit(
                    diagnostics, WotDiagnosticCode.NodeCountExceeded, href,
                    "The merged NodeSet exceeds the configured Node count.");
                return;
            }
            using var xml = new DocumentSetBuffer(options.MaxNodeSetSize, cancellationToken);
            WriteDocumentSetXml(result, xml, href, options, diagnostics, cancellationToken);
        }

        private static bool WriteDocumentSetXml(
            UANodeSet part,
            DocumentSetBuffer xml,
            string href,
            WotNodeSetConverterOptions options,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            try
            {
                part.Write(xml);
                xml.Position = 0;
                XmlReaderSettings settings = CoreUtils.DefaultXmlReaderSettings();
                settings.CloseInput = false;
                settings.MaxCharactersInDocument = options.MaxNodeSetSize;
                using (var reader = XmlReader.Create(xml, settings))
                {
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.Depth >= options.MaxXmlDepth)
                        {
                            ReportDocumentSetLimit(
                                diagnostics, WotDiagnosticCode.DepthExceeded, href,
                                "The NodeSet exceeds the configured XML depth bound.");
                            return false;
                        }
                    }
                }
                xml.Position = 0;
                return true;
            }
            catch (Exception exception) when (exception is
                ServiceResultException or XmlException or InvalidOperationException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool size = (exception is ServiceResultException encoding &&
                    encoding.StatusCode == StatusCodes.BadEncodingLimitsExceeded) ||
                    (exception.InnerException is ServiceResultException inner &&
                        inner.StatusCode == StatusCodes.BadEncodingLimitsExceeded);
                ReportDocumentSetLimit(
                    diagnostics, size ? WotDiagnosticCode.NodeSetTooLarge : WotDiagnosticCode.MalformedNodeSet,
                    href, exception.Message);
                return false;
            }
        }

        private static void ReportDocumentSetLimit(
            List<WotDiagnostic> diagnostics,
            WotDiagnosticCode code,
            string href,
            string message)
        {
            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Error, code, message, new WotLocation(reference: href)));
        }

        private sealed class DocumentSetBuffer(long maximumBytes, CancellationToken cancellationToken) : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                CheckWrite(count);
                base.Write(buffer, offset, count);
            }

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                CheckWrite(buffer.Length);
                base.Write(buffer);
            }
#endif

            public override void WriteByte(byte value)
            {
                CheckWrite(1);
                base.WriteByte(value);
            }

            private void CheckWrite(int count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Position > maximumBytes - count)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The NodeSet exceeds the configured byte bound.");
                }
            }
        }
    }
}
