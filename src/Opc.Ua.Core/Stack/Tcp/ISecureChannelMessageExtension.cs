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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A secured message that arrived on a SecureChannel under a MessageType
    /// an extension owns.
    /// </summary>
    public readonly struct SecureChannelMessage : IEquatable<SecureChannelMessage>
    {
        /// <summary>
        /// Creates the message.
        /// </summary>
        /// <param name="messageType">The MessageType and chunk type as they
        /// appeared on the wire.</param>
        /// <param name="requestId">The RequestId from the sequence header.</param>
        /// <param name="body">The decrypted and verified body.</param>
        public SecureChannelMessage(uint messageType, uint requestId, ArraySegment<byte> body)
        {
            MessageType = messageType;
            RequestId = requestId;
            Body = body;
        }

        /// <summary>
        /// The MessageType and chunk type as they appeared on the wire.
        /// </summary>
        public uint MessageType { get; }

        /// <summary>
        /// The RequestId carried in the sequence header.
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        /// The decrypted and verified body.
        /// </summary>
        /// <remarks>
        /// The buffer belongs to the channel's receive loop and is valid only
        /// for the duration of the call. An extension that needs the content
        /// afterwards copies it.
        /// </remarks>
        public ArraySegment<byte> Body { get; }

        /// <inheritdoc/>
        public bool Equals(SecureChannelMessage other)
        {
            return MessageType == other.MessageType &&
                RequestId == other.RequestId &&
                Body.Equals(other.Body);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is SecureChannelMessage other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(MessageType, RequestId, Body);
        }

        /// <summary>
        /// Compares two messages for equality.
        /// </summary>
        /// <param name="left">The first message.</param>
        /// <param name="right">The second message.</param>
        public static bool operator ==(SecureChannelMessage left, SecureChannelMessage right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two messages for inequality.
        /// </summary>
        /// <param name="left">The first message.</param>
        /// <param name="right">The second message.</param>
        public static bool operator !=(SecureChannelMessage left, SecureChannelMessage right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// The SecureChannel an extension is attached to, and everything the
    /// extension is permitted to do with it.
    /// </summary>
    /// <remarks>
    /// A message that is neither a Service call nor part of establishing the
    /// SecureChannel still has to be secured with the channel's keys and drawn
    /// from the channel's single SequenceNumber space. This is the surface that
    /// makes that possible without publishing the channel's internals.
    /// </remarks>
    public interface ISecureChannelMessageHost
    {
        /// <summary>
        /// The identifier of the SecureChannel, unique across the process.
        /// </summary>
        string GlobalChannelId { get; }

        /// <summary>
        /// The largest chunk the peer accepts.
        /// </summary>
        int SendBufferSize { get; }

        /// <summary>
        /// The largest chunk this channel accepts.
        /// </summary>
        int ReceiveBufferSize { get; }

        /// <summary>
        /// The number of bytes the symmetric signature occupies in a secured
        /// chunk, which an extension subtracts when it computes how much of a
        /// chunk its own payload may occupy.
        /// </summary>
        int SymmetricSignatureSize { get; }

        /// <summary>
        /// The pool chunk buffers are rented from.
        /// </summary>
        BufferManager BufferManager { get; }

        /// <summary>
        /// The clock the channel measures time against.
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// How much of the SequenceNumber space remains under the
        /// SecurityToken currently in force.
        /// </summary>
        /// <remarks>
        /// The space is per token and is shared by every MessageType the
        /// channel carries, so an extension that emits a large number of
        /// messages has to draw on this rather than assume it is alone.
        /// Consume it inside the callback passed to <see cref="SendMessageAsync"/>,
        /// which is where the reservation and the send are atomic.
        /// </remarks>
        SequenceNumberBudget SequenceBudget { get; }

        /// <summary>
        /// Reports a fault whose blast radius is the whole SecureChannel.
        /// </summary>
        /// <param name="reason">Why the channel cannot continue.</param>
        void Fault(ServiceResult reason);

        /// <summary>
        /// Secures a body under the channel's keys and writes it as one chunk.
        /// </summary>
        /// <remarks>
        /// Assigning the SequenceNumber and applying message security are
        /// serialized against the Service traffic on the same channel, because
        /// both draw on the same keys and the same counter. <paramref name="onSecuring"/>
        /// runs inside that serialization: an extension uses it to decide,
        /// atomically with the send, whether the message may go at all, and
        /// throws to refuse. The write itself is awaited outside it, so a slow
        /// peer on an extension cannot stall Service traffic.
        /// </remarks>
        /// <param name="messageType">The MessageType to write.</param>
        /// <param name="requestId">The RequestId for the sequence header.</param>
        /// <param name="isRequest">True to secure with the client key set.</param>
        /// <param name="body">The already-encoded body.</param>
        /// <param name="onSecuring">Invoked while the message is being secured,
        /// or <c>null</c>. Throwing from it abandons the send.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendMessageAsync(
            uint messageType,
            uint requestId,
            bool isRequest,
            ArraySegment<byte> body,
            Action? onSecuring,
            CancellationToken ct);
    }

    /// <summary>
    /// Owns a MessageType that is neither a Service call nor part of
    /// establishing a SecureChannel, and carries it over the channel.
    /// </summary>
    /// <remarks>
    /// One extension is registered per MessageType per channel. Until one is
    /// registered the channel treats that MessageType as unrecognized and
    /// faults, which is what OPC 10000-6 §6.7.2.2 requires of a receiver that
    /// does not implement it.
    /// </remarks>
    public interface ISecureChannelMessageExtension
    {
        /// <summary>
        /// The MessageType this extension owns, without the chunk type.
        /// </summary>
        uint MessageType { get; }

        /// <summary>
        /// Handles a secured, sequence-verified message.
        /// </summary>
        /// <param name="message">The message.</param>
        void OnMessageReceived(in SecureChannelMessage message);

        /// <summary>
        /// Reports that a chunk of the owned MessageType could not be secured,
        /// verified or sequenced, so its content was never seen.
        /// </summary>
        /// <param name="reason">Why the chunk was rejected.</param>
        void OnMessageRejected(ServiceResult reason);

        /// <summary>
        /// Reports that a new SecurityToken is in force, which restores the
        /// SequenceNumber space the extension draws on.
        /// </summary>
        void OnSecurityTokenActivated();

        /// <summary>
        /// Reports that the SecureChannel is gone.
        /// </summary>
        void OnChannelClosed();
    }
}
