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
using System.Linq;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Checks projection-specific metadata and plan shape without resolving sources
    /// or claiming ordinary TD/TM or native compatibility validation.
    /// </summary>
    internal static class WotProjectionAdmission
    {
        public static string? GetError(
            WotDocument document,
            WoTDocumentKindEnum kind,
            string format,
            string contentType,
            WotProjectionCompatibilityMode compatibilityMode)
        {
            bool projection = WotProjection.IsProjection(document);
            bool projectionContentType = string.Equals(contentType, WotProjection.ContentType, StringComparison.Ordinal);
            if (!projection && !UsesProjectionFormat(format, contentType))
            {
                return null;
            }

            if (!projection)
            {
                return "The projection format requires an unresolved plan carrying the uav:projection role.";
            }
            if (!string.Equals(format, WotProjection.Format, StringComparison.Ordinal) || !projectionContentType)
            {
                return $"An unresolved projection requires Format '{WotProjection.Format}' and " +
                    $"ContentType '{WotProjection.ContentType}', not ordinary TD/TM admission.";
            }

            var diagnostics = new List<WotDiagnostic>();
            var parsed = WotProjection.Parse(document, diagnostics, compatibilityMode);
            if (diagnostics.Any(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error))
            {
                return string.Join("; ", diagnostics);
            }
            WotDocumentKind expected = kind switch
            {
                WoTDocumentKindEnum.ThingDescription => WotDocumentKind.ThingDescription,
                WoTDocumentKindEnum.ThingModel => WotDocumentKind.ThingModel,
                _ => WotDocumentKind.Unknown
            };
            if (parsed is null || expected == WotDocumentKind.Unknown || parsed.ResultKind != expected)
            {
                return "The projection result kind must match the resource's stored TD/TM kind.";
            }
            return null;
        }

        public static bool UsesProjectionFormat(string format, string contentType)
        {
            return format.StartsWith("WoT-Projection/", StringComparison.Ordinal) ||
                string.Equals(contentType, WotProjection.ContentType, StringComparison.Ordinal);
        }
    }
}
