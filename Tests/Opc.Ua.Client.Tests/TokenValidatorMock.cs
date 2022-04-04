// This document contains confidential and proprietary information of ABB
// and may be protected by patents, trademarks, copyrights, trade secrets,
// and/or other relevant state, federal, and foreign laws. Its receipt or
// possession does not convey any rights to reproduce, disclose its contents,
// or to manufacture, use or sell anything contained herein. Forwarding,
// reproducing, disclosing or using without specific written authorization of
// ABB is strictly forbidden.
//
// See the LICENSE file in the project root for more information.
// Copyright © ABB Ltd. All rights reserved.

using Opc.Ua;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    public class TokenValidatorMock : ITokenValidator
    {
        public IssuedIdentityToken LastIssuedToken { get; set; }
            
        public IUserIdentity ValidateToken(IssuedIdentityToken issuedToken)
        {
            this.LastIssuedToken = issuedToken;

            return new UserIdentity(issuedToken);
        }
    }
}
