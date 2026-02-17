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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua
{
    /// <summary>
    /// NodeId Obsoleted static methods
    /// </summary>
    public static class NodeIdStaticObsolete
    {
        extension(NodeId)
        {
            /// <summary>
            /// Checks if the node id represents a 'Null' node id.
            /// </summary>
            /// <remarks>
            /// Returns a true/false value to indicate if the specified NodeId is null.
            /// </remarks>
            /// <param name="nodeId">The NodeId to validate</param>
            [Obsolete("Use NodeId.IsNull property instead.")]
            public static bool IsNull([NotNullWhen(false)] NodeId nodeId)
            {
                return nodeId.IsNull;
            }

            /// <summary>
            /// Checks if the node id represents a 'Null' node id.
            /// </summary>
            /// <remarks>
            /// Returns a true/false to indicate if the specified <see cref="ExpandedNodeId"/> is null.
            /// </remarks>
            /// <param name="nodeId">The ExpandedNodeId to validate</param>
            [Obsolete("Use ExpandedNodeId.IsNull property instead.")]
            public static bool IsNull([NotNullWhen(false)] ExpandedNodeId nodeId)
            {
                return nodeId.IsNull;
            }
        }
    }

    /// <summary>
    /// Obsoleted members
    /// </summary>
    public static class NodeIdObsolete
    {
        extension(NodeId nodeId)
        {
            /// <summary>
            /// Returns true if the node id is null
            /// </summary>
            [Obsolete("Use NodeId.IsNull property instead.")]
            public bool IsNullNodeId => nodeId.IsNull;
        }
    }

    /// <summary>
    /// QualifiedName obsolete static methods
    /// </summary>
    public static class QualifiedNameStaticObsolete
    {
        extension(QualifiedName)
        {
            /// <summary>
            /// Returns true if the text is a null or empty string.
            /// </summary>
            [Obsolete("Use QualifiedName.IsNull property instead.")]
            public static bool IsNull([NotNullWhen(false)] QualifiedName value)
            {
                return value.IsNull;
            }
        }
    }

    /// <summary>
    /// QualifiedName obsolete members
    /// </summary>
    public static class QualifiedNameObsolete
    {
        extension(QualifiedName qualifiedName)
        {
            /// <summary>
            /// Returns true if the node id is null
            /// </summary>
            [Obsolete("Use QualifiedName.IsNull property instead.")]
            public bool IsNullQn => qualifiedName.IsNull;
        }
    }

    /// <summary>
    /// LocalizedText extensions
    /// </summary>
    public static class LocalizedTextObsolete
    {
        extension(LocalizedText)
        {
            /// <summary>
            /// Returns true if the text is a null or empty string.
            /// </summary>
            [Obsolete("Use LocalizedText.IsNullOrEmpty property instead.")]
            public static bool IsNullOrEmpty(LocalizedText value)
            {
                return value.IsNullOrEmpty;
            }
        }
    }

    /// <summary>
    /// Extension object extensions
    /// </summary>
    public static class ExtensionObjectObsolete
    {
        extension(ExtensionObject)
        {
            /// <summary>
            /// Tests if the extension or embed objects are null value.
            /// </summary>
            /// <param name="extension">The object to check if null</param>
            /// <returns>
            /// <c>true</c> if the specified <paramref name="extension"/> is null
            /// of the embedded object is null; otherwise, <c>false</c>.
            /// </returns>
            public static bool IsNull([NotNullWhen(false)] ExtensionObject extension)
            {
                return extension.IsNull;
            }
        }
    }

    /// <summary>
    /// Status code extensions
    /// </summary>
    public static class StatusCodeObsolete
    {
        extension(StatusCode statusCode)
        {
            /// <summary>
            /// Set code bits
            /// </summary>
            [Obsolete("Use StatusCode.WithCodeBits instead.")]
            public StatusCode SetCodeBits(uint bits)
            {
                return statusCode.WithCodeBits(bits);
            }

            /// <summary>
            /// Set code bits
            /// </summary>
            [Obsolete("Use StatusCode.WithCodeBits instead.")]
            public StatusCode SetCodeBits(StatusCode code)
            {
                return statusCode.WithCodeBits(code);
            }

            /// <summary>
            /// Set flag bits
            /// </summary>
            [Obsolete("Use StatusCode.WithFlagBits instead.")]
            public StatusCode SetFlagBits(uint bits)
            {
                return statusCode.WithFlagBits(bits);
            }

            /// <summary>
            /// Set Limit bits
            /// </summary>
            [Obsolete("Use StatusCode.WithLimitBits instead.")]
            public StatusCode SetLimitBits(LimitBits bits)
            {
                return statusCode.WithLimitBits(bits);
            }

            /// <summary>
            /// Set Limit bits
            /// </summary>
            [Obsolete("Use StatusCode.WithAggregateBits instead.")]
            public StatusCode SetAggregateBits(AggregateBits bits)
            {
                return statusCode.WithAggregateBits(bits);
            }

            /// <summary>
            /// Set sub code
            /// </summary>
            [Obsolete("Use StatusCode.WithSubCode instead.")]
            public StatusCode SetSubCode(uint subCode)
            {
                return statusCode.WithSubCode(subCode);
            }
        }
    }
}
