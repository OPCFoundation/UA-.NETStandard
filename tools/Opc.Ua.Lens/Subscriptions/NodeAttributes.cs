/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using Opc.Ua;

namespace UaLens.Subscriptions;

/// <summary>
/// OPC UA attribute set per <see cref="NodeClass"/>.
/// </summary>
/// <remarks>
/// Encodes the mandatory + optional attributes for each node class as
/// listed in OPC UA Part 3 §5 (Standard Node Model).  Used by the
/// attribute-panel view model to issue a single batched
/// <see cref="ManagedSession.ReadAsync"/> for exactly the attributes
/// the server would actually accept on that node — anything else would
/// always come back <c>BadAttributeIdInvalid</c>.
/// </remarks>
internal static class NodeAttributeSets
{
    public readonly record struct Entry(uint AttributeId, string Name);

    // Common attributes every node type carries.
    private static readonly Entry[] s_common =
    [
        new(Attributes.NodeId,              "NodeId"),
        new(Attributes.NodeClass,           "NodeClass"),
        new(Attributes.BrowseName,          "BrowseName"),
        new(Attributes.DisplayName,         "DisplayName"),
        new(Attributes.Description,         "Description"),
        new(Attributes.WriteMask,           "WriteMask"),
        new(Attributes.UserWriteMask,       "UserWriteMask"),
        new(Attributes.RolePermissions,     "RolePermissions"),
        new(Attributes.UserRolePermissions, "UserRolePermissions"),
        new(Attributes.AccessRestrictions,  "AccessRestrictions"),
    ];

    private static readonly Entry[] s_object =
    [
        new(Attributes.EventNotifier,       "EventNotifier"),
    ];

    private static readonly Entry[] s_variable =
    [
        new(Attributes.Value,                   "Value"),
        new(Attributes.DataType,                "DataType"),
        new(Attributes.ValueRank,               "ValueRank"),
        new(Attributes.ArrayDimensions,         "ArrayDimensions"),
        new(Attributes.AccessLevel,             "AccessLevel"),
        new(Attributes.UserAccessLevel,         "UserAccessLevel"),
        new(Attributes.MinimumSamplingInterval, "MinimumSamplingInterval"),
        new(Attributes.Historizing,             "Historizing"),
        new(Attributes.AccessLevelEx,           "AccessLevelEx"),
    ];

    private static readonly Entry[] s_method =
    [
        new(Attributes.Executable,      "Executable"),
        new(Attributes.UserExecutable,  "UserExecutable"),
    ];

    private static readonly Entry[] s_objectType =
    [
        new(Attributes.IsAbstract, "IsAbstract"),
    ];

    private static readonly Entry[] s_variableType =
    [
        new(Attributes.Value,           "Value"),
        new(Attributes.DataType,        "DataType"),
        new(Attributes.ValueRank,       "ValueRank"),
        new(Attributes.ArrayDimensions, "ArrayDimensions"),
        new(Attributes.IsAbstract,      "IsAbstract"),
    ];

    private static readonly Entry[] s_referenceType =
    [
        new(Attributes.IsAbstract,  "IsAbstract"),
        new(Attributes.Symmetric,   "Symmetric"),
        new(Attributes.InverseName, "InverseName"),
    ];

    private static readonly Entry[] s_dataType =
    [
        new(Attributes.IsAbstract,         "IsAbstract"),
        new(Attributes.DataTypeDefinition, "DataTypeDefinition"),
    ];

    private static readonly Entry[] s_view =
    [
        new(Attributes.ContainsNoLoops, "ContainsNoLoops"),
        new(Attributes.EventNotifier,   "EventNotifier"),
    ];

    /// <summary>
    /// Returns the union of the common attributes and the
    /// class-specific attributes for <paramref name="nodeClass"/>.
    /// </summary>
    public static IReadOnlyList<Entry> SupportedAttributes(NodeClass nodeClass)
    {
        Entry[] specific = nodeClass switch
        {
            NodeClass.Object => s_object,
            NodeClass.Variable => s_variable,
            NodeClass.Method => s_method,
            NodeClass.ObjectType => s_objectType,
            NodeClass.VariableType => s_variableType,
            NodeClass.ReferenceType => s_referenceType,
            NodeClass.DataType => s_dataType,
            NodeClass.View => s_view,
            _ => System.Array.Empty<Entry>()
        };
        var result = new List<Entry>(s_common.Length + specific.Length);
        result.AddRange(s_common);
        result.AddRange(specific);
        return result;
    }
}
