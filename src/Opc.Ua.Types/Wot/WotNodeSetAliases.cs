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
 *
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
using System.Collections.Generic;
using Opc.Ua.Export;
using Opc.Ua.Types;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Declares the <c>&lt;Aliases&gt;</c> a converted NodeSet2 document needs
    /// to be importable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NodeSet2 document may write a standard name such as
    /// <c>HasComponent</c> or <c>Double</c> wherever a NodeId is expected, but
    /// only if it declares that name in its own <c>&lt;Aliases&gt;</c> table.
    /// The importer resolves an attribute through that table and reports
    /// <c>BadNodeIdInvalid</c> for a name it does not find, so a document that
    /// uses a name it never declares cannot be loaded at all.
    /// </para>
    /// <para>
    /// Both halves of the conversion produce such names. Synthesis writes the
    /// readable names directly, and a restore reproduces whatever spelling the
    /// document it restores from used - which, for the <c>uav:nodes</c>
    /// projection and the <c>uav:nodeSet</c> envelope, is the source
    /// document's own and must stay byte-identical. Rewriting those names to
    /// identifiers would therefore break the preservation contract, so the
    /// names are kept and the missing declarations are added instead.
    /// </para>
    /// <para>
    /// Only a name this library can resolve to a standard base-namespace Node
    /// is declared. A name that resolves to nothing is left exactly as it was,
    /// so an undeclared alias in a source document still fails the import with
    /// the message that names it, rather than being quietly discarded.
    /// </para>
    /// </remarks>
    internal static class WotNodeSetAliases
    {
        /// <summary>
        /// Declares every standard name a node set uses but does not yet
        /// declare, and returns the same instance.
        /// </summary>
        /// <remarks>
        /// The pass is idempotent and adds nothing to a node set that already
        /// declares what it uses, which is what keeps a byte-exact restore
        /// byte-exact. New declarations are appended after the ones the
        /// document brought, in ascending ordinal order of the alias, so the
        /// result depends only on the content and never on enumeration order.
        /// </remarks>
        /// <param name="nodeSet">The node set to complete, or <c>null</c>.</param>
        /// <returns><paramref name="nodeSet"/>.</returns>
        public static UANodeSet? Declare(UANodeSet? nodeSet)
        {
            if (nodeSet?.Items is not { Length: > 0 } items)
            {
                return nodeSet;
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (nodeSet.Aliases is { Length: > 0 } aliases)
            {
                foreach (NodeIdAlias alias in aliases)
                {
                    if (alias?.Alias is { Length: > 0 } name)
                    {
                        declared.Add(name);
                    }
                }
            }

            var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (UANode node in items)
            {
                CollectFromNode(node, declared, missing);
            }

            if (missing.Count == 0)
            {
                return nodeSet;
            }

            int existing = nodeSet.Aliases?.Length ?? 0;
            var completed = new NodeIdAlias[existing + missing.Count];
            if (existing > 0)
            {
                Array.Copy(nodeSet.Aliases!, completed, existing);
            }
            int index = existing;
            foreach (KeyValuePair<string, string> entry in missing)
            {
                completed[index++] = new NodeIdAlias
                {
                    Alias = entry.Key,
                    Value = entry.Value
                };
            }
            nodeSet.Aliases = completed;
            return nodeSet;
        }

        private static void CollectFromNode(
            UANode? node,
            HashSet<string> declared,
            SortedDictionary<string, string> missing)
        {
            if (node is null)
            {
                return;
            }

            if (node.References is { Length: > 0 } references)
            {
                foreach (Reference reference in references)
                {
                    if (reference is null)
                    {
                        continue;
                    }
                    Collect(reference.ReferenceType, declared, missing);
                    Collect(reference.Value, declared, missing);
                }
            }

            if (node.RolePermissions is { Length: > 0 } permissions)
            {
                foreach (RolePermission permission in permissions)
                {
                    Collect(permission?.Value, declared, missing);
                }
            }

            if (node is UAInstance instance)
            {
                Collect(instance.ParentNodeId, declared, missing);
            }

            switch (node)
            {
                case UAVariable variable:
                    Collect(variable.DataType, declared, missing);
                    break;
                case UAMethod method:
                    Collect(method.MethodDeclarationId, declared, missing);
                    break;
                case UAVariableType variableType:
                    Collect(variableType.DataType, declared, missing);
                    break;
                case UADataType dataType:
                    CollectFromDefinition(dataType.Definition, declared, missing);
                    break;
            }
        }

        private static void CollectFromDefinition(
            Export.DataTypeDefinition? definition,
            HashSet<string> declared,
            SortedDictionary<string, string> missing)
        {
            if (definition is null)
            {
                return;
            }
            Collect(definition.BaseType, declared, missing);
            if (definition.Field is not { Length: > 0 } fields)
            {
                return;
            }
            foreach (Export.DataTypeField field in fields)
            {
                Collect(field?.DataType, declared, missing);
            }
        }

        /// <summary>
        /// Records one name that has to be declared, when it is a name at all
        /// and this library knows what it stands for.
        /// </summary>
        private static void Collect(
            string? value,
            HashSet<string> declared,
            SortedDictionary<string, string> missing)
        {
            if (string.IsNullOrEmpty(value) ||
                declared.Contains(value!) ||
                missing.ContainsKey(value!) ||
                IsIdentifier(value!) ||
                !TryResolveStandardName(value!, out string nodeId))
            {
                return;
            }
            missing.Add(value!, nodeId);
        }

        /// <summary>
        /// Gets whether a value is already an identifier rather than a name.
        /// </summary>
        /// <remarks>
        /// The check is by shape rather than by <c>NodeId.Parse</c>: parsing
        /// throws for every name, and a NodeSet of any size carries thousands
        /// of these values. Every identifier form NodeSet2 admits begins with
        /// a two-character type prefix or a namespace prefix, and no
        /// BrowseName can, so the two are told apart without allocating.
        /// </remarks>
        private static bool IsIdentifier(string value)
        {
            return StartsWith(value, "i=") ||
                StartsWith(value, "s=") ||
                StartsWith(value, "g=") ||
                StartsWith(value, "b=") ||
                StartsWith(value, "ns=") ||
                StartsWith(value, "nsu=") ||
                StartsWith(value, "svr=");
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves a standard base-namespace ReferenceType or DataType name.
        /// </summary>
        public static bool TryResolveStandardName(string name, out string nodeId)
        {
            if (WotVocabulary.TryGetReferenceTypeNodeId(name, out nodeId))
            {
                return true;
            }
            if (s_dataTypeNameToNodeId.TryGetValue(name, out nodeId!))
            {
                return true;
            }
            nodeId = string.Empty;
            return false;
        }

        /// <summary>
        /// The DataType names a NodeSet2 document conventionally aliases.
        /// </summary>
        /// <remarks>
        /// Written as name/identifier pairs taken from the generated
        /// identifier table, so a name and the identifier it stands for cannot
        /// drift apart. Only DataTypes of the base namespace appear: a name in
        /// any other namespace is the source document's to declare.
        /// </remarks>
        private static readonly (string Name, uint Identifier)[] s_standardDataTypes =
        [
            (nameof(DataTypes.BaseDataType), DataTypes.BaseDataType),
            (nameof(DataTypes.Boolean), DataTypes.Boolean),
            (nameof(DataTypes.SByte), DataTypes.SByte),
            (nameof(DataTypes.Byte), DataTypes.Byte),
            (nameof(DataTypes.Int16), DataTypes.Int16),
            (nameof(DataTypes.UInt16), DataTypes.UInt16),
            (nameof(DataTypes.Int32), DataTypes.Int32),
            (nameof(DataTypes.UInt32), DataTypes.UInt32),
            (nameof(DataTypes.Int64), DataTypes.Int64),
            (nameof(DataTypes.UInt64), DataTypes.UInt64),
            (nameof(DataTypes.Float), DataTypes.Float),
            (nameof(DataTypes.Double), DataTypes.Double),
            (nameof(DataTypes.String), DataTypes.String),
            (nameof(DataTypes.DateTime), DataTypes.DateTime),
            (nameof(DataTypes.Guid), DataTypes.Guid),
            (nameof(DataTypes.ByteString), DataTypes.ByteString),
            (nameof(DataTypes.XmlElement), DataTypes.XmlElement),
            (nameof(DataTypes.NodeId), DataTypes.NodeId),
            (nameof(DataTypes.ExpandedNodeId), DataTypes.ExpandedNodeId),
            (nameof(DataTypes.StatusCode), DataTypes.StatusCode),
            (nameof(DataTypes.QualifiedName), DataTypes.QualifiedName),
            (nameof(DataTypes.LocalizedText), DataTypes.LocalizedText),
            (nameof(DataTypes.Structure), DataTypes.Structure),
            (nameof(DataTypes.DataValue), DataTypes.DataValue),
            (nameof(DataTypes.DiagnosticInfo), DataTypes.DiagnosticInfo),
            (nameof(DataTypes.Number), DataTypes.Number),
            (nameof(DataTypes.Integer), DataTypes.Integer),
            (nameof(DataTypes.UInteger), DataTypes.UInteger),
            (nameof(DataTypes.Enumeration), DataTypes.Enumeration),
            (nameof(DataTypes.Image), DataTypes.Image),
            (nameof(DataTypes.Decimal), DataTypes.Decimal),
            (nameof(DataTypes.Union), DataTypes.Union),
            (nameof(DataTypes.UriString), DataTypes.UriString),
            (nameof(DataTypes.Argument), DataTypes.Argument),
            (nameof(DataTypes.EnumValueType), DataTypes.EnumValueType),
            (nameof(DataTypes.OptionSet), DataTypes.OptionSet),
            (nameof(DataTypes.Duration), DataTypes.Duration),
            (nameof(DataTypes.UtcTime), DataTypes.UtcTime),
            (nameof(DataTypes.LocaleId), DataTypes.LocaleId),
            (nameof(DataTypes.IntegerId), DataTypes.IntegerId),
            (nameof(DataTypes.NumericRange), DataTypes.NumericRange),
            (nameof(DataTypes.Counter), DataTypes.Counter),
            (nameof(DataTypes.VersionTime), DataTypes.VersionTime),
            (nameof(DataTypes.NormalizedString), DataTypes.NormalizedString),
            (nameof(DataTypes.DecimalString), DataTypes.DecimalString),
            (nameof(DataTypes.DurationString), DataTypes.DurationString),
            (nameof(DataTypes.TimeString), DataTypes.TimeString),
            (nameof(DataTypes.DateString), DataTypes.DateString),
            (nameof(DataTypes.TimeZoneDataType), DataTypes.TimeZoneDataType),
            (nameof(DataTypes.RolePermissionType), DataTypes.RolePermissionType),
            (nameof(DataTypes.PermissionType), DataTypes.PermissionType),
            (nameof(DataTypes.AccessRestrictionType), DataTypes.AccessRestrictionType),
            (nameof(DataTypes.StructureDefinition), DataTypes.StructureDefinition),
            (nameof(DataTypes.EnumDefinition), DataTypes.EnumDefinition),
            (nameof(DataTypes.StructureField), DataTypes.StructureField),
            (nameof(DataTypes.EnumField), DataTypes.EnumField)
        ];

        private static readonly Dictionary<string, string> s_dataTypeNameToNodeId =
            BuildDataTypeTable();

        private static Dictionary<string, string> BuildDataTypeTable()
        {
            var table = new Dictionary<string, string>(
                s_standardDataTypes.Length,
                StringComparer.Ordinal);
            foreach ((string name, uint identifier) in s_standardDataTypes)
            {
                table[name] = "i=" +
                    identifier.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return table;
        }
    }
}
