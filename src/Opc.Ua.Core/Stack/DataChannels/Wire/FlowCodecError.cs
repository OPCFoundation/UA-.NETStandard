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

using System;
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Identifies a codec failure without exposing payload or grant contents.
    /// </summary>
    internal enum FlowCodecError
    {
        /// <summary>The input ended inside a record.</summary>
        Truncated,

        /// <summary>The declared record length is too small.</summary>
        InvalidLength,

        /// <summary>The record kind is not supported.</summary>
        UnknownKind,

        /// <summary>A finite local bound was exceeded.</summary>
        TooLarge,

        /// <summary>The message identifier is zero.</summary>
        InvalidMessageId,

        /// <summary>The revision is invalid for this record.</summary>
        InvalidRevision,

        /// <summary>A single-record container has extra bytes.</summary>
        TrailingBytes,

        /// <summary>A field or record has the wrong type.</summary>
        WrongType,

        /// <summary>A number is outside its permitted range.</summary>
        OutOfRange,

        /// <summary>The UTF-8 sequence is invalid.</summary>
        InvalidUtf8,

        /// <summary>A string contains an unpaired surrogate.</summary>
        InvalidUnicode,

        /// <summary>A challenge does not contain exactly 32 bytes.</summary>
        InvalidChallenge,

        /// <summary>An absolute URI is required.</summary>
        InvalidUri,

        /// <summary>A typed value is not a supported discriminator.</summary>
        InvalidEnumeration,

        /// <summary>Descriptor fields are mutually inconsistent.</summary>
        InvalidDescriptor,

        /// <summary>A control operation is not supported.</summary>
        UnknownOperation,

        /// <summary>A binary discriminator has no defined value.</summary>
        UnknownDiscriminator,

        /// <summary>The selected wire version is not supported.</summary>
        UnsupportedVersion,

        /// <summary>The addressed flow is invalid.</summary>
        InvalidFlow,

        /// <summary>The UASC FLO RequestId is not zero.</summary>
        RequestIdMismatch,

        /// <summary>A UASC flow body is not a final chunk.</summary>
        NonFinalUasc,

        /// <summary>A native datagram contains CONTROL.</summary>
        DatagramControl,

        /// <summary>A carrier selection or capability is inconsistent.</summary>
        CarrierMismatch,

        /// <summary>A UASC control claims a native stream.</summary>
        InvalidStream
    }

    /// <summary>
    /// Reports a bounded, payload-independent wire decoding failure.
    /// </summary>
    // This internal failure must be translated at the owning API boundary, not exposed as a second public model.
    // TODO: Revisit these design rules if a public codec exception contract is introduced.
    [SuppressMessage("Design", "CA1064:Exceptions should be public",
        Justification = "The codec and its typed failures are an internal implementation seam.")]
    [SuppressMessage("Design", "CA1032:Implement standard exception constructors",
        Justification = "A precise error discriminator is required; message-only constructors would lose it.")]
    [SuppressMessage("Roslynator", "RCS1194:Implement exception constructors",
        Justification = "Typed failures cannot be created through arbitrary message-only constructors.")]
    internal sealed class FlowCodecException : Exception
    {
        /// <summary>
        /// Creates a codec failure.
        /// </summary>
        internal FlowCodecException(FlowCodecError error)
            : base($"Flow codec rejected input: {error}.")
        {
            Error = error;
        }

        /// <summary>
        /// Gets the precise codec failure.
        /// </summary>
        internal FlowCodecError Error { get; }

        /// <summary>
        /// Gets the protocol category; fault scope and response policy belong to the caller.
        /// </summary>
        internal FlowWireStatus Status => Error switch
        {
            FlowCodecError.TooLarge => FlowWireStatus.TooLarge,
            FlowCodecError.UnknownKind or FlowCodecError.UnknownOperation or FlowCodecError.UnknownDiscriminator or
                FlowCodecError.UnsupportedVersion or FlowCodecError.DatagramControl => FlowWireStatus.Unsupported,
            _ => FlowWireStatus.InvalidArgument
        };
    }

    /// <summary>
    /// Supplies immutable local byte bounds independently of any peer-advertised limits.
    /// </summary>
    internal sealed class FlowCodecLimits
    {
        /// <summary>
        /// Creates finite bounds. The record bound excludes any ChannelId or security envelope.
        /// </summary>
        internal FlowCodecLimits(
            int maxDataBytes = 16 * 1024 * 1024,
            int maxControlBytes = 65536,
            int maxRecordBytes = int.MaxValue)
        {
            if (maxDataBytes is < 0 or > (int.MaxValue - FlowRecordCodec.HeaderSize))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDataBytes));
            }
            if (maxControlBytes is < 1 or > (int.MaxValue - FlowRecordCodec.HeaderSize))
            {
                throw new ArgumentOutOfRangeException(nameof(maxControlBytes));
            }
            if (maxRecordBytes < FlowRecordCodec.HeaderSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRecordBytes));
            }
            MaxDataBytes = maxDataBytes;
            MaxControlBytes = maxControlBytes;
            MaxRecordBytes = maxRecordBytes;
        }

        /// <summary>
        /// Gets the maximum DATA body size.
        /// </summary>
        internal int MaxDataBytes { get; }

        /// <summary>
        /// Gets the maximum CONTROL body size.
        /// </summary>
        internal int MaxControlBytes { get; }

        /// <summary>
        /// Gets the maximum complete common-record size.
        /// </summary>
        internal int MaxRecordBytes { get; }
    }
}
