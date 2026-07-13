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
using Opc.Ua.PubSub.Security;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Security context for a transcode. Holds the target-side UADP
    /// security wrapper used to re-secure re-published messages and the
    /// cross-encoding policy governing whether a secured source may be
    /// lowered to an encoding without message-layer security (JSON).
    /// </summary>
    /// <remarks>
    /// Message-layer security in Part 14 is defined only for the UADP
    /// mapping (<see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4">
    /// §7.2.4.4</see>). Transcoding a secured UADP stream to JSON therefore
    /// drops message-layer protection; such a downgrade is refused unless
    /// <see cref="AllowInsecureCrossEncoding"/> is set (the deployment then
    /// relies on transport-layer security such as DTLS, TLS or MQTT).
    /// </remarks>
    public sealed class TranscodeSecurity
    {
        /// <summary>
        /// An unsecured context: no wrapping is applied and any secured
        /// source that would be downgraded is refused.
        /// </summary>
        public static TranscodeSecurity None { get; } = new();

        /// <summary>
        /// Target-side UADP security wrapper. When <see langword="null"/>
        /// the target is emitted without message-layer security.
        /// </summary>
        public UadpSecurityWrapper? TargetWrapper { get; init; }

        /// <summary>
        /// Sign / encrypt selection applied by <see cref="TargetWrapper"/>.
        /// </summary>
        public UadpSecurityWrapOptions TargetWrapOptions { get; init; }
            = UadpSecurityWrapOptions.SignAndEncrypt;

        /// <summary>
        /// When <see langword="true"/>, allows transcoding a secured
        /// source to an encoding that cannot carry message-layer security
        /// (JSON) or to an unsecured UADP target. Defaults to
        /// <see langword="false"/> so security downgrades are refused.
        /// </summary>
        public bool AllowInsecureCrossEncoding { get; init; }

        /// <summary>
        /// <see langword="true"/> when a target-side wrapper is configured.
        /// </summary>
        public bool IsTargetSecured => TargetWrapper is not null;

        /// <summary>
        /// Wraps an encoded UADP frame that was produced with a security
        /// boundary. When no target wrapper is configured the input is
        /// returned unchanged.
        /// </summary>
        /// <param name="encoded">
        /// Encoded UADP NetworkMessage (prefix + payload).
        /// </param>
        /// <param name="payloadOffset">
        /// Offset separating the outer prefix from the securable payload.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The wrapped frame, or the input when unsecured.</returns>
        public ValueTask<ReadOnlyMemory<byte>> WrapUadpAsync(
            ReadOnlyMemory<byte> encoded,
            int payloadOffset,
            CancellationToken cancellationToken = default)
        {
            if (TargetWrapper is null)
            {
                return new ValueTask<ReadOnlyMemory<byte>>(encoded);
            }
            ReadOnlyMemory<byte> prefix = encoded[..payloadOffset];
            ReadOnlyMemory<byte> inner = encoded[payloadOffset..];
            return TargetWrapper.WrapAsync(
                prefix, inner, TargetWrapOptions, cancellationToken);
        }

        /// <summary>
        /// Returns <see langword="true"/> when producing the target output
        /// would lower the security level of a secured source (or fail to
        /// honour a requested target security) and the insecure
        /// cross-encoding policy does not permit it.
        /// </summary>
        /// <param name="sourceSecured">
        /// Whether the source frame carried message-layer security.
        /// </param>
        /// <param name="targetEncoding">The requested target encoding.</param>
        /// <returns>
        /// <see langword="true"/> when the transcode must be refused.
        /// </returns>
        public bool WouldRefuseDowngrade(bool sourceSecured, TranscodeEncoding targetEncoding)
        {
            bool outputSecured = targetEncoding == TranscodeEncoding.Uadp && IsTargetSecured;
            bool securityIntended = sourceSecured || IsTargetSecured;
            return securityIntended && !outputSecured && !AllowInsecureCrossEncoding;
        }
    }
}
