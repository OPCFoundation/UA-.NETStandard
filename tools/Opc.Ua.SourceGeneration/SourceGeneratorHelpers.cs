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
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;

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
    }
}
