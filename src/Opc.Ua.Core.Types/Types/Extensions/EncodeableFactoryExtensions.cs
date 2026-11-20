// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Extension methods for encodeable factories.
    /// </summary>
    public static class EncodeableFactoryExtensions
    {
        extension(EncodeableFactory)
        {
            /// <summary>
            /// Create a new encodeble factory initialized with all known types.
            /// </summary>
            /// <returns></returns>
            public static IEncodeableFactory Create()
            {
                return GetRoot().Fork();
            }

            /// <summary>
            /// Lazy create a root encodeable factory with the OPC UA types
            /// loaded.
            /// </summary>
            /// <returns></returns>
            private static EncodeableFactory GetRoot()
            {
                if (!EncodeableFactory.Root.IsValueCreated ||
                    // Also test whether it was initialized to prevent that
                    // a service message context was created with the root
                    // and then the encodeable factory is created.
                    !EncodeableFactory.Root.Value.ContainsEncodeableType(
                        DataTypeIds.ServiceFault)
                    )
                {
                    EncodeableFactory.Root.Value.Builder.AddOpcUa().Commit();
                }
                return EncodeableFactory.Root.Value;
            }
        }
    }
}
