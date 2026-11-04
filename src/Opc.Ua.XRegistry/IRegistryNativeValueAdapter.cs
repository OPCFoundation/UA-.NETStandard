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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Maps generic values of adapter-owned native DataTypes, such as typed schema content, that are not
    /// compiled records. The mapping engine selects the concrete DataType; the adapter must return exactly
    /// that DataType or throw, and must never drop or reinterpret source content.
    /// </summary>
    public interface IRegistryNativeValueAdapter
    {
        /// <summary>
        /// Gets the concrete catalog DataType names that this adapter owns.
        /// </summary>
        ArrayOf<string> DataTypes { get; }

        /// <summary>
        /// Projects a generic source value to the owned native DataType <paramref name="dataType"/>.
        /// </summary>
        IEncodeable Project(RegistryValueDataType value, string dataType, RegistryRecordMapper mapper);

        /// <summary>
        /// Restores the exact generic source value of an owned native value of DataType
        /// <paramref name="dataType"/>.
        /// </summary>
        RegistryValueDataType Restore(IEncodeable value, string dataType, RegistryRecordMapper mapper);
    }

    /// <summary>
    /// A representation error of a generic document or native record, with the exact member names and
    /// array positions that lead to the offending value.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A mapping error always carries a status and a document path.")]
    [SuppressMessage(
        "Usage",
        "RCS1194:Implement exception constructors",
        Justification = "A mapping error always carries a status and a document path.")]
    public sealed class RegistryRecordMappingException : ServiceResultException, IRegistryDiagnosticSource
    {
        /// <summary>
        /// Initializes a mapping error.
        /// </summary>
        public RegistryRecordMappingException(StatusCode statusCode, string message, ArrayOf<string> path)
            : base(statusCode, message)
        {
            Path = path;
        }

        /// <summary>
        /// Initializes a mapping error that wraps the failure of a value adapter or of a generated encoding.
        /// </summary>
        public RegistryRecordMappingException(
            StatusCode statusCode,
            string message,
            ArrayOf<string> path,
            Exception innerException)
            : base(statusCode, message, innerException)
        {
            Path = path;
        }

        /// <summary>
        /// Gets the member names and decimal array positions from the root to the offending value.
        /// </summary>
        public ArrayOf<string> Path { get; }

        /// <summary>
        /// Creates the typed diagnostic. Representation errors use the stable code <c>E_NATIVE_INPUT</c>.
        /// </summary>
        public RegistryDiagnosticDataType ToDiagnostic()
        {
            return new RegistryDiagnosticDataType
            {
                StatusCode = StatusCode,
                Code = "E_NATIVE_INPUT",
                Path = Path,
                Detail = Message
            };
        }
    }
}
