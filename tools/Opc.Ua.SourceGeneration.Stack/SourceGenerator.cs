/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.SourceGeneration
{
    internal static partial class SourceGenerator
    {
        /// <summary>
        /// The namespace prefix for generated code
        /// </summary>
        public const string Name = nameof(StackSourceGenerator);

        public static readonly DiagnosticDescriptor GenericError = new(
            id: "STACKGEN001",
            title: "Error",
            messageFormat: (LocalizableString)"Error during stack generation '{0}'",
            category: Name,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        public static readonly DiagnosticDescriptor GenericWarning = new(
            id: "STACKGEN002",
            title: "Warning",
            messageFormat: (LocalizableString)"Warning during stack generation '{0}'",
            category: Name,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        public static readonly DiagnosticDescriptor Exception = new(
            id: "STACKGEN003",
            title: "Exception",
            messageFormat: (LocalizableString)"Exception during stack generation '{0}': {1}",
            category: Name,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        /// <summary>
        /// A BrowseName from the design model contained characters that
        /// required escaping when interpolated into a generated C# string
        /// literal. See the matching descriptor in the model-side
        /// SourceGenerator for the full rationale.
        /// </summary>
        public static readonly DiagnosticDescriptor BrowseNameUnsafe = new(
            id: "STACKGEN020",
            title: "BrowseName requires C# string-literal escaping (UASG_BROWSENAME_UNSAFE)",
            messageFormat: (LocalizableString)("BrowseName '{0}' contained characters that " +
                "required escaping when emitted into a C# string literal. The generator " +
                "escaped the value and continued; clean up the design XML to remove the " +
                "warning."),
            category: Name,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        /// <summary>
        /// Get diagnostic descriptor for event id
        /// </summary>
        public static bool TryGetDiagnostic(
            LogLevel logLevel,
            EventId eventId,
            out DiagnosticDescriptor descriptor)
        {
            descriptor = eventId.Id switch
            {
                1 => GenericError,
                2 => GenericWarning,
                3 => Exception,
                20 => BrowseNameUnsafe,
                _ => logLevel switch
                {
                    // Critical is reported as enabled by the generator's logger,
                    // so it has to map to a diagnostic or the message is dropped.
                    LogLevel.Critical => GenericError,
                    LogLevel.Error => GenericError,
                    LogLevel.Warning => GenericWarning,
                    _ => null
                }
            };
            return descriptor != null;
        }
    }
}
