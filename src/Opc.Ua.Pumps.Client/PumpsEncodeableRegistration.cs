// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Pumps.Client
{
    /// <summary>
    /// Teaches a session the structured types OPC 40223 defines, so a
    /// nameplate's <c>PhysicalAddress</c> and the five option sets decode into
    /// the generated types instead of staying opaque
    /// <see cref="ExtensionObject"/>s.
    /// </summary>
    internal static class PumpsEncodeableRegistration
    {
        public static void Register(ISession session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException(
                    "The session must provide a message context.",
                    nameof(session));

            IEncodeableFactory factory = messageContext.Factory;
            if (factory.ContainsEncodeableType(DataTypeIds.PhysicalAddressDataType))
            {
                return;
            }
            factory.Builder.AddOpcUaPumps().Commit();
        }
    }
}
