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

using System;
using System.Collections.Generic;
#if NET
using System.Runtime.InteropServices;
#endif
using System.Threading;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Binds the PKCS#11 module a token talks to.
    /// </summary>
    /// <remarks>
    /// Loading the module is the only step that needs a real device present, so
    /// it is the only step behind an interface. Everything the stack does with a
    /// token - selecting a slot, logging in, finding objects, signing and
    /// decrypting - is expressed against the interfaces
    /// <c>Pkcs11Interop</c> already provides, and is therefore exercisable
    /// wherever the tests run rather than only where a device is attached.
    /// </remarks>
    internal interface IPkcs11LibraryLoader
    {
        /// <summary>
        /// Loads the module at a path.
        /// </summary>
        /// <param name="factories">The factories the library is built from.</param>
        /// <param name="modulePath">The file system path of the module.</param>
        /// <returns>The loaded library.</returns>
        IPkcs11Library Load(Pkcs11InteropFactories factories, string modulePath);
    }

    /// <summary>
    /// Loads the module through <c>Pkcs11Interop</c>.
    /// </summary>
    /// <remarks>
    /// Each module that loads successfully is also pinned for the lifetime of
    /// the process. Disposing an <see cref="IPkcs11Library"/> unloads the
    /// module, but a module such as SoftHSM can leave thread-specific-data
    /// destructors behind (from itself or from the crypto library it pulls in).
    /// A worker thread that exits after the unload then calls into unmapped
    /// code and the process crashes. Holding one extra loader reference keeps
    /// the image mapped; <c>C_Initialize</c> and <c>C_Finalize</c> still run
    /// per library exactly as before.
    /// </remarks>
    internal sealed class DefaultPkcs11LibraryLoader : IPkcs11LibraryLoader
    {
        /// <summary>
        /// Creates a loader that pins modules through the given delegate.
        /// </summary>
        /// <param name="tryPin">
        /// Takes one additional, never released, native reference on a module
        /// path and reports whether it succeeded.
        /// </param>
        internal DefaultPkcs11LibraryLoader(Func<string, bool> tryPin)
        {
            m_tryPin = tryPin ?? throw new ArgumentNullException(nameof(tryPin));
        }

        /// <summary>
        /// The shared instance.
        /// </summary>
        public static DefaultPkcs11LibraryLoader Instance { get; } = new(TryPinNativeModule);

        /// <inheritdoc/>
        public IPkcs11Library Load(Pkcs11InteropFactories factories, string modulePath)
        {
            IPkcs11Library library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                factories,
                modulePath,
                AppType.MultiThreaded);
            Pin(modulePath);
            return library;
        }

        /// <summary>
        /// Pins a module path once per loader; later calls for the same path
        /// do not take further references.
        /// </summary>
        /// <param name="modulePath">The module path that was loaded.</param>
        /// <returns><c>true</c> if this call attempted the pin.</returns>
        internal bool Pin(string modulePath)
        {
            lock (m_pinLock)
            {
                if (!m_pinned.Add(modulePath))
                {
                    return false;
                }
            }
            m_tryPin(modulePath);
            return true;
        }

        private static bool TryPinNativeModule(string modulePath)
        {
#if NET
            return NativeLibrary.TryLoad(modulePath, out _);
#else
            return false;
#endif
        }

        private readonly Func<string, bool> m_tryPin;
        private readonly Lock m_pinLock = new();
        private readonly HashSet<string> m_pinned = new(StringComparer.Ordinal);
    }
}
