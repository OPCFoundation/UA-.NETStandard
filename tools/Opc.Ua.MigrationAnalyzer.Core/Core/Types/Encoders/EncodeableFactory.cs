/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Migration shim &#8212; restores <c>EncodeableFactory.GlobalFactory</c>
    /// which was removed in 1.6. Callers should migrate to
    /// <see cref="ServiceMessageContext.GlobalContext"/>'s
    /// <see cref="ServiceMessageContext.Factory"/>.
    /// </summary>
    public static class EncodeableFactoryShim
    {
        extension(EncodeableFactory)
        {
            /// <summary>
            /// The global encodeable factory shared across the process.
            /// </summary>
            [Obsolete("EncodeableFactory.GlobalFactory was removed in 1.6. " +
                "Use ServiceMessageContext.GlobalContext.Factory instead. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0020")]
            [OpcUaShim("UA0020")]
            public static IEncodeableFactory GlobalFactory => ServiceMessageContext.GlobalContext.Factory;
        }
    }
}
