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

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// An AMB field together with its actual source and unmodified DataValue.
    /// </summary>
    public sealed record AssetReadValue
    {
        /// <summary>
        /// Creates a field without normalizing its value, quality or timestamps.
        /// </summary>
        public AssetReadValue(
            NodeId sourceNodeId,
            ArrayOf<QualifiedName> browsePath,
            uint attributeId,
            in DataValue value)
        {
            SourceNodeId = sourceNodeId;
            BrowsePath = browsePath;
            AttributeId = attributeId;
            Value = value;
        }

        /// <summary>
        /// Gets the local variable from which the attribute was read.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <summary>
        /// Gets the field path, relative to its containing asset or condition.
        /// For directly browsed variables this is their browse name.
        /// A non-Good read rejected before field mapping may have an empty path;
        /// its SourceNodeId and AttributeId still identify the failed read.
        /// </summary>
        public ArrayOf<QualifiedName> BrowsePath { get; }

        /// <summary>
        /// Gets the attribute, usually Value or UserAccessLevel.
        /// </summary>
        public uint AttributeId { get; }

        /// <summary>
        /// Gets the unmodified server value, including quality and both timestamps.
        /// In strict mode, an absent optional path has a synthetic BadNoMatch
        /// value instead.
        /// </summary>
        public DataValue Value { get; }

        /// <summary>
        /// Gets whether the optional path did not resolve. A null or empty
        /// server value is not absence when its source exists.
        /// </summary>
        public bool IsAbsent => SourceNodeId.IsNull;
    }

    /// <summary>
    /// Rejects a non-Good or incorrectly typed AMB read without losing its
    /// source, original quality, payload or timestamps.
    /// </summary>
    public sealed class AmbValueReadException : ServiceResultException
    {
        /// <summary>
        /// Creates an unexpected read failure without field evidence.
        /// </summary>
        public AmbValueReadException()
            : base(StatusCodes.BadUnexpectedError)
        {
        }

        /// <summary>
        /// Creates an unexpected read failure with a message.
        /// </summary>
        public AmbValueReadException(string message)
            : base(StatusCodes.BadUnexpectedError, message)
        {
        }

        /// <summary>
        /// Creates an unexpected read failure with its underlying cause.
        /// </summary>
        public AmbValueReadException(string message, Exception innerException)
            : base(StatusCodes.BadUnexpectedError, message, innerException)
        {
        }

        /// <summary>
        /// Creates the failure with its original read evidence. For a type
        /// mismatch, StatusCode is BadTypeMismatch while ReadValue retains
        /// the original server status.
        /// </summary>
        public AmbValueReadException(StatusCode statusCode, AssetReadValue readValue)
            : base(statusCode, "The AMB field could not be read with the expected quality and type.")
        {
            ReadValue = readValue ?? throw new ArgumentNullException(nameof(readValue));
        }

        /// <summary>
        /// Gets the field rejected by strict validation. General-purpose
        /// exception constructors have no field evidence.
        /// </summary>
        public AssetReadValue? ReadValue { get; }
    }
}
