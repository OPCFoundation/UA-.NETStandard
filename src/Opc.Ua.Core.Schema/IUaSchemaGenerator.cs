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

namespace Opc.Ua.Schema
{
    /// <summary>
    /// A generator that produces a schema for a single supported
    /// <see cref="UaSchemaFormat"/>. Implementations are registered with the
    /// dependency injection container and selected by the
    /// <see cref="ISchemaProvider"/> based on the requested format.
    /// </summary>
    public interface IUaSchemaGenerator
    {
        /// <summary>
        /// Returns whether the generator supports the requested format.
        /// </summary>
        /// <param name="format">The requested schema format.</param>
        /// <returns><c>true</c> when the format is supported.</returns>
        bool CanGenerate(UaSchemaFormat format);

        /// <summary>
        /// Generates the schema for the supplied data type description.
        /// </summary>
        /// <param name="type">The data type to generate a schema for.</param>
        /// <param name="resolver">The resolver used to look up referenced types.</param>
        /// <param name="format">The schema format to generate.</param>
        /// <param name="scope">The scope of the generated schema.</param>
        /// <returns>The generated schema.</returns>
        IUaSchema Generate(
            UaTypeDescription type,
            IDataTypeDefinitionResolver resolver,
            UaSchemaFormat format,
            UaSchemaScope scope);
    }
}
