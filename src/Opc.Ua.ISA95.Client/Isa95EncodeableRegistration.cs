// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;

namespace Opc.Ua.ISA95.Client
{
    internal static class Isa95EncodeableRegistration
    {
        public static void Register(ISession session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException(
                    "The session must provide a message context.",
                    nameof(session));
            IEncodeableFactory factory = messageContext.Factory;
            IEncodeableFactoryBuilder builder = factory.Builder;
            bool commit = false;
            if (!factory.ContainsEncodeableType(DataTypeIds.ISA95TestResultDataType))
            {
                builder = builder.AddOpcUaISA95();
                commit = true;
            }
            if (!factory.ContainsEncodeableType(
                JobControl.V1.DataTypeIds.ISA95JobOrderDataType))
            {
                builder = JobControl.V1.OpcUaISA95JobControlV1Extensions
                    .AddOpcUaISA95JobControlV1(builder);
                commit = true;
            }
            if (!factory.ContainsEncodeableType(
                JobControl.V2.DataTypeIds.ISA95JobOrderDataType))
            {
                builder = JobControl.V2.OpcUaISA95JobControlV2Extensions
                    .AddOpcUaISA95JobControlV2(builder);
                commit = true;
            }
            if (commit)
            {
                builder.Commit();
            }
        }
    }
}
