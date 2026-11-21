// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class VariableTypeIds
    {
        public static readonly NodeId BaseVariableType = new(VariableTypes.BaseVariableType);

        public static readonly NodeId BaseDataVariableType = new(VariableTypes.BaseDataVariableType);

        public static readonly NodeId PropertyType = new(VariableTypes.PropertyType);

        public static readonly NodeId DataTypeDictionaryType = new(VariableTypes.DataTypeDictionaryType);
    }
}
