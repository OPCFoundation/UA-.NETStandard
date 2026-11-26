// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Shared helpers for the source generator entry points. The file is
    /// linked into every generator assembly, each of which supplies its own
    /// diagnostic descriptors in the other half of the partial class.
    /// </summary>
    internal static partial class SourceGenerator
    {
        /// <summary>
        /// Reads generated UTF-8 output without copying the VFS bytes or a whole-file string.
        /// Preserves the leading BOM character emitted by the former GetString path.
        /// </summary>
        internal static SourceText ReadGeneratedSource(IFileSystem fileSystem, string path)
        {
            using Stream stream = fileSystem.OpenRead(path);
            using var reader = new StreamReader(stream, s_utf8WithoutPreamble, detectEncodingFromByteOrderMarks: false);
            // Roslyn uses length as a chunk-capacity estimate. UTF-8 byte length
            // bounds the character count without a second decoding pass.
            return SourceText.From(reader, checked((int)stream.Length), Encoding.UTF8);
        }

        /// <summary>
        /// Runs a source output callback and converts an unhandled exception
        /// into a diagnostic. Roslyn fails the whole compilation with a bare
        /// stack trace when a generator throws, so every callback funnels
        /// through here to report a well-formed diagnostic instead.
        /// </summary>
        public static void Guard(SourceProductionContext context, Action action)
        {
            try
            {
                action();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // ex.ToString() rather than ex.StackTrace: it carries the exception
                // type and the inner-exception chain, and is never null - a bare
                // StackTrace is null for an exception that never left its throw site.
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Exception,
                        Location.None,
                        ex.Message,
                        ex.ToString()));
            }
        }

        /// <summary>
        /// Creates a diagnostic that renders an already fully-formatted message
        /// verbatim under the identity of <paramref name="descriptor"/>. The
        /// looked-up descriptor's <c>messageFormat</c> may declare more
        /// positional placeholders than the single formatted string carries
        /// (for example the exception descriptor declares <c>{0}</c> and
        /// <c>{1}</c>); reporting the message directly against it makes
        /// <see cref="Diagnostic.GetMessage(System.IFormatProvider)"/> throw a
        /// <see cref="System.FormatException"/> internally and fall back to the
        /// raw, unsubstituted template. Reporting through a passthrough
        /// descriptor whose format is exactly <c>"{0}"</c> avoids that, is
        /// independent of the original placeholder arity, and is safe even when
        /// the message itself contains <c>{</c> or <c>}</c> characters. All
        /// other descriptor metadata is preserved.
        /// </summary>
        public static Diagnostic CreateFormattedDiagnostic(
            DiagnosticDescriptor descriptor,
            string message)
        {
            var passthrough = new DiagnosticDescriptor(
                descriptor.Id,
                descriptor.Title,
                "{0}",
                descriptor.Category,
                descriptor.DefaultSeverity,
                descriptor.IsEnabledByDefault,
                description: descriptor.Description,
                helpLinkUri: descriptor.HelpLinkUri,
                customTags: descriptor.CustomTags.ToArray());
            return Diagnostic.Create(passthrough, Location.None, message);
        }

        /// <summary>
        /// Attaches a debugger to the compiler process hosting the generator.
        /// Compiled away unless the generator is built with the DEBUGX
        /// constant defined.
        /// </summary>
        [Conditional("DEBUGX")]
        public static void AttachDebuggerIfRequested()
        {
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
        }

        private static readonly Encoding s_utf8WithoutPreamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
