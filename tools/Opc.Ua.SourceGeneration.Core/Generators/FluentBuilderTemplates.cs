/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Top-level templates for the <see cref="FluentBuilderGenerator"/>.
    /// The bulk of the generated code is emitted directly via
    /// <see cref="ITemplateWriter"/> calls from the generator (per-class,
    /// per-accessor, per-method-overload bodies); only the file shell and
    /// the boilerplate per-class skeleton live as template strings.
    /// </summary>
    internal static class FluentBuilderTemplates
    {
        /// <summary>
        /// Single output file template. Hosts the typed manager interface
        /// plus every per-type, per-instance and per-method wrapper class
        /// generated for the model.
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            {{Tokens.CodeHeader}}

            #pragma warning disable CS0419 // Ambiguous reference in cref attribute
            #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
            #pragma warning disable CA1707 // Identifiers should not contain underscores
            #pragma warning disable CA1822 // Mark members as static
            #pragma warning disable IDE0008 // Use explicit type
            #pragma warning disable IDE1006 // Naming rule violation

            // Annotations only, matching the shared header. A bare
            // '#nullable enable' would switch the nullable *warnings* back
            // on that the header turned off, and the typed method wrappers
            // would then report CS8600 on every `out T` of a structure
            // argument in a project that builds warnings as errors.
            #nullable enable annotations

            {{Tokens.AssemblyAttributes}}

            namespace {{Tokens.NamespacePrefix}}
            {
                {{Tokens.ListOfTypes}}
            }

            """);
    }
}
