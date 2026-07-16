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

using Microsoft.CodeAnalysis;

namespace Opc.Ua.MigrationAnalyzer.Generator
{
    /// <summary>
    /// Diagnostic descriptors emitted by <see cref="MigrationGenerator"/>.
    /// </summary>
    /// <remarks>
    /// These ids use the <c>MIG</c> prefix to distinguish them from the analyzer's
    /// <c>UA00xx</c> rules. Today the generator emits exactly one diagnostic; the
    /// surface is centralized so future generator-time hints can land here without
    /// touching the main pipeline.
    /// </remarks>
    internal static class GeneratorDiagnostics
    {
        public const string Category = "OpcUa.Migration";

        /// <summary>
        /// MIG01 — fires when the generator detects an unresolvable
        /// <c>&lt;Foo&gt;Collection</c> identifier and cannot find a unique element
        /// type <c>Foo</c> in the consumer's compilation. The user is told to either
        /// add a <c>using</c> for the namespace defining <c>Foo</c> or migrate the
        /// site manually.
        /// </summary>
        public static readonly DiagnosticDescriptor UnresolvableElementType = new(
            id: "MIG01",
            title: "Cannot resolve collection element type for migration shim",
            messageFormat: "Cannot resolve element type '{0}' for legacy wrapper '{1}'. " +
                "Add a 'using' for the namespace that defines '{0}' so the generator " +
                "can synthesize the [Obsolete] shim, or migrate the reference manually " +
                "to 'List<{0}>' / 'ArrayOf<{0}>'.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The OPC UA migration source generator emits an internal " +
                "[Obsolete] shim subclass of List<T> for every legacy <Type>Collection " +
                "wrapper the consumer references. When the element type can't be " +
                "uniquely resolved in the consumer compilation, the shim cannot be " +
                "synthesized.",
            helpLinkUri: "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md#collection-type-migration",
            customTags: WellKnownDiagnosticTags.Telemetry);
    }
}
