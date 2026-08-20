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
using System.Globalization;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// Severity of a materialization diagnostic.
    /// </summary>
    public enum AasMaterializationDiagnosticSeverity
    {
        /// <summary>
        /// Conversion succeeded with additional information.
        /// </summary>
        Info,

        /// <summary>
        /// Conversion succeeded with a recoverable concern.
        /// </summary>
        Warning,

        /// <summary>
        /// The related identifiable or conversion failed.
        /// </summary>
        Error
    }

    /// <summary>
    /// Stable diagnostic codes emitted by the AAS materializer.
    /// </summary>
    public enum AasMaterializationDiagnosticCode
    {
        /// <summary>
        /// No specific code.
        /// </summary>
        None = 0,

        /// <summary>
        /// A generated String NodeId exceeded the OPC UA limit.
        /// </summary>
        NodeIdTooLong = 1000,

        /// <summary>
        /// Two Identifiables of one kind carried the same identifier.
        /// </summary>
        DuplicateIdentifier = 1001,

        /// <summary>
        /// A required element short name was absent.
        /// </summary>
        MissingIdShort = 1002,

        /// <summary>
        /// A value could not be encoded with its declared type.
        /// </summary>
        InvalidValue = 1003
    }

    /// <summary>
    /// Locates a materialization diagnostic within the source AAS or produced NodeSet.
    /// </summary>
    public sealed class AasMaterializationLocation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasMaterializationLocation"/> class.
        /// </summary>
        /// <param name="kind">The AAS node kind.</param>
        /// <param name="id">The top-level identifier.</param>
        /// <param name="idShortPath">The element path, when applicable.</param>
        /// <param name="nodeId">The generated OPC UA NodeId, when applicable.</param>
        public AasMaterializationLocation(
            AasNodeKind kind,
            string? id = null,
            string? idShortPath = null,
            string? nodeId = null)
        {
            Kind = kind;
            Id = id;
            IdShortPath = idShortPath;
            NodeId = nodeId;
        }

        /// <summary>
        /// Gets the AAS node kind.
        /// </summary>
        public AasNodeKind Kind { get; }

        /// <summary>
        /// Gets the top-level identifier, when applicable.
        /// </summary>
        public string? Id { get; }

        /// <summary>
        /// Gets the element path, when applicable.
        /// </summary>
        public string? IdShortPath { get; }

        /// <summary>
        /// Gets the generated OPC UA NodeId, when applicable.
        /// </summary>
        public string? NodeId { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var parts = new List<string>
            {
                "Kind=" + Kind
            };
            if (!string.IsNullOrEmpty(Id))
            {
                parts.Add("Id=" + Id);
            }
            if (!string.IsNullOrEmpty(IdShortPath))
            {
                parts.Add("Path=" + IdShortPath);
            }
            if (!string.IsNullOrEmpty(NodeId))
            {
                parts.Add("NodeId=" + NodeId);
            }
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// A single structured AAS materialization diagnostic.
    /// </summary>
    public sealed class AasMaterializationDiagnostic
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AasMaterializationDiagnostic"/> class.
        /// </summary>
        /// <param name="severity">The diagnostic severity.</param>
        /// <param name="code">The stable diagnostic code.</param>
        /// <param name="message">The human-readable message.</param>
        /// <param name="location">The optional source or NodeSet location.</param>
        public AasMaterializationDiagnostic(
            AasMaterializationDiagnosticSeverity severity,
            AasMaterializationDiagnosticCode code,
            string message,
            AasMaterializationLocation? location = null)
        {
            Severity = severity;
            Code = code;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Location = location;
        }

        /// <summary>
        /// Gets the diagnostic severity.
        /// </summary>
        public AasMaterializationDiagnosticSeverity Severity { get; }

        /// <summary>
        /// Gets the stable diagnostic code.
        /// </summary>
        public AasMaterializationDiagnosticCode Code { get; }

        /// <summary>
        /// Gets the human-readable message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the optional source or NodeSet location.
        /// </summary>
        public AasMaterializationLocation? Location { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} AASM{1:D4}: {2}{3}",
                Severity,
                (int)Code,
                Message,
                Location is null ? string.Empty : " [" + Location + "]");
        }
    }
}
