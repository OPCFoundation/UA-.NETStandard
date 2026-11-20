// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// One field's local source, qualified path and unmodified server DataValue.
    /// Evidence belongs to the returned operation, never to the shared client.
    /// </summary>
    public sealed record MachineryReadValue
    {
        /// <summary>
        /// Creates evidence without normalizing the payload, quality or timestamps.
        /// </summary>
        public MachineryReadValue(
            NodeId sourceNodeId,
            NodeId startingNodeId,
            ArrayOf<QualifiedName> browsePath,
            in DataValue value,
            bool isAbsent = false)
        {
            SourceNodeId = sourceNodeId;
            StartingNodeId = startingNodeId;
            BrowsePath = browsePath;
            Value = value;
            IsAbsent = isAbsent;
        }

        /// <summary>
        /// Gets the local Value-attribute source, or NodeId.Null if translation
        /// did not produce a source. StartingNodeId still identifies the request.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <summary>
        /// Gets the node relative to which BrowsePath was resolved or browsed.
        /// </summary>
        public NodeId StartingNodeId { get; }

        /// <summary>
        /// Gets the qualified path, or the browse name for a directly browsed value.
        /// </summary>
        public ArrayOf<QualifiedName> BrowsePath { get; }

        /// <summary>
        /// Gets the original value, including both timestamps and picoseconds.
        /// A path or service failure without a returned value instead carries
        /// synthetic status evidence; an absent optional path carries BadNoMatch.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// Gets whether the optional path or namespace was genuinely absent.
        /// A Good-null server value is present, not absent.
        /// </summary>
        public bool IsAbsent { get; }

        /// <summary>
        /// Gets an optional model namespace that the server did not publish.
        /// No namespace index is invented or appended for this absence.
        /// </summary>
        public string? MissingNamespaceUri { get; init; }
    }

    /// <summary>
    /// Rejects a Machinery read without discarding its status and source evidence.
    /// </summary>
    public sealed class MachineryReadException : ServiceResultException
    {
        /// <summary>
        /// Creates an unexpected read failure without field evidence.
        /// </summary>
        public MachineryReadException()
            : base(StatusCodes.BadUnexpectedError)
        {
        }

        /// <summary>
        /// Creates an unexpected read failure with a message.
        /// </summary>
        public MachineryReadException(string message)
            : base(StatusCodes.BadUnexpectedError, message)
        {
        }

        /// <summary>
        /// Creates an unexpected read failure with its underlying cause.
        /// </summary>
        public MachineryReadException(string message, Exception innerException)
            : base(StatusCodes.BadUnexpectedError, message, innerException)
        {
        }

        /// <summary>
        /// Creates a failure with the offending field. A BadTypeMismatch or
        /// BadDecodingError does not replace the original DataValue quality.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, MachineryReadValue readValue, string message)
            : base(statusCode, message)
        {
            ReadValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        }

        /// <summary>
        /// Creates a failure retaining the service or decoder exception and attempted source.
        /// </summary>
        public MachineryReadException(
            StatusCode statusCode,
            MachineryReadValue readValue,
            string message,
            Exception innerException)
            : base(statusCode, message, innerException)
        {
            ReadValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        }

        /// <summary>
        /// Wraps an underlying exception, retaining its status when one is available.
        /// </summary>
        public MachineryReadException(Exception innerException, StatusCode defaultCode)
            : base(innerException, defaultCode)
        {
        }

        /// <summary>
        /// Creates a failure with the supplied status and no field evidence.
        /// </summary>
        public MachineryReadException(StatusCode statusCode)
            : base(statusCode)
        {
        }

        /// <summary>
        /// Creates a failure with a status and explanatory message.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, string message)
            : base(statusCode, message)
        {
        }

        /// <summary>
        /// Creates a failure with a status and localized message.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, LocalizedText message)
            : base(statusCode, message)
        {
        }

        /// <summary>
        /// Creates a failure retaining its status and underlying exception.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, Exception innerException)
            : base(statusCode, innerException)
        {
        }

        /// <summary>
        /// Creates a failure with a status, message and underlying exception.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, string message, Exception innerException)
            : base(statusCode, message, innerException)
        {
        }

        /// <summary>
        /// Creates a failure with a status, localized message and underlying exception.
        /// </summary>
        public MachineryReadException(StatusCode statusCode, LocalizedText message, Exception innerException)
            : base(statusCode, message, innerException)
        {
        }

        /// <summary>
        /// Creates a failure from an existing service result without changing its status.
        /// </summary>
        public MachineryReadException(ServiceResult status)
            : base(status)
        {
        }

        /// <summary>
        /// Gets the rejected field, when a read request supplied field evidence.
        /// </summary>
        public MachineryReadValue? ReadValue { get; }
    }
}
