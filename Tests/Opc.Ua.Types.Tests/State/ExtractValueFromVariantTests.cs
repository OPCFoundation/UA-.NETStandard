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

using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Tests for ExtractValueFromVariant method in BaseVariableState.
    /// </summary>
    [TestFixture]
    [Category("ExtractValueFromVariant")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ExtractValueFromVariantTests
    {
        private IServiceMessageContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_context = new ServiceMessageContext(m_telemetry);
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            CoreUtils.SilentDispose(m_context);
        }

        /// <summary>
        /// Test that setting a value wrapped in an ExtensionObject extracts the body correctly.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromExtensionObject()
        {
            // Create a PropertyState for Argument type (IEncodeable)
            var propertyState = new PropertyState<Argument>(null);

            // Create an Argument (IEncodeable type that can be in ExtensionObject)
            var testArg = new Argument("arg1", DataTypeIds.String, -1, "test description");
            
            // Wrap in ExtensionObject
            var extensionObject = new ExtensionObject(testArg);

            // Set the value using the base Value property (object type)
            // This should trigger ExtractValueFromVariant to unwrap the ExtensionObject
            ((BaseVariableState)propertyState).Value = extensionObject;

            // The value should be extracted from the ExtensionObject
            Assert.IsNotNull(propertyState.Value);
            Assert.AreEqual("arg1", propertyState.Value.Name);
            Assert.AreEqual("test description", propertyState.Value.Description.Text);
        }

        /// <summary>
        /// Test that setting a value wrapped in an ExtensionObject for a complex type extracts correctly.
        /// </summary>
        [Test]
        public void PropertyStateExtractsComplexTypeFromExtensionObject()
        {
            // Create a PropertyState for RelativePath type (IEncodeable)
            var propertyState = new PropertyState<RelativePath>(null);

            // Create a RelativePath (IEncodeable type)
            var testValue = new RelativePath
            {
                Elements = new RelativePathElementCollection
                {
                    new RelativePathElement
                    {
                        TargetName = new QualifiedName("TestName"),
                        IsInverse = false
                    }
                }
            };
            
            var extensionObject = new ExtensionObject(testValue);

            // Set the value
            ((BaseVariableState)propertyState).Value = extensionObject;

            // The value should be extracted
            Assert.IsNotNull(propertyState.Value);
            Assert.IsNotNull(propertyState.Value.Elements);
            Assert.AreEqual(1, propertyState.Value.Elements.Count);
            Assert.AreEqual("TestName", propertyState.Value.Elements[0].TargetName.Name);
        }

        /// <summary>
        /// Test that setting a direct value (not wrapped) still works correctly.
        /// </summary>
        [Test]
        public void PropertyStateAcceptsDirectValue()
        {
            var propertyState = new PropertyState<string>(null);
            var testString = "DirectValue";

            // Set value directly (not in ExtensionObject)
            ((BaseVariableState)propertyState).Value = testString;

            Assert.AreEqual(testString, propertyState.Value);
        }

        /// <summary>
        /// Test that setting null value works correctly.
        /// </summary>
        [Test]
        public void PropertyStateAcceptsNullValue()
        {
            var propertyState = new PropertyState<string>(null);

            // Set null value
            ((BaseVariableState)propertyState).Value = null;

            Assert.IsNull(propertyState.Value);
        }

        /// <summary>
        /// Test with BaseDataVariableState to ensure the fix works for all variable types.
        /// </summary>
        [Test]
        public void BaseDataVariableStateExtractsValueFromExtensionObject()
        {
            var variableState = new BaseDataVariableState<Argument>(null);

            // Create an Argument (IEncodeable type)
            var testArg = new Argument("testArg", DataTypeIds.Int32, -1, "test description");
            var extensionObject = new ExtensionObject(testArg);

            ((BaseVariableState)variableState).Value = extensionObject;

            Assert.IsNotNull(variableState.Value);
            Assert.AreEqual("testArg", variableState.Value.Name);
        }

        /// <summary>
        /// Test that Variant values are properly unwrapped.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromVariant()
        {
            var propertyState = new PropertyState<string>(null);
            var testString = "VariantValue";
            var variant = new Variant(testString);

            // Use WrappedValue property which calls ExtractValueFromVariant
            propertyState.WrappedValue = variant;

            Assert.AreEqual(testString, propertyState.Value);
        }

        /// <summary>
        /// Test that Variant with ExtensionObject is properly unwrapped.
        /// </summary>
        [Test]
        public void PropertyStateExtractsValueFromVariantWithExtensionObject()
        {
            var propertyState = new PropertyState<Argument>(null);
            var testArg = new Argument("variantArg", DataTypeIds.Double, -1, "test description");
            var extensionObject = new ExtensionObject(testArg);
            var variant = new Variant(extensionObject);

            // Use WrappedValue property
            propertyState.WrappedValue = variant;

            Assert.IsNotNull(propertyState.Value);
            Assert.AreEqual("variantArg", propertyState.Value.Name);
        }
    }
}
