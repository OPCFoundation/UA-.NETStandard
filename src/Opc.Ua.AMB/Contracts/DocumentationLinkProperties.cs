/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The metadata Properties the AMB server of this SDK adds to the links
    /// of a <c>DocumentationLinks</c> AddIn, as OPC 10000-110 §10.5.1 lets
    /// vendors do.
    /// </summary>
    /// <remarks>
    /// OPC 10000-110 defines no way to tell a link of the manufacturer from
    /// one a user added through <c>AddLink</c>, although only the latter can
    /// be removed with <c>RemoveLink</c> (§10.5.4). The names are not AMB
    /// names: the server qualifies them with its namespace of server-specific
    /// types, which an application may configure, so a client matches the
    /// name alone.
    /// </remarks>
    public static class DocumentationLinkProperties
    {
        /// <summary>
        /// The <c>Boolean</c> Property, <see langword="true"/>, a link a user
        /// added through <c>AddLink</c> carries, and only such a link. It is
        /// the link <c>RemoveLink</c> accepts.
        /// </summary>
        public const string UserLink = "UserLink";
    }
}
