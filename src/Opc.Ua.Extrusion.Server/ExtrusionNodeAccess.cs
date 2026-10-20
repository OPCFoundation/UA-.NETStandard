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
using System.Collections.Generic;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Operates on real compiled nodes while preserving their declared wire types.
    /// </summary>
    internal static class ExtrusionNodeAccess
    {
        /// <summary>
        /// Resolves a relative path, rejecting ambiguity rather than selecting a similarly named vendor node.
        /// </summary>
        internal static BaseInstanceState? Find(ISystemContext context, NodeState root, string path)
        {
            NodeState current = root;
            foreach (string segment in path.Split('/'))
            {
                BaseInstanceState? match = null;
                foreach (BaseInstanceState child in Children(context, current))
                {
                    if (child.BrowseName.Name == segment)
                    {
                        if (match != null)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadBrowseNameInvalid, "Browse path '{0}' is ambiguous.", path);
                        }
                        match = child;
                    }
                }
                if (match == null)
                {
                    return null;
                }
                current = match;
            }
            return current as BaseInstanceState;
        }

        /// <summary>
        /// Gets all immediate generated and dynamically repeated children.
        /// </summary>
        internal static List<BaseInstanceState> Children(ISystemContext context, NodeState root)
        {
            var result = new List<BaseInstanceState>();
            root.GetChildren(context, result);
            return result;
        }

        /// <summary>
        /// Enumerates a device graph together with its concrete relative BrowseName paths.
        /// </summary>
        internal static IEnumerable<(string Path, BaseInstanceState Node)> Walk(
            ISystemContext context,
            NodeState root,
            string prefix = "")
        {
            foreach (BaseInstanceState child in Children(context, root))
            {
                string path = prefix.Length == 0 ? child.BrowseName.Name! : prefix + "/" + child.BrowseName.Name;
                yield return (path, child);
                foreach ((string Path, BaseInstanceState Node) descendant in Walk(context, child, path))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Writes a configured or already committed value without impersonating a UA service request.
        /// </summary>
        internal static void Set(BaseVariableState variable, in DataValue value)
        {
            variable.WrappedValue = value.WrappedValue;
            variable.StatusCode = value.StatusCode;
            variable.Timestamp = value.SourceTimestamp;
        }

        /// <summary>
        /// Initializes a known identity field before publication.
        /// </summary>
        internal static void SetIdentity(
            ISystemContext context,
            NodeState root,
            string path,
            Variant value,
            DateTimeUtc timestamp)
        {
            if (Find(context, root, path) is BaseVariableState variable)
            {
                Set(variable, new DataValue(value, StatusCodes.Good, timestamp));
            }
        }

        /// <summary>
        /// Validates native UA datatype, rank and defined enumeration values.
        /// </summary>
        internal static void ValidateValue(ISystemContext context, BaseVariableState variable, in DataValue value)
        {
            if (value.SourceTimestamp == DateTimeUtc.MinValue)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidTimestampArgument, "A committed observation requires its source timestamp.");
            }
            if (value.WrappedValue.IsNull && StatusCode.IsBad(value.StatusCode))
            {
                return;
            }
            if (TypeInfo.IsInstanceOfDataType(value.WrappedValue, variable.DataType, variable.ValueRank,
                context.NamespaceUris, context.TypeTable).IsUnknown)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch, "The committed value does not match '{0}'.", variable.BrowseName);
            }
            if (value.WrappedValue.TryGetValue(out EnumValue enumeration) &&
                context.TypeTable.IsTypeOf(variable.DataType, Opc.Ua.DataTypeIds.Enumeration) &&
                context.EncodeableFactory.TryGetEnumeratedType(
                    NodeId.ToExpandedNodeId(variable.DataType, context.NamespaceUris),
                    out IEnumeratedType? definition) &&
                !definition.TryGetSymbol(enumeration.Value, out _))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange, "The committed enumeration is not declared by '{0}'.",
                        variable.BrowseName);
            }
            if (value.WrappedValue.TryGetValue(out double number) && (double.IsNaN(number)
                || double.IsInfinity(number)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange, "Extrusion measurements must be finite.");
            }
            if (variable.BrowseName.Name == "ControllerOutput" &&
                context.NamespaceUris.GetString(variable.BrowseName.NamespaceIndex) == Namespaces.Extrusion &&
                value.WrappedValue.TryGetValue(out double output) && (output < -100 || output > 100))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                        "The extrusion heating/cooling output must be between -100 and 100 percent.");
            }
            if (variable.Parent is Extruder.FeederState feeder && variable.BrowseName.Name == "Mode" &&
                value.WrappedValue.TryGetValue(out Extruder.FeedingModeEnumeration mode) &&
                mode != Extruder.FeedingModeEnumeration.ONLY_CONVEYING && feeder.Throughput == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "OPC 40084-3 requires Throughput for a dosing feeder.");
            }
        }

        /// <summary>
        /// Identifies metadata that must not be treated as an unknown live measurement.
        /// </summary>
        internal static bool IsMetadata(BaseVariableState variable)
        {
            return variable.BrowseName.Name is
                "EngineeringUnits" or "EURange" or "InstrumentRange" or "EnumValues" or "EnumStrings" or
                "InputArguments" or "OutputArguments" or "NodeVersion" or "DefaultInstanceBrowseName" or
                "Definition" or "ValuePrecision" or "StateNumber" or "TransitionNumber" or
                "TrueState" or "FalseState" or "Id" or "Name" or "Description" or "Model" or "Manufacturer" or
                "SerialNumber" or "ProductInstanceUri" or "DeviceClass" or "ControllerName" or "LineId" or
                "SupportedLogbookEvents";
        }
    }
}
