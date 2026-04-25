#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;

    /// <summary>
    /// Extends encoders to encode enumerated values
    /// </summary>
    public interface IEnumValueTypeEncoder
    {
        /// <summary>
        /// Read enum value
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="enumValue"></param>
        /// <param name="enumDefinition"></param>
        /// <returns></returns>
        void WriteEnumerated(string fieldName, EnumValue enumValue,
            EnumDefinition enumDefinition);
    }
}
#endif
