/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Opc.Ua.Schema;

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
            public static ComplexTypeSystem Create(
                ISession session,
                ITelemetryContext telemetry)
            {
                return new ComplexTypeSystem(
                    new NodeCacheResolver(session, telemetry),
                    telemetry);
            }

            /// <summary>
            /// Initializes the type system with a session and a custom type
            /// builder factory to load the custom types.
            /// </summary>
            /// <param name="session">The client session to load custom types for.</param>
            /// <param name="complexTypeBuilderFactory">The type builder factory to use.</param>
            /// <param name="telemetry">The telemetry context.</param>
            public static ComplexTypeSystem Create(
                ISession session,
                IComplexTypeFactory complexTypeBuilderFactory,
                ITelemetryContext telemetry)
            {
                return new ComplexTypeSystem(
                    new NodeCacheResolver(session, telemetry),
                    complexTypeBuilderFactory,
                    telemetry);
            }
        }
    }
}
