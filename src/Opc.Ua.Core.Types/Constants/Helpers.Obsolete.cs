// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class Attributes
    {
        /// <summary>
        /// Returns the browse names for all attributes.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }

        /// <summary>
        /// Returns the ids for all attributes.
        /// </summary>
        [Obsolete("Use Identifiers property instead.")]
        public static IEnumerable<uint> GetIdentifiers()
        {
            return [.. Identifiers];
        }
    }

    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class DataTypes
    {
        /// <summary>
        /// Returns the browse names for all data types.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }
    }

    /// <summary>
    /// A class that defines constants used by UA applications.
    /// </summary>
    public static partial class ReferenceTypes
    {
        /// <summary>
        /// Returns the browse names for all reference types.
        /// </summary>
        [Obsolete("Use BrowseNames property instead.")]
        public static string[] GetBrowseNames()
        {
            return [.. BrowseNames];
        }
    }
}
