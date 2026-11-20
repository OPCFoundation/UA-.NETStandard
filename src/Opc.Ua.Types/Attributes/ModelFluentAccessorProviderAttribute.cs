// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Identifies an assembly that contains source-generated typed fluent
    /// accessors for an OPC UA model.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class ModelFluentAccessorProviderAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="modelUri">The OPC UA model URI.</param>
        /// <param name="prefix">The C# namespace prefix of the model the
        /// accessors are generated for (the namespace of its state classes).</param>
        public ModelFluentAccessorProviderAttribute(string modelUri, string prefix)
        {
            ModelUri = modelUri;
            Prefix = prefix;
        }

        /// <summary>
        /// The OPC UA model URI.
        /// </summary>
        public string ModelUri { get; }

        /// <summary>
        /// The C# namespace prefix of the model the accessors are generated
        /// for. The accessors themselves live in that namespace unless a
        /// <c>[NodeManager]</c> binding placed them in the manager's namespace.
        /// </summary>
        public string Prefix { get; }
    }
}
