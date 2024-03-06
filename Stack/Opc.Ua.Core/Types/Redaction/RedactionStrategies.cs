/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;

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
    /// The redaction is off by default and can be enabled by adding <see cref="IRedactionStrategy"/> implementations
    /// via <see cref="AddStrategy(IRedactionStrategy)"/>.
    /// <br/>
    /// Suggestions for adding redaction strategies:
    /// <list type="bullet">
    ///   <item>add the redaction strategies early on in your application's lifecycle,</item>
    ///   <item>the strategies should be added in order of their priority: more specific ones first</item>
    ///   <item>lastly add a fallback strategy that handles any incoming type</item>
    ///   <item>it's recommended to add strategies for
    ///     <list type="bullet">
    ///       <item><see cref="Uri"/></item>
    ///       <item><see cref="UriBuilder"/></item>
    ///       <item><see cref="Exception"/></item>
    ///       <item><see cref="string"/></item>
    ///     </list>
    ///   </item>
    /// </list>
    /// </remarks>
    public static partial class RedactionStrategies
    {
        private static readonly IRedactionStrategy m_fallbackStrategy = new NullRedactionStrategy();
        private static readonly List<IRedactionStrategy> m_strategies = new List<IRedactionStrategy>();

        /// <summary>
        /// Add a redaction strategy to the collection.
        /// </summary>
        public static void AddStrategy(IRedactionStrategy strategy)
        {
            m_strategies.Add(strategy);
        }

        /// <summary>
        /// Get the redaction strategy for the specified type.
        /// </summary>
        public static IRedactionStrategy GetRedactionStrategyForType(Type type)
        {
            return m_strategies.FirstOrDefault(s => s.CanRedact(type))
                ?? m_fallbackStrategy;
        }

        /// <summary>
        /// Fallback for when no other strategy can redact the type. It returns the string representation of the value.
        /// </summary>
        private class NullRedactionStrategy : IRedactionStrategy
        {
            public string Redact(object value)
            {
                return value?.ToString() ?? "null";
            }

            public bool CanRedact(Type type)
            {
                return true;
            }
        }
    }
}
