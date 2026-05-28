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

using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.ComplexTypes;

namespace Opc.Ua.ComplexTypes.Emit
{
    /// <summary>
    /// Static methods that produce a <see cref="ComplexTypeSystem"/>
    /// backed by the Reflection.Emit-based
    /// <see cref="ComplexTypeBuilderFactory"/>. Hosts that need runtime
    /// concrete .NET classes for custom DataTypes use these instead of
    /// the default (AOT-friendly) <see cref="ComplexTypeSystem"/>
    /// constructors, which use <see cref="DefaultComplexTypeFactory"/>.
    /// </summary>
    public static class ComplexTypesEmitExtensions
    {
        extension(ComplexTypeSystem)
        {
            /// <summary>
            /// Initializes a <see cref="ComplexTypeSystem"/> bound to
            /// <paramref name="session"/> that materialises custom
            /// DataTypes as runtime .NET classes via
            /// <see cref="ComplexTypeBuilderFactory"/>.
            /// </summary>
            public static ComplexTypeSystem CreateWithReflectionEmit(
                ISession session,
                ITelemetryContext telemetry)
            {
                return new ComplexTypeSystem(
                    session,
                    new ComplexTypeBuilderFactory(),
                    telemetry);
            }

            /// <summary>
            /// Initializes a <see cref="ComplexTypeSystem"/> bound to a
            /// caller-supplied <see cref="IComplexTypeResolver"/> that
            /// materialises custom DataTypes as runtime .NET classes
            /// via <see cref="ComplexTypeBuilderFactory"/>.
            /// </summary>
            public static ComplexTypeSystem CreateWithReflectionEmit(
                IComplexTypeResolver complexTypeResolver,
                ITelemetryContext telemetry)
            {
                return new ComplexTypeSystem(
                    complexTypeResolver,
                    new ComplexTypeBuilderFactory(),
                    telemetry);
            }
        }
    }
}
