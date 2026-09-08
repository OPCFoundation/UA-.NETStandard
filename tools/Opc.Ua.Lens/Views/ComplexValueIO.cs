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
