/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
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
    public sealed class CttIdentifierConstants
    {
        private readonly Dictionary<string, double> _constants = new();

        public CttIdentifierConstants()
        {
            PopulateFromType(typeof(ObjectIds));
            PopulateFromType(typeof(VariableIds));
            PopulateFromType(typeof(MethodIds));
            PopulateFromType(typeof(ObjectTypeIds));
            PopulateFromType(typeof(VariableTypeIds));
            PopulateFromType(typeof(ReferenceTypeIds));
            PopulateFromType(typeof(DataTypeIds));
        }

        /// <summary>
        /// Creates a JS object with all identifier constants set as properties.
        /// </summary>
        public ObjectInstance ToJsObject()
        {
            var engine = new Engine();
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            foreach (var kvp in _constants)
            {
                obj.Set(kvp.Key, JsValue.FromObject(engine, kvp.Value));
            }
            return obj;
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
                        if (nodeId.Value.TryGetIdentifier(out uint numericId))
                        {
                            _constants[name] = numericId;
                        }
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
