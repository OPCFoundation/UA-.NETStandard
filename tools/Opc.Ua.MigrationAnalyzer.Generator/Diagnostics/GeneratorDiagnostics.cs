// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// type <c>Foo</c> in the consumer's source declarations or the supported
        /// standard metadata namespaces. The user is told to migrate the site
        /// manually or define the wrapper explicitly.
        /// </summary>
        public static readonly DiagnosticDescriptor UnresolvableElementType = new(
            id: "MIG01",
            title: "Cannot resolve collection element type for migration shim",
            messageFormat: "Cannot resolve a unique element type '{0}' for legacy wrapper '{1}'. " +
                "The generator discovers consumer source declarations plus exact System.<Type> " +
                "or Opc.Ua.<Type> metadata names only. Migrate the reference manually to the " +
                "intended List<T> / ArrayOf<T>, or define the wrapper type explicitly.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The OPC UA migration source generator emits a public " +
                "[Obsolete] shim subclass of List<T> for every legacy <Type>Collection " +
                "wrapper the consumer references. It resolves consumer source declarations " +
                "by short name and metadata types named exactly System.<Type> or Opc.Ua.<Type>. " +
                "When the element type can't be uniquely resolved, the shim cannot be synthesized.",
            helpLinkUri:
                "https://github.com/OPCFoundation/UA-.NETStandard/blob/master/" +
                "docs/migrate/2.0.x/source-generation.md" +
                "#mig01-resolution-playbook",
            customTags: WellKnownDiagnosticTags.Telemetry);
    }
}
