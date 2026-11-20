/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Client side factory helpers that bind a <see cref="ComplexTypeSystem"/>
    /// to a client <see cref="ISession"/> using the session node cache as the
    /// <see cref="IComplexTypeResolver"/>. The default overloads use the
    /// NativeAOT friendly <see cref="DefaultComplexTypeFactory"/>.
    /// </summary>
    public static class ComplexTypeSystemClientExtensions
    {
        extension(ComplexTypeSystem)
        {
            /// <summary>
            /// Initializes the type system with a session to load the custom
            /// types using dynamically built stand-in encodeables (no reflection
            /// emit, NativeAOT friendly).
            /// </summary>
            /// <param name="session">The client session to load custom types for.</param>
            /// <param name="telemetry">The telemetry context.</param>
            /// <returns>A type system that owns its resolver; dispose it when
            /// no more types are loaded through it.</returns>
            public static ComplexTypeSystem Create(
                ISession session,
                ITelemetryContext telemetry)
            {
                return ComplexTypeSystem.Create(session, new DefaultComplexTypeFactory(), telemetry);
            }

            /// <summary>
            /// Initializes the type system with a session and a custom type
            /// builder factory to load the custom types.
            /// </summary>
            /// <param name="session">The client session to load custom types for.</param>
            /// <param name="complexTypeBuilderFactory">The type builder factory to use.</param>
            /// <param name="telemetry">The telemetry context.</param>
            /// <returns>A type system that owns its resolver; dispose it when
            /// no more types are loaded through it.</returns>
            public static ComplexTypeSystem Create(
                ISession session,
                IComplexTypeFactory complexTypeBuilderFactory,
                ITelemetryContext telemetry)
            {
#pragma warning disable CA2000 // ownership of the resolver transfers to the type system, which disposes it
                return new ComplexTypeSystem(
                    new NodeCacheResolver(session, telemetry),
                    complexTypeBuilderFactory,
                    telemetry,
                    ownsResolver: true);
#pragma warning restore CA2000
            }
        }
    }
}
