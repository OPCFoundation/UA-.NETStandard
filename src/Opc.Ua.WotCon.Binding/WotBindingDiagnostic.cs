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
using System.Globalization;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Stable diagnostic codes emitted while validating and compiling a WoT
    /// interaction form into a binding plan. Codes are grouped by concern so
    /// operators can filter without matching on message text.
    /// </summary>
    public enum WotBindingDiagnosticCode
    {
        /// <summary>No specific code.</summary>
        None = 0,

        /// <summary>The form has no <c>href</c>.</summary>
        MissingHref = 7000,

        /// <summary>The <c>href</c> is not a valid URI for this binding.</summary>
        InvalidHref = 7001,

        /// <summary>The <c>href</c> scheme is not handled by this binding.</summary>
        UnsupportedScheme = 7002,

        /// <summary>The requested <c>op</c> is not compatible with the affordance kind.</summary>
        IncompatibleOperation = 7003,

        /// <summary>The requested <c>op</c> is not supported by this binding.</summary>
        UnsupportedOperation = 7004,

        /// <summary>The <c>contentType</c> is missing where the binding requires it.</summary>
        MissingContentType = 7005,

        /// <summary>The <c>contentType</c> is not supported by this binding.</summary>
        UnsupportedContentType = 7006,

        /// <summary>A required binding-specific field is missing.</summary>
        MissingRequiredField = 7007,

        /// <summary>A binding-specific field has an invalid value.</summary>
        InvalidFieldValue = 7008,

        /// <summary>A vocabulary term is not defined by the pinned document.</summary>
        UnknownVocabularyTerm = 7009,

        /// <summary>Two fields conflict and cannot both be honoured.</summary>
        ConflictingFields = 7010,

        /// <summary>A referenced security scheme is not declared by the document.</summary>
        UnknownSecurityScheme = 7011,

        /// <summary>
        /// The binding validates and compiles but has no runtime executor, so the
        /// affordance is materialized as non-executable.
        /// </summary>
        NonExecutableBinding = 7012,

        /// <summary>A value exceeded a configured safety bound.</summary>
        BoundsExceeded = 7013,

        /// <summary>An informational note about how the form was interpreted.</summary>
        Informational = 7014
    }

    /// <summary>
    /// A single structured diagnostic produced while validating or compiling a
    /// binding form. Every diagnostic carries a severity, a stable code and an
    /// RFC 6901 JSON Pointer into the originating Thing Description / Thing Model
    /// so callers can locate the offending term precisely.
    /// </summary>
    public sealed class WotBindingDiagnostic
    {
        /// <summary>Initializes a new immutable binding diagnostic.</summary>
        /// <param name="severity">The severity of the diagnostic.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="message">A human-readable message.</param>
        /// <param name="jsonPointer">The RFC 6901 JSON Pointer into the document.</param>
        /// <param name="term">The offending vocabulary term, if any.</param>
        public WotBindingDiagnostic(
            WotDiagnosticSeverity severity,
            WotBindingDiagnosticCode code,
            string message,
            string? jsonPointer = null,
            string? term = null)
        {
            Severity = severity;
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            JsonPointer = jsonPointer;
            Term = term;
        }

        /// <summary>Gets the severity of the diagnostic.</summary>
        public WotDiagnosticSeverity Severity { get; }

        /// <summary>Gets the stable diagnostic code.</summary>
        public WotBindingDiagnosticCode Code { get; }

        /// <summary>Gets the human-readable message.</summary>
        public string Message { get; }

        /// <summary>Gets the RFC 6901 JSON Pointer into the document, if any.</summary>
        public string? JsonPointer { get; }

        /// <summary>Gets the offending vocabulary term, if any.</summary>
        public string? Term { get; }

        /// <summary>Gets whether this diagnostic is an error.</summary>
        public bool IsError => Severity == WotDiagnosticSeverity.Error;

        /// <summary>Creates an error diagnostic.</summary>
        public static WotBindingDiagnostic Error(
            WotBindingDiagnosticCode code, string message, string? jsonPointer = null, string? term = null)
            => new WotBindingDiagnostic(WotDiagnosticSeverity.Error, code, message, jsonPointer, term);

        /// <summary>Creates a warning diagnostic.</summary>
        public static WotBindingDiagnostic Warning(
            WotBindingDiagnosticCode code, string message, string? jsonPointer = null, string? term = null)
            => new WotBindingDiagnostic(WotDiagnosticSeverity.Warning, code, message, jsonPointer, term);

        /// <summary>Creates an informational diagnostic.</summary>
        public static WotBindingDiagnostic Info(
            WotBindingDiagnosticCode code, string message, string? jsonPointer = null, string? term = null)
            => new WotBindingDiagnostic(WotDiagnosticSeverity.Info, code, message, jsonPointer, term);

        /// <summary>Projects this diagnostic onto the shared <see cref="WotDiagnostic"/> model.</summary>
        public WotDiagnostic ToWotDiagnostic()
        {
            WotLocation? location = JsonPointer is null ? null : WotLocation.FromPointer(JsonPointer);
            return new WotDiagnostic(Severity, WotDiagnosticCode.ValidationError, Message, location);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} WOTB{1:D4}: {2}{3}",
                Severity,
                (int)Code,
                Message,
                JsonPointer is null ? string.Empty : " [" + JsonPointer + "]");
        }
    }
}
