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
 *
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

using System.Collections.Generic;
using System.IO;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Stores a <see cref="SessionHandoff"/> as a protected record. A handoff carries the
    /// secrets of a server session, so it is only ever written through an
    /// <see cref="IRecordProtector"/>.
    /// </summary>
    internal static class SessionHandoffCodec
    {
        /// <summary>
        /// Encodes and protects a handoff.
        /// </summary>
        /// <param name="handoff">The handoff to store.</param>
        /// <param name="protector">The record protector.</param>
        /// <param name="context">The protection context of the record.</param>
        /// <param name="messageContext">The message context whose namespace tables the
        /// subscriptions of the handoff refer to.</param>
        /// <returns>The protected record.</returns>
        internal static ByteString Protect(
            SessionHandoff handoff,
            IRecordProtector protector,
            ByteString context,
            IServiceMessageContext messageContext)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, messageContext, true))
            {
                encoder.WriteInt32(null, kMagic);
                encoder.WriteInt32(null, kVersion);
                using (var configuration = new MemoryStream())
                {
                    SnapshotCodec.SaveConfiguration(configuration, messageContext, handoff.Session);
                    encoder.WriteByteString(null, new ByteString(configuration.ToArray()));
                }
                encoder.WriteDouble(null, handoff.RevisedSessionTimeout);
                encoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
                encoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
                encoder.WriteInt32(null, handoff.Subscriptions.Count);
                for (int i = 0; i < handoff.Subscriptions.Count; i++)
                {
                    encoder.WriteEncodeable(null, handoff.Subscriptions[i]);
                }
            }
            return protector.Protect(context, new ByteString(stream.ToArray()));
        }

        /// <summary>
        /// Unprotects and decodes a handoff stored by <see cref="Protect"/>.
        /// </summary>
        /// <param name="record">The protected record.</param>
        /// <param name="protector">The record protector.</param>
        /// <param name="context">The protection context of the record.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="handoff">The decoded handoff.</param>
        /// <returns>False when the record cannot be unprotected or is empty.</returns>
        /// <exception cref="ServiceResultException">The record is malformed.</exception>
        internal static bool TryUnprotect(
            ByteString record,
            IRecordProtector protector,
            ByteString context,
            ITelemetryContext telemetry,
            out SessionHandoff? handoff)
        {
            handoff = null;
            if (!protector.TryUnprotect(context, record, out ByteString plaintext) || plaintext.IsEmpty)
            {
                return false;
            }
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.Factory.Builder.AddOpcUaClientDataTypes();
            using var stream = new MemoryStream(plaintext.ToArray(), writable: false);
            using var decoder = new BinaryDecoder(stream, messageContext, true);
            if (decoder.ReadInt32(null) != kMagic || decoder.ReadInt32(null) != kVersion)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The record is not a supported session handoff.");
            }
            ByteString configurationBytes = decoder.ReadByteString(null);
            SessionConfiguration configuration;
            using (var configurationStream = new MemoryStream(configurationBytes.ToArray(), writable: false))
            {
                configuration = SessionConfiguration.Create(configurationStream, telemetry)
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        "The session handoff has no session.");
            }
            double timeout = decoder.ReadDouble(null);
            ArrayOf<string?> namespaces = decoder.ReadStringArray(null);
            ArrayOf<string?> servers = decoder.ReadStringArray(null);
            decoder.SetMappingTables(
                namespaces.IsNull ? new NamespaceTable() : new NamespaceTable(namespaces.Memory.ToArray()!),
                servers.IsNull ? new StringTable() : new StringTable(servers.Memory.ToArray()!));
            int count = decoder.ReadInt32(null);
            if (count < 0 || (messageContext.MaxArrayLength > 0 && count > messageContext.MaxArrayLength))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadDecodingError,
                    "The subscription count of the session handoff is invalid.");
            }
            var subscriptions = new List<SubscriptionStateSnapshot>();
            for (int i = 0; i < count; i++)
            {
                subscriptions.Add(decoder.ReadEncodeable<SubscriptionStateSnapshot>(null));
            }
            handoff = new SessionHandoff
            {
                Session = configuration,
                RevisedSessionTimeout = timeout,
                Subscriptions = subscriptions.ToArray()
            };
            return true;
        }

        /// <summary>
        /// Identifies a session handoff record.
        /// </summary>
        private const int kMagic = 0x48535541;

        /// <summary>
        /// The version of the record format.
        /// </summary>
        private const int kVersion = 1;
    }
}
