// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    /// <summary>
    /// Build options of type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal interface IOptionsBuilder<T>
    {
        /// <summary>
        /// Build the options
        /// </summary>
        /// <returns></returns>
        T Options { get; }
    }
}
