// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Interface to access properties of a complex type.
    /// </summary>
    public interface IComplexTypeProperties : IStructure
    {
        /// <summary>
        /// Get count of properties.
        /// </summary>
        int GetPropertyCount();

        /// <summary>
        /// Get ordered list of property names.
        /// </summary>
        IList<string> GetPropertyNames();

        /// <summary>
        /// Get ordered list of property types.
        /// </summary>
        IList<Type> GetPropertyTypes();

        /// <summary>
        /// Ordered enumerator for properties.
        /// </summary>
        IEnumerable<ComplexTypePropertyInfo> GetPropertyEnumerator();
    }
}
