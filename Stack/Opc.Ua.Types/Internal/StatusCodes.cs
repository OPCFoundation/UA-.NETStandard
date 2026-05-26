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

namespace Opc.Ua.Types
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    /// <exclude />
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class StatusCodes
    {
        /// <summary>
        /// The operation completed successfully.
        /// </summary>
        public static readonly StatusCode Good = new(0x00000000, nameof(Good));

        /// <summary>
        /// The operation completed however its outputs may not be usable.
        /// </summary>
        public static readonly StatusCode Uncertain = new(0x40000000, nameof(Uncertain));

        /// <summary>
        /// The operation failed.
        /// </summary>
        public static readonly StatusCode Bad = new(0x80000000, nameof(Bad));

        /// <summary>
        /// An unexpected error occurred.
        /// </summary>
        public static readonly StatusCode BadUnexpectedError = new(0x80010000, nameof(BadUnexpectedError));

        /// <summary>
        /// Encoding halted because of invalid data in the objects being serialized.
        /// </summary>
        public static readonly StatusCode BadEncodingError = new(0x80060000, nameof(BadEncodingError));

        /// <summary>
        /// Decoding halted because of invalid data in the stream.
        /// </summary>
        public static readonly StatusCode BadDecodingError = new(0x80070000, nameof(BadDecodingError));

        /// <summary>
        /// The message encoding/decoding limits imposed by the stack have been exceeded.
        /// </summary>
        public static readonly StatusCode BadEncodingLimitsExceeded = new(0x80080000, nameof(BadEncodingLimitsExceeded));

        /// <summary>
        /// User does not have permission to perform the requested operation.
        /// </summary>
        public static readonly StatusCode BadUserAccessDenied = new(0x801F0000, nameof(BadUserAccessDenied));

        /// <summary>
        /// Too many arguments were provided.
        /// </summary>
        public static readonly StatusCode BadTooManyArguments = new(0x80E50000, nameof(BadTooManyArguments));

        /// <summary>
        /// Waiting for the server to obtain values from the underlying data source.
        /// </summary>
        public static readonly StatusCode BadWaitingForInitialData = new(0x80320000, nameof(BadWaitingForInitialData));

        /// <summary>
        /// The syntax the node id is not valid or refers to a node that is not valid for the operation.
        /// </summary>
        public static readonly StatusCode BadNodeIdInvalid = new(0x80330000, nameof(BadNodeIdInvalid));

        /// <summary>
        /// The node id refers to a node that does not exist in the server address space.
        /// </summary>
        public static readonly StatusCode BadNodeIdUnknown = new(0x80340000, nameof(BadNodeIdUnknown));

        /// <summary>
        /// The attribute is not supported for the specified Node.
        /// </summary>
        public static readonly StatusCode BadAttributeIdInvalid = new(0x80350000, nameof(BadAttributeIdInvalid));

        /// <summary>
        /// The syntax of the index range parameter is invalid.
        /// </summary>
        public static readonly StatusCode BadIndexRangeInvalid = new(0x80360000, nameof(BadIndexRangeInvalid));

        /// <summary>
        /// No data exists within the range of indexes specified.
        /// </summary>
        public static readonly StatusCode BadIndexRangeNoData = new(0x80370000, nameof(BadIndexRangeNoData));

        /// <summary>
        /// The data encoding is invalid.
        /// </summary>
        public static readonly StatusCode BadDataEncodingInvalid = new(0x80380000, nameof(BadDataEncodingInvalid));

        /// <summary>
        /// The server does not support the requested data encoding for the node.
        /// </summary>
        public static readonly StatusCode BadDataEncodingUnsupported = new(0x80390000, nameof(BadDataEncodingUnsupported));

        /// <summary>
        /// The access level does not allow reading or subscribing to the Node.
        /// </summary>
        public static readonly StatusCode BadNotReadable = new(0x803A0000, nameof(BadNotReadable));

        /// <summary>
        /// The access level does not allow writing to the Node.
        /// </summary>
        public static readonly StatusCode BadNotWritable = new(0x803B0000, nameof(BadNotWritable));

        /// <summary>
        /// The requested operation is not supported.
        /// </summary>
        public static readonly StatusCode BadNotSupported = new(0x803D0000, nameof(BadNotSupported));

        /// <summary>
        /// Requested operation is not implemented.
        /// </summary>
        public static readonly StatusCode BadNotImplemented = new(0x80400000, nameof(BadNotImplemented));

        /// <summary>
        /// The configuration is bad
        /// </summary>
        public static readonly StatusCode BadConfigurationError = new(0x80890000, nameof(BadConfigurationError));

        /// <summary>
        /// A mandatory structured parameter was missing or null.
        /// </summary>
        public static readonly StatusCode BadStructureMissing = new(0x80460000, nameof(BadStructureMissing));

        /// <summary>
        /// The browse name is invalid.
        /// </summary>
        public static readonly StatusCode BadBrowseNameInvalid = new(0x80600000, nameof(BadBrowseNameInvalid));

        /// <summary>
        /// The server does not support writing the combination of value, status and timestamps provided.
        /// </summary>
        public static readonly StatusCode BadWriteNotSupported = new(0x80730000, nameof(BadWriteNotSupported));

        /// <summary>
        /// The value supplied for the attribute is not of the same type as the attribute's value.
        /// </summary>
        public static readonly StatusCode BadTypeMismatch = new(0x80740000, nameof(BadTypeMismatch));

        /// <summary>
        /// The client did not specify all of the input arguments for the method.
        /// </summary>
        public static readonly StatusCode BadArgumentsMissing = new(0x80760000, nameof(BadArgumentsMissing));

        /// <summary>
        /// The executable attribute does not allow the execution of the method.
        /// </summary>
        public static readonly StatusCode BadNotExecutable = new(0x81110000, nameof(BadNotExecutable));

        /// <summary>
        /// One or more arguments are invalid.
        /// </summary>
        public static readonly StatusCode BadInvalidArgument = new(0x80AB0000, nameof(BadInvalidArgument));

        /// <summary>
        /// A value had an invalid syntax.
        /// </summary>
        public static readonly StatusCode BadSyntaxError = new(0x80B60000, nameof(BadSyntaxError));
    }
}
