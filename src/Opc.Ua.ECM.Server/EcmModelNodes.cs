// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Validates generated model children before binding or publishing their values.
    /// </summary>
    internal static class EcmModelNodes
    {
        /// <summary>
        /// Returns a declared child or reports an incomplete generated graph.
        /// </summary>
        /// <typeparam name="T">The generated node type.</typeparam>
        internal static T Required<T>(T? node)
            where T : NodeState
        {
            return node ?? throw new InvalidOperationException(
                $"The ECM graph is missing a required {typeof(T).Name} child.");
        }
    }
}
