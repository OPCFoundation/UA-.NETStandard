/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace Opc.Ua.CttTestRunner.Runtime.Types
{
    /// <summary>
    /// Exposes all OPC UA numeric NodeId constants (Identifier.*) to JavaScript.
    /// The CTT scripts use patterns like: new UaNodeId(Identifier.Server_ServerCapabilities)
    /// </summary>
    public sealed class CttIdentifierConstants : ObjectInstance
    {
        public CttIdentifierConstants() : base(new Engine())
        {
            // Use reflection to populate from the Opc.Ua.ObjectIds, VariableIds, etc.
            PopulateFromType(typeof(ObjectIds));
            PopulateFromType(typeof(VariableIds));
            PopulateFromType(typeof(MethodIds));
            PopulateFromType(typeof(ObjectTypeIds));
            PopulateFromType(typeof(VariableTypeIds));
            PopulateFromType(typeof(ReferenceTypeIds));
            PopulateFromType(typeof(DataTypeIds));
        }

        private void PopulateFromType(Type idsType)
        {
            foreach (var field in idsType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(NodeId))
                {
                    var nodeId = (NodeId?)field.GetValue(null);
                    if (nodeId.HasValue && nodeId.Value.IdType == IdType.Numeric)
                    {
                        string name = ConvertFieldName(field.Name);
                        Set(name,
                            JsValue.FromObject(Engine, (double)(uint)nodeId.Value.Identifier!));
                    }
                }
            }
        }

        /// <summary>
        /// Convert .NET field names (Server_ServerCapabilities) to CTT identifier format.
        /// CTT uses the same underscore-separated naming convention.
        /// </summary>
        private static string ConvertFieldName(string name)
        {
            return name;
        }
    }
}
