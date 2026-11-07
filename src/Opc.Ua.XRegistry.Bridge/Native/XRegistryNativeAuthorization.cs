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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    internal static class XRegistryNativeAuthorization
    {
        public static async ValueTask EnsureAsync(
            XRegistryBridgeNativeOptions options, ISystemContext context, bool isMutation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (isMutation && !HasEncryptedChannel(context))
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient);
            }
            if (!HasAuthenticatedSession(context))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "An authenticated native session is required independently of upstream credentials.");
            }
            bool allowed = options.AuthorizeCallerAsync is null
                ? !isMutation
                : await options.AuthorizeCallerAsync(context, isMutation, ct).ConfigureAwait(false);
            if (!allowed)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "The inbound caller has not been authorized for this registry operation.");
            }
        }

        public static async ValueTask<bool> CanMutateAsync(
            XRegistryBridgeNativeOptions options, ISystemContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return HasAuthenticatedSession(context) &&
                HasEncryptedChannel(context) &&
                options.AuthorizeCallerAsync is not null &&
                await options.AuthorizeCallerAsync(context, true, ct).ConfigureAwait(false);
        }

        private static bool HasAuthenticatedSession(ISystemContext context)
        {
            return context is ISessionSystemContext
            {
                SessionId.IsNull: false,
                UserIdentity.TokenType: UserTokenType.UserName or UserTokenType.Certificate or UserTokenType.IssuedToken
            };
        }

        private static bool HasEncryptedChannel(ISystemContext context)
        {
            return context is SessionSystemContext
            {
                OperationContext: OperationContext
                {
                    ChannelContext.EndpointDescription.SecurityMode: MessageSecurityMode.SignAndEncrypt
                }
            };
        }
    }
}
