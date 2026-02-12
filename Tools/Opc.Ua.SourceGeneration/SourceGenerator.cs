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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.SourceGeneration
{
    internal static class SourceGenerator
    {
        /// <summary>
        /// The namespace prefix for generated code
        /// </summary>
        public const string Name = nameof(ModelSourceGenerator);

        public static readonly DiagnosticDescriptor GenericError = new(
            id: "MODELGEN001",
            title: "Error",
            messageFormat: (LocalizableString)"Error during model generation '{0}'",
            category: Name,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        public static readonly DiagnosticDescriptor GenericWarning = new(
            id: "MODELGEN002",
            title: "Warning",
            messageFormat: (LocalizableString)"Warning during model generation '{0}'",
            category: Name,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "www.opcfoundation.org",
            customTags: ["opcua"]);

        public static readonly DiagnosticDescriptor Exception = new(
            id: "MODELGEN003",
            title: "Exception",
            messageFormat: (LocalizableString)"Exception during model generation '{0}': {1}",
            category: Name,
            DiagnosticSeverity.Error,
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
                _ => logLevel switch
                {
                    LogLevel.Error => GenericError,
                    LogLevel.Warning => GenericWarning,
                    _ => null
                }
            };
            return descriptor != null;
        }
    }
}
