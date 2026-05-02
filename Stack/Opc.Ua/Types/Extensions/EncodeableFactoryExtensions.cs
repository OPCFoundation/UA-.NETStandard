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
                if (!EncodeableFactory.Root.IsValueCreated
                    ||
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
