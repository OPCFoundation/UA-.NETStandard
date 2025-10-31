/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    /// <exclude />
    public static partial class StatusCodes
    {
        /// <summary>
        /// An unexpected error occurred.
        /// </summary>
        public const uint BadUnexpectedError = 0x80010000;

        /// <summary>
        /// Encoding halted because of invalid data in the objects being serialized.
        /// </summary>
        public const uint BadEncodingError = 0x80060000;

        /// <summary>
        /// Decoding halted because of invalid data in the stream.
        /// </summary>
        public const uint BadDecodingError = 0x80070000;

        /// <summary>
        /// The message encoding/decoding limits imposed by the stack have been exceeded.
        /// </summary>
        public const uint BadEncodingLimitsExceeded = 0x80080000;

        /// <summary>
        /// User does not have permission to perform the requested operation.
        /// </summary>
        public const uint BadUserAccessDenied = 0x801F0000;

        /// <summary>
        /// Waiting for the server to obtain values from the underlying data source.
        /// </summary>
        public const uint BadWaitingForInitialData = 0x80320000;

        /// <summary>
        /// The syntax the node id is not valid or refers to a node that is not valid for the operation.
        /// </summary>
        public const uint BadNodeIdInvalid = 0x80330000;

        /// <summary>
        /// The node id refers to a node that does not exist in the server address space.
        /// </summary>
        public const uint BadNodeIdUnknown = 0x80340000;

        /// <summary>
        /// The attribute is not supported for the specified Node.
        /// </summary>
        public const uint BadAttributeIdInvalid = 0x80350000;

        /// <summary>
        /// The syntax of the index range parameter is invalid.
        /// </summary>
        public const uint BadIndexRangeInvalid = 0x80360000;

        /// <summary>
        /// No data exists within the range of indexes specified.
        /// </summary>
        public const uint BadIndexRangeNoData = 0x80370000;

        /// <summary>
        /// The data encoding is invalid.
        /// </summary>
        public const uint BadDataEncodingInvalid = 0x80380000;

        /// <summary>
        /// The server does not support the requested data encoding for the node.
        /// </summary>
        public const uint BadDataEncodingUnsupported = 0x80390000;

        /// <summary>
        /// The access level does not allow reading or subscribing to the Node.
        /// </summary>
        public const uint BadNotReadable = 0x803A0000;

        /// <summary>
        /// The access level does not allow writing to the Node.
        /// </summary>
        public const uint BadNotWritable = 0x803B0000;

        /// <summary>
        /// The requested operation is not supported.
        /// </summary>
        public const uint BadNotSupported = 0x803D0000;

        /// <summary>
        /// A mandatory structured parameter was missing or null.
        /// </summary>
        public const uint BadStructureMissing = 0x80460000;

        /// <summary>
        /// The browse name is invalid.
        /// </summary>
        public const uint BadBrowseNameInvalid = 0x80600000;

        /// <summary>
        /// The server does not support writing the combination of value, status and timestamps provided.
        /// </summary>
        public const uint BadWriteNotSupported = 0x80730000;

        /// <summary>
        /// The value supplied for the attribute is not of the same type as the attribute's value.
        /// </summary>
        public const uint BadTypeMismatch = 0x80740000;
    }
}
