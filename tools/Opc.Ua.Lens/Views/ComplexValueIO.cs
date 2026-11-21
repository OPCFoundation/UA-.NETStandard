// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.StructuredValues;

namespace UaLens.Views;

/// <summary>
/// Shares the non-UI schema service between dialogs belonging to one managed session.
/// The service invalidates metadata on inner-session/namespace replacement, not merely
/// when this wrapper is collected.
/// </summary>
internal static class ComplexValueIO
{
    public static IStructuredValueService ForSession(ManagedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return s_services.GetValue(session, static value => new SessionStructuredValueService(value));
    }

    public static Task<DataTypeDefinition?> GetDataTypeDefinitionAsync(
        NodeId dataTypeId,
        ManagedSession session,
        CancellationToken cancellationToken)
    {
        return ForSession(session).ResolveAsync(dataTypeId, cancellationToken);
    }

    public static void Refresh(ManagedSession session)
    {
        ForSession(session).Refresh();
    }

    public static async Task<bool> IsComplexAsync(
        NodeId dataTypeId,
        ManagedSession session,
        CancellationToken cancellationToken)
    {
        DataTypeDefinition? definition = await GetDataTypeDefinitionAsync(
            dataTypeId, session, cancellationToken).ConfigureAwait(false);
        return definition is StructureDefinition or EnumDefinition;
    }

    public static Variant DefaultScalar(BuiltInType builtInType)
    {
        return Variant.CreateDefault(TypeInfo.Create(builtInType, ValueRanks.Scalar));
    }

    private static readonly ConditionalWeakTable<ManagedSession, SessionStructuredValueService> s_services = new();
}
