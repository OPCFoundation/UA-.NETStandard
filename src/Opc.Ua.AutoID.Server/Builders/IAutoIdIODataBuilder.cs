// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// Optional builder capability for application-defined sensor and actuator data.
    /// </summary>
    public interface IAutoIdIODataBuilder : IAutoIdReaderBuilder
    {
        /// <summary>
        /// Adds the standard IOData group and configures application-owned children before registration.
        /// </summary>
        /// <remarks>
        /// The callback owns its variables, access permissions and hardware bindings. OPC 30010 defines
        /// no standard children. Throwing aborts reader creation through the normal registration rollback.
        /// </remarks>
        IAutoIdReaderBuilder WithIOData(Action<ISystemContext, BaseObjectState> configure);
    }
}
