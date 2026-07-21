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
 *
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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Severity of a <see cref="WotDiagnostic"/>.
    /// </summary>
    public enum WotDiagnosticSeverity
    {
        /// <summary>An informational note; conversion succeeded.</summary>
        Info,

        /// <summary>A recoverable concern; conversion succeeded with caveats.</summary>
        Warning,

        /// <summary>A fatal problem; the associated conversion did not succeed.</summary>
        Error
    }

    /// <summary>
    /// Stable diagnostic codes emitted by the WoT/NodeSet conversion.
    /// </summary>
    public enum WotDiagnosticCode
    {
        /// <summary>No specific code.</summary>
        None = 0,

        /// <summary>The JSON document exceeded the configured byte limit.</summary>
        JsonDocumentTooLarge = 1000,

        /// <summary>The NodeSet2 payload exceeded the configured byte limit.</summary>
        NodeSetTooLarge = 1001,

        /// <summary>A nesting depth limit was exceeded.</summary>
        DepthExceeded = 1002,

        /// <summary>The node count limit was exceeded.</summary>
        NodeCountExceeded = 1003,

        /// <summary>The affordance count limit was exceeded.</summary>
        AffordanceCountExceeded = 1004,

        /// <summary>The JSON document was malformed.</summary>
        MalformedJson = 1005,

        /// <summary>The NodeSet2 XML was malformed.</summary>
        MalformedNodeSet = 1006,

        /// <summary>The preservation envelope is missing.</summary>
        EnvelopeMissing = 2000,

        /// <summary>The preservation envelope is structurally invalid.</summary>
        EnvelopeInvalid = 2001,

        /// <summary>The envelope content type is not supported.</summary>
        UnsupportedContentType = 2002,

        /// <summary>The envelope encoding is not supported.</summary>
        UnsupportedEncoding = 2003,

        /// <summary>The envelope data was not valid base64.</summary>
        InvalidBase64 = 2004,

        /// <summary>The envelope digest was not a valid SHA-256 value.</summary>
        InvalidDigest = 2005,

        /// <summary>The envelope digest did not match the decoded payload.</summary>
        DigestMismatch = 2006,

        /// <summary>A native member restated a baseline fact inconsistently.</summary>
        NativeProjectionConflict = 3000,

        /// <summary>Neither an envelope nor a native projection was present.</summary>
        NoConvertibleContent = 3001,

        /// <summary>A native projection record was structurally invalid.</summary>
        NativeProjectionInvalid = 3002,

        /// <summary>A referenced target could not be resolved to a NodeId.</summary>
        UnresolvedReference = 4000,

        /// <summary>A NodeId was generated deterministically because none was supplied.</summary>
        GeneratedNodeId = 4001,

        /// <summary>A WoT construct had no faithful NodeSet2 representation.</summary>
        LossySynthesis = 4002,

        /// <summary>A required BrowseName or title was missing.</summary>
        MissingBrowseName = 4003,

        /// <summary>A DataSchema could not be mapped to an OPC UA DataType.</summary>
        UnsupportedSchema = 4004,

        /// <summary>External resolution detected a cycle.</summary>
        ResolverCycle = 5000,

        /// <summary>External resolution exceeded the configured depth.</summary>
        ResolverDepthExceeded = 5001,

        /// <summary>External resolution exceeded a configured resource limit.</summary>
        ResolverLimitExceeded = 5002,

        /// <summary>An external document could not be resolved.</summary>
        ResolverNotFound = 5003,

        /// <summary>A document validation rule was violated.</summary>
        ValidationError = 6000
    }

    /// <summary>
    /// Locates a diagnostic within a WoT document and/or a NodeSet2 document.
    /// </summary>
    public sealed class WotLocation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotLocation"/> class.
        /// </summary>
        /// <param name="jsonPointer">An RFC 6901 JSON Pointer into the WoT document.</param>
        /// <param name="nodeId">An OPC UA NodeId string.</param>
        /// <param name="attribute">An OPC UA attribute name.</param>
        /// <param name="reference">A reference descriptor (type and target).</param>
        public WotLocation(
            string? jsonPointer = null,
            string? nodeId = null,
            string? attribute = null,
            string? reference = null)
        {
            JsonPointer = jsonPointer;
            NodeId = nodeId;
            Attribute = attribute;
            Reference = reference;
        }

        /// <summary>Gets the RFC 6901 JSON Pointer of the location, if any.</summary>
        public string? JsonPointer { get; }

        /// <summary>Gets the OPC UA NodeId of the location, if any.</summary>
        public string? NodeId { get; }

        /// <summary>Gets the OPC UA attribute name of the location, if any.</summary>
        public string? Attribute { get; }

        /// <summary>Gets the reference descriptor of the location, if any.</summary>
        public string? Reference { get; }

        /// <summary>Creates a location from a JSON Pointer.</summary>
        public static WotLocation FromPointer(string jsonPointer)
        {
            return new WotLocation(jsonPointer: jsonPointer);
        }

        /// <summary>Creates a location from a NodeId and optional attribute.</summary>
        public static WotLocation FromNode(string nodeId, string? attribute = null)
        {
            return new WotLocation(nodeId: nodeId, attribute: attribute);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var builder = new StringBuilder();
            Append(builder, nameof(JsonPointer), JsonPointer);
            Append(builder, nameof(NodeId), NodeId);
            Append(builder, nameof(Attribute), Attribute);
            Append(builder, nameof(Reference), Reference);
            return builder.Length == 0 ? "(document)" : builder.ToString();

            static void Append(StringBuilder builder, string name, string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(name).Append('=').Append(value);
            }
        }
    }

    /// <summary>
    /// A single structured conversion diagnostic.
    /// </summary>
    public sealed class WotDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotDiagnostic"/> class.
        /// </summary>
        /// <param name="severity">The severity of the diagnostic.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="message">A human-readable message.</param>
        /// <param name="location">The optional location of the diagnostic.</param>
        public WotDiagnostic(
            WotDiagnosticSeverity severity,
            WotDiagnosticCode code,
            string message,
            WotLocation? location = null)
        {
            Severity = severity;
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Location = location;
        }

        /// <summary>Gets the severity of the diagnostic.</summary>
        public WotDiagnosticSeverity Severity { get; }

        /// <summary>Gets the stable diagnostic code.</summary>
        public WotDiagnosticCode Code { get; }

        /// <summary>Gets the human-readable message.</summary>
        public string Message { get; }

        /// <summary>Gets the optional location of the diagnostic.</summary>
        public WotLocation? Location { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} WOT{1:D4}: {2}{3}",
                Severity,
                (int)Code,
                Message,
                Location is null ? string.Empty : " [" + Location + "]");
        }
    }

    /// <summary>
    /// The outcome of a WoT/NodeSet conversion: an optional value together
    /// with the structured diagnostics that describe how it was produced.
    /// </summary>
    /// <typeparam name="T">The type of the produced value.</typeparam>
    public sealed class WotConversionResult<T>
        where T : class
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WotConversionResult{T}"/> class.
        /// </summary>
        /// <param name="value">The produced value, or <c>null</c> on failure.</param>
        /// <param name="diagnostics">The diagnostics produced.</param>
        public WotConversionResult(T? value, IReadOnlyList<WotDiagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>Gets the produced value, or <c>null</c> when conversion failed.</summary>
        public T? Value { get; }

        /// <summary>Gets the diagnostics produced during conversion.</summary>
        public IReadOnlyList<WotDiagnostic> Diagnostics { get; }

        /// <summary>Gets a value indicating whether any error diagnostic was produced.</summary>
        public bool HasErrors
        {
            get
            {
                for (int ii = 0; ii < Diagnostics.Count; ii++)
                {
                    if (Diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether conversion produced a usable value
        /// without any error diagnostic.
        /// </summary>
        public bool Success => Value is not null && !HasErrors;
    }
}
