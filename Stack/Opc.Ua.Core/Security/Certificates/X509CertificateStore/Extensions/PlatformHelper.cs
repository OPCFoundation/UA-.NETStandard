/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.X509StoreExtensions
{
    /// <summary>
    /// Static Helper class to retrieve if executed on a specific operating system
    /// </summary>
    internal static class PlatformHelper
    {
        private static bool? _isWindowsWithCrlSupport = null;
        /// <summary>
        /// True if OS Windows and Version >= Windows XP
        /// </summary>
        /// <returns>True if Crl Support is given in the system X509 Store</returns>
        public static bool IsWindowsWithCrlSupport()
        {
            if (_isWindowsWithCrlSupport != null)
            {
                return _isWindowsWithCrlSupport.Value;
            }
            OperatingSystem version = Environment.OSVersion;
            _isWindowsWithCrlSupport = version.Platform == PlatformID.Win32NT
                && (
                       (version.Version.Major > 5)
                        || (version.Version.Major == 5 && version.Version.Minor >= 1)
                    );
            return _isWindowsWithCrlSupport.Value;
        }
    }
}
