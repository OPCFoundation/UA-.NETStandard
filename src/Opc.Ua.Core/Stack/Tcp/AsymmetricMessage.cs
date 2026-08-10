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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Everything read from an asymmetric message.
    /// </summary>
    /// <param name="Body">The decrypted message body.</param>
    /// <param name="ChannelId">The secure channel identifier from the header.</param>
    /// <param name="SenderCertificate">
    /// The sender's certificate, which the caller owns and must dispose. May be
    /// <c>null</c> when the security policy carries none.
    /// </param>
    /// <param name="RequestId">The request identifier from the sequence header.</param>
    /// <param name="SequenceNumber">The sequence number from the sequence header.</param>
    /// <param name="Signature">The signature that was verified.</param>
    /// <remarks>
    /// The synchronous <c>ReadAsymmetricMessage</c> returns these through
    /// <see langword="out"/> parameters, which an asynchronous method cannot
    /// have.
    /// </remarks>
    public readonly record struct AsymmetricMessage(
        ArraySegment<byte> Body,
        uint ChannelId,
        Certificate? SenderCertificate,
        uint RequestId,
        uint SequenceNumber,
        byte[] Signature);

    /// <summary>
    /// The result of writing an asymmetric message.
    /// </summary>
    /// <param name="Chunks">
    /// The chunks to send. The caller owns them and must release them to the
    /// buffer manager.
    /// </param>
    /// <param name="Signature">
    /// The signature generated for the message, which the enhanced security
    /// policies bind into the response.
    /// </param>
    /// <remarks>
    /// The synchronous <c>WriteAsymmetricMessage</c> returns the signature
    /// through an <see langword="out"/> parameter, which an asynchronous method
    /// cannot have.
    /// </remarks>
    public readonly record struct AsymmetricWriteResult(
        BufferCollection Chunks,
        byte[] Signature);
}
