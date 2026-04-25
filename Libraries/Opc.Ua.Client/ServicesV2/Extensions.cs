#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client.Services
{
    using Opc.Ua;
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Service extensions
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// Reads a byte string value safely in fragments if needed. Uses the byte
        /// string size limits to chunk the reads if needed. The first read happens
        /// as usual and no stream is allocated, if the result is below the limits
        /// the buffer that is read into is returned, otherwise buffers are added
        /// to a memory stream whose content is finally returned.
        /// </summary>
        /// <param name="services"></param>
        /// <param name="variableId"></param>
        /// <param name="maxByteStringLength"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<ReadOnlyMemory<byte>> ReadBytesAsync(
            this IAttributeServiceSet services, NodeId variableId,
            int maxByteStringLength = 0, CancellationToken ct = default)
        {
            if (maxByteStringLength == 0)
            {
                maxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            }
            var offset = 0;
            MemoryStream? stream = null;
            try
            {
                while (true)
                {
                    var valueToRead = new ReadValueId
                    {
                        NodeId = variableId,
                        AttributeId = Attributes.Value,
                        IndexRange = new NumericRange(offset,
                            offset + maxByteStringLength - 1).ToString(),
                        DataEncoding = default
                    };

                    var readValueIds = new ReadValueId[] { valueToRead }.ToArrayOf();
                    var response = await services.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        readValueIds, ct).ConfigureAwait(false);
                    ClientBase.ValidateResponse(response.Results, readValueIds);
                    ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, readValueIds);
                    var wrappedValue = response.Results[0].WrappedValue;
                    if (wrappedValue.TypeInfo.BuiltInType != BuiltInType.ByteString ||
                        wrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "Value is not a ByteString scalar.");
                    }
                    if (StatusCode.IsBad(response.Results[0].StatusCode))
                    {
                        if (response.Results[0].StatusCode == StatusCodes.BadIndexRangeNoData)
                        {
                            // this happens when the previous read has fetched all remaining data
                            break;
                        }
                        var serviceResult = ClientBase.GetResult(response.Results[0].StatusCode,
                            0, response.DiagnosticInfos, response.ResponseHeader);
                        throw new ServiceResultException(serviceResult);
                    }
                    if (response.Results[0].WrappedValue.AsBoxedObject() is not byte[] chunk || chunk.Length == 0)
                    {
                        break;
                    }
                    if (chunk.Length < maxByteStringLength && offset == 0)
                    {
                        // Done
                        return chunk;
                    }
                    stream ??= new MemoryStream();
                    await stream.WriteAsync(chunk, ct).ConfigureAwait(false);
                    if (chunk.Length < maxByteStringLength)
                    {
                        // Done
                        break;
                    }
                    offset += maxByteStringLength;
                }
                return stream?.ToArray() ?? [];
            }
            finally
            {
                if (stream != null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
#endif
