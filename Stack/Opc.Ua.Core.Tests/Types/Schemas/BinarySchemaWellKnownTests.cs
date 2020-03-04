/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Schema.Binary;

namespace Opc.Ua.Core.Tests.Types.Schemas
{
    /// <summary>
    /// Tests for the Binary Schema Validator class.
    /// </summary>
    [TestFixture, Category("BinarySchema")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class BinarySchemaWellKnownTests : BinarySchemaValidator
    {
        #region DataPointSources
        [DatapointSource]
        public string[][] WellKnownSchemaData = WellKnownDictionaries;
        #endregion

        #region Test Methods
        /// <summary>
        /// Load well known resource type dictionaries.
        /// </summary>
        [Theory]
        public void LoadResources(string[] schemaData)
        {
            Assert.That(schemaData.Length == 2);
            var assembly = typeof(BinarySchemaValidator).GetTypeInfo().Assembly;
            var resource = LoadResource(typeof(TypeDictionary), schemaData[1], assembly);
            Assert.IsNotNull(resource);
            Assert.AreEqual(resource.GetType(), typeof(TypeDictionary));
        }

        /// <summary>
        /// Load and validate well known resource type dictionaries.
        /// </summary>
        [Theory]
        public async Task ValidateResources(string[] schemaData)
        {
            var assembly = typeof(BinarySchemaValidator).GetTypeInfo().Assembly;
            var stream = assembly.GetManifestResourceStream(schemaData[1]);
            Assert.IsNotNull(stream);
            var schema = new BinarySchemaValidator();
            Assert.IsNotNull(schema);
            await schema.Validate(stream);
            Assert.IsNotNull(schema.Dictionary);
            Assert.AreEqual(schemaData[0], schema.Dictionary.TargetNamespace);
        }
        #endregion
    }

}
