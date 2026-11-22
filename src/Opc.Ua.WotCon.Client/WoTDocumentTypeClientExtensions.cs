// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Status-preserving operations on an explicitly selected WoT document node.
    /// </summary>
    public static class WoTDocumentTypeClientExtensions
    {
        /// <summary>
        /// Validates the exact document represented by the proxy, returning the server's status and outcome.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(StatusCode StatusCode, WoTValidationOutcomeDataType Outcome)>
            ValidateWithResultAsync(this WoTDocumentTypeClient document, CancellationToken ct = default)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            CallMethodResult result = await document.CallWithResultAsync(
                MethodIds.WoTDocumentType_Validate, [], ct).ConfigureAwait(false);
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            if (result.OutputArguments.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Validate returned no outcome.");
            }
            Variant value = result.OutputArguments[0];
            IServiceMessageContext context = document.Session.MessageContext;
            if (value.TryGetStructure<WoTValidationOutcomeDataType>(
                context, out WoTValidationOutcomeDataType? outcome) &&
                outcome is not null)
            {
                return (result.StatusCode, outcome);
            }
            if (value.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetAsBinary(out ByteString body, context) &&
                !body.IsNull)
            {
                using var decoder = new BinaryDecoder(body.Span.ToArray(), context);
                outcome = new WoTValidationOutcomeDataType();
                outcome.Decode(decoder);
                return (result.StatusCode, outcome);
            }
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Validate returned an invalid outcome.");
        }
    }
}
