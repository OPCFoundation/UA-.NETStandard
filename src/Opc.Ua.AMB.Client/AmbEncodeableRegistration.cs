// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// Registers the AMB data types with the session, so the structures in
    /// alarm events and method results decode.
    /// </summary>
    internal static class AmbEncodeableRegistration
    {
        public static void Register(ISession session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException("The session must provide a message context.", nameof(session));

            IEncodeableFactory factory = messageContext.Factory;
            if (factory.ContainsEncodeableType(DataTypeIds.RootCauseDataType))
            {
                return;
            }
            factory.Builder.AddOpcUaAMB().Commit();
        }
    }
}
