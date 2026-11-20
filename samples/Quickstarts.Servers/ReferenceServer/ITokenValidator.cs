// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua;

namespace Quickstarts.ReferenceServer
{
    public interface ITokenValidator
    {
        IUserIdentity ValidateToken(IssuedIdentityTokenHandler issuedToken);
    }
}
