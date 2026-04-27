/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Jint.Native;
using Jint.Native.Object;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// Global helper functions for CTT scripts.
    /// </summary>
    public static class CttGlobals
    {
        /// <summary>
        /// CTT's isDefined() — returns true if value is not null/undefined.
        /// </summary>
        public static bool IsDefined(object? value)
        {
            if (value == null) return false;
            if (value is JsValue jsVal)
            {
                // Check if the JsValue is null or undefined by checking the Type property
                // In Jint 4.x, these are represented as specific enum values
                string typeStr = jsVal.Type.ToString();
                return typeStr != "Null" && typeStr != "Undefined";
            }
            return true;
        }
    }
}


