// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.ISA95.Server.Providers
{
    internal enum Isa95JobOperation
    {
        Store,
        StoreAndStart,
        Start,
        Update,
        Stop,
        StopAndRemove,
        Cancel,
        Clear,
        Pause,
        Resume,
        Abort,
        RevokeStart
    }
}
