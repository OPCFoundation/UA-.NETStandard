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
