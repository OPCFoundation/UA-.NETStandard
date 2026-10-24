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

namespace Opc.Ua.DataChannels.Wire
{
    internal static partial class FlowControlCodec
    {
        /// <summary>
        /// Checks stateless carrier-specific constraints after the authenticated profile is selected.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static void ValidateCarrier(FlowControl control, FlowCarrier carrier, bool uascBaseline = false)
        {
            if (control is null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            FlowWireNames.Carrier(carrier);
            if (control is FlowHello hello && hello.Carrier != carrier)
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
            if (control is FlowCredit && !uascBaseline &&
                carrier is not (FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc))
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
            if (control is FlowBinding or FlowAssociation &&
                (!uascBaseline || carrier is not (FlowCarrier.Quic or FlowCarrier.WebTransport)))
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
            switch (control)
            {
                case FlowOpen open:
                    ValidateAssociation(open.Stream, open.Parameters, carrier, uascBaseline);
                    break;
                case FlowAccept accept:
                    ValidateAssociation(accept.Stream, accept.Parameters, carrier, uascBaseline);
                    break;
                case FlowPrepare prepare when uascBaseline ||
                    carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc:
                    ValidateLimits(prepare.Limits, 0);
                    break;
            }
        }

        /// <summary>
        /// Validates intrinsic header and body relationships without a stateful high-water mark.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        private static void Validate(FlowControl control, uint revision, uint messageId)
        {
            if (control is null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            FlowRecordCodec.ValidateFields(FlowRecordKind.Control, revision, messageId);
            bool connection = control.Flow == 0;
            if (connection &&
                control.Operation is not
                    (FlowOperation.Hello or FlowOperation.Credit or FlowOperation.Ping or FlowOperation.Pong or
                    FlowOperation.Status or FlowOperation.Bind))
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
            bool validRevision = control.Operation switch
            {
                FlowOperation.Hello or FlowOperation.Bind => revision == 0,
                FlowOperation.Open or FlowOperation.Accept or FlowOperation.Ready => revision == 1,
                FlowOperation.Associate or FlowOperation.AssociateAck => revision >= 1,
                FlowOperation.Prepare or FlowOperation.AcceptUpdate or FlowOperation.Commit or
                    FlowOperation.Ack or FlowOperation.AbortUpdate => revision >= 2,
                _ => connection ? revision == 0 : revision >= 1
            };
            if (!validRevision)
            {
                throw new FlowCodecException(FlowCodecError.InvalidRevision);
            }
            switch (control)
            {
                case FlowHello hello:
                    ValidateHello(hello);
                    break;
                case FlowOpen open:
                    ValidateParameters(open.Parameters);
                    FlowWireNames.Authorization(open.Authorization);
                    if (open.Authorization == FlowWireAuthorization.Session != open.Grant.HasValue)
                    {
                        throw new FlowCodecException(FlowCodecError.WrongType);
                    }
                    break;
                case FlowAccept accept:
                    ValidateParameters(accept.Parameters);
                    break;
                case FlowPrepare prepare:
                    ValidateLimits(prepare.Limits, 0);
                    break;
                case FlowCredit credit when connection && credit.ChannelCredit != 0:
                    throw new FlowCodecException(FlowCodecError.InvalidFlow);
                case FlowBinding binding:
                    if (binding.Role > 1)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidEnumeration);
                    }
                    if (binding.TranscriptDigest.IsNull || binding.TranscriptDigest.Length != 32 ||
                        binding.Proof.IsNull || binding.Proof.Length != 32)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidChallenge);
                    }
                    break;
                case FlowAssociation association:
                    if (association.Flow == 0 || association.Token == Guid.Empty)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidFlow);
                    }
                    if (association.Direction > 1)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidEnumeration);
                    }
                    break;
                case FlowOutcome outcome:
                    FlowWireNames.Status(outcome.Status);
                    break;
            }
        }

        /// <summary>
        /// Checks HELLO syntax without substituting for freshness or peer-identity validation.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateHello(FlowHello hello)
        {
            if (hello.Version != 1)
            {
                throw new FlowCodecException(FlowCodecError.UnsupportedVersion);
            }
            FlowWireNames.Carrier(hello.Carrier);
            FlowWireNames.Protection(hello.Protection);
            FlowWireText.ValidateUri(hello.Application);
            if (hello.Generation == 0 ||
                hello.MaxMessageSize == 0 ||
                hello.MaxReceiveBytes == 0 ||
                hello.MaxFlows == 0)
            {
                throw new FlowCodecException(FlowCodecError.OutOfRange);
            }
            if (hello.Challenge.Length != 32)
            {
                throw new FlowCodecException(FlowCodecError.InvalidChallenge);
            }
            if (hello.Carrier is FlowCarrier.Quic or FlowCarrier.WebTransport &&
                hello.Protection != FlowWireProtection.Confidential)
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
        }

        /// <summary>
        /// Checks descriptor syntax and stateless size/rate relationships.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateParameters(FlowWireParameters parameters)
        {
            FlowWireText.ValidateUri(parameters.Resource);
            FlowWireText.ValidateUri(parameters.Protocol);
            if (FlowWireText.Utf8Length(parameters.ContentType) > FlowWireText.MaxContentTypeBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            FlowWireNames.Direction(parameters.Direction);
            FlowWireNames.Delivery(parameters.Delivery);
            FlowWireNames.Protection(parameters.Protection);
            if (parameters.ContentType.Length == 0 ||
                (parameters.Delivery == FlowWireDelivery.Datagram && parameters.Limits.MaxRate <= 0))
            {
                throw new FlowCodecException(FlowCodecError.InvalidDescriptor);
            }
            ValidateLimits(parameters.Limits, 0);
        }

        /// <summary>
        /// Checks finite limits without wrapping encoded-size arithmetic.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateLimits(FlowWireLimits limits, int overhead)
        {
            if (limits.MaxMessageSize == 0 ||
                limits.MaxReceiveBytes == 0 ||
                double.IsNaN(limits.MaxRate) ||
                double.IsInfinity(limits.MaxRate) ||
                limits.MaxRate < 0)
            {
                throw new FlowCodecException(FlowCodecError.OutOfRange);
            }
            if ((ulong)limits.MaxMessageSize + (uint)overhead > limits.MaxReceiveBytes)
            {
                throw new FlowCodecException(FlowCodecError.InvalidDescriptor);
            }
        }

        /// <summary>
        /// Checks carrier-static association rules, leaving native ownership to the carrier.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        private static void ValidateAssociation(
            ulong stream, FlowWireParameters parameters, FlowCarrier carrier, bool uascBaseline)
        {
            if (uascBaseline || carrier is FlowCarrier.DedicatedUasc or FlowCarrier.SharedUasc)
            {
                if (stream != 0)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidStream);
                }
                if (parameters.Delivery != FlowWireDelivery.Ordered &&
                    (carrier is not (FlowCarrier.Quic or FlowCarrier.WebTransport) ||
                        parameters.Protection != FlowWireProtection.Confidential))
                {
                    throw new FlowCodecException(FlowCodecError.CarrierMismatch);
                }
                ValidateLimits(parameters.Limits,
                    parameters.Delivery == FlowWireDelivery.Datagram ? DirectDatagramCodec.HeaderSize : 0);
            }
            else if (parameters.Protection != FlowWireProtection.Confidential)
            {
                throw new FlowCodecException(FlowCodecError.CarrierMismatch);
            }
            else
            {
                int overhead = FlowRecordCodec.HeaderSize +
                    (parameters.Delivery == FlowWireDelivery.Datagram ? 4 : 0);
                ValidateLimits(parameters.Limits, overhead);
            }
        }
    }
}
