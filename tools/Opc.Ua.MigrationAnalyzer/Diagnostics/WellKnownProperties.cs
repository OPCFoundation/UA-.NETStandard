// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.MigrationAnalyzer.Diagnostics
{
    /// <summary>
    /// Shared <c>DiagnosticDescriptor.Properties</c> keys passed from analyzers
    /// to their companion code-fix providers.
    /// </summary>
    /// <remarks>
    /// Defined here (in the analyzer DLL) and linked into the companion CodeFixes
    /// project so both assemblies can use the exact same string constants without
    /// the CodeFixes assembly needing a ProjectReference back to the analyzer
    /// (which would create a NuGet restore cycle).
    /// </remarks>
    internal static class WellKnownProperties
    {
        /// <summary>UA0008: method name extracted from a <c>Session.Call</c> invocation.</summary>
        public const string MethodName = "MethodName";

        /// <summary>UA0020: form discriminator key for <c>EncodeableFactory</c> rename.</summary>
        public const string Form = "Form";

        /// <summary>UA0020 form: legacy <c>EncodeableFactory.GlobalFactory</c> getter.</summary>
        public const string FormGlobalFactory = "A";

        /// <summary>UA0020 form: legacy <c>factory.Create()</c> instance call.</summary>
        public const string FormCreate = "B";
    }
}
