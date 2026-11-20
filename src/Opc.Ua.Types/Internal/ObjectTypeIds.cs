/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class ObjectTypeIds
    {
        public static readonly NodeId BaseObjectType = new(ObjectTypes.BaseObjectType);

        public static readonly NodeId DataTypeEncodingType = new(ObjectTypes.DataTypeEncodingType);

        public static readonly NodeId BaseEventType = new(ObjectTypes.BaseEventType);
    }
}
