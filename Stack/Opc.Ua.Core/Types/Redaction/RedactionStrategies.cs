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

namespace Opc.Ua.Redaction
{
    /// <summary>
    /// Collection of the redaction strategies.
    /// The redaction (censoring) can be applied to log message parameters, exception messages, and other sensitive data.
    /// <br/>
    /// To use a strategy wrap the value with <see cref="RedactionWrapper{T}"/>, e.g.
    /// <code>
    /// Utils.LogDebug("The password is {0}", RedactionWrapper.Create(password));
    /// Utils.LogError("An exception occurred: {0}", RedactionWrapper.Create(exception));
    /// </code>
    /// </summary>
    /// <remarks>
    /// The redaction is off by default and can be enabled by implementing <see cref="IRedactionStrategy"/>
    /// and setting it via <see cref="SetStrategy(IRedactionStrategy)"/>.
    /// </remarks>
    public static partial class RedactionStrategies
    {
        private static readonly IRedactionStrategy s_fallbackStrategy = new FallbackRedactionStrategy();

        /// <summary>
        /// Gets the current redaction strategy.
        /// </summary>
        internal static IRedactionStrategy CurrentStrategy { get; private set; } = s_fallbackStrategy;

        /// <summary>
        /// Sets the current redaction strategy.
        /// </summary>
        public static void SetStrategy(IRedactionStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }

            CurrentStrategy = strategy;
        }

        /// <summary>
        /// Resets the fallback strategy to the default (empty) implementation.
        /// </summary>
        public static void ResetStrategy()
        {
            CurrentStrategy = s_fallbackStrategy;
        }

        /// <summary>
        /// Fallback for when no other strategy was set. It returns the string representation of the value.
        /// </summary>
        private class FallbackRedactionStrategy : IRedactionStrategy
        {
            public string Redact(object value)
            {
                return value?.ToString() ?? "null";
            }
        }
    }
}
