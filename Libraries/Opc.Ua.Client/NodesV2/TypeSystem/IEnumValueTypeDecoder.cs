#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;

    /// <summary>
    /// Extens decoders to decode enumerated values
    /// </summary>
    public interface IEnumValueTypeDecoder
    {
        /// <summary>
        /// Read enum value
        /// </summary>
        /// <param name="fieldName"></param>
        /// <param name="enumDefinition"></param>
        /// <returns></returns>
        EnumValue ReadEnumerated(string fieldName,
            EnumDefinition enumDefinition);
    }
}
#endif
