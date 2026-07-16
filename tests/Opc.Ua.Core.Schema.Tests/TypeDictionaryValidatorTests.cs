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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Schema.Types;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Direct coverage tests for <see cref="TypeDictionaryValidator"/> using
    /// self-contained type dictionaries fed through the stream overload.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    [SetCulture("en-us")]
    public class TypeDictionaryValidatorTests
    {
        /// <summary>
        /// The target namespace of the validated dictionary.
        /// </summary>
        private const string TestNamespace = "http://test.org/UA/typedict";

        /// <summary>
        /// The namespace of the imported built-in type dictionary.
        /// </summary>
        private const string BuiltInNamespace = "http://opcfoundation.org/UA/BuiltInTypes/";

        /// <summary>
        /// A sample of built-in type names expected in the imported dictionary.
        /// </summary>
        private static readonly string[] ExpectedBuiltInNames = ["Boolean", "Int32", "String", "Variant"];

        /// <summary>
        /// A valid dictionary that imports the built-in types and defines an
        /// enumeration, a base and derived complex type and a type declaration.
        /// </summary>
        private const string ValidDictionary =
            """
            <TypeDictionary
                xmlns="http://opcfoundation.org/UA/TypeDictionary/"
                xmlns:ua="http://opcfoundation.org/UA/BuiltInTypes/"
                xmlns:tns="http://test.org/UA/typedict"
                TargetNamespace="http://test.org/UA/typedict">
              <Import Namespace="http://opcfoundation.org/UA/BuiltInTypes/" />
              <EnumeratedType Name="CoverageEnum">
                <Value Name="Red" Value="0" />
                <Value Name="Green" Value="1" />
              </EnumeratedType>
              <ComplexType Name="CoverageBase">
                <Field Name="Id" DataType="ua:Int32" />
              </ComplexType>
              <ComplexType Name="CoverageDerived" BaseType="tns:CoverageBase">
                <Field Name="Shade" DataType="tns:CoverageEnum" />
                <Field Name="Label" DataType="ua:String" />
              </ComplexType>
              <TypeDeclaration Name="CoverageAlias" SourceType="tns:CoverageDerived" />
            </TypeDictionary>
            """;

        /// <summary>
        /// An enumerated type without any values is rejected by the validator.
        /// </summary>
        private const string EnumWithoutValuesDictionary =
            """
            <TypeDictionary
                xmlns="http://opcfoundation.org/UA/TypeDictionary/"
                TargetNamespace="http://test.org/UA/typedict">
              <EnumeratedType Name="EmptyEnum" />
            </TypeDictionary>
            """;

        [Test]
        public void ValidateValidDictionaryPopulatesDictionaryAndImports()
        {
            TypeDictionaryValidator validator = CreateValidatedValidator();

            Assert.That(validator.Dictionary, Is.Not.Null);
            TypeDictionary dictionary = validator.Dictionary!;
            TypeDictionary[] loaded = [.. validator.LoadedTypeDictionaries];
            Assert.Multiple(() =>
            {
                Assert.That(dictionary.TargetNamespace, Is.EqualTo(TestNamespace));
                Assert.That(dictionary.Items, Has.Length.EqualTo(4));
                Assert.That(loaded, Has.Length.EqualTo(1));
                Assert.That(loaded[0].TargetNamespace, Is.EqualTo(BuiltInNamespace));
                Assert.That(loaded[0].Items, Has.Length.EqualTo(25));
                Assert.That(loaded[0].Items!.Select(d => d.Name),
                    Is.SupersetOf(ExpectedBuiltInNames));
            });
        }

        [Test]
        public void FindTypeResolvesLocalAndImportedTypes()
        {
            TypeDictionaryValidator validator = CreateValidatedValidator();

            DataType? derived = validator.FindType(new XmlQualifiedName("CoverageDerived", TestNamespace));
            DataType? builtIn = validator.FindType(new XmlQualifiedName("Int32", BuiltInNamespace));
            DataType? missing = validator.FindType(new XmlQualifiedName("DoesNotExist", TestNamespace));

            Assert.Multiple(() =>
            {
                Assert.That(derived, Is.Not.Null);
                Assert.That(derived, Is.InstanceOf<ComplexType>());
                Assert.That(derived!.Name, Is.EqualTo("CoverageDerived"));
                Assert.That(builtIn, Is.Not.Null);
                Assert.That(builtIn!.Name, Is.EqualTo("Int32"));
                Assert.That(missing, Is.Null);
            });
        }

        [Test]
        public void ResolveTypeFollowsTypeDeclarationToConcreteType()
        {
            TypeDictionaryValidator validator = CreateValidatedValidator();

            DataType? resolvedAlias = validator.ResolveType(new XmlQualifiedName("CoverageAlias", TestNamespace));
            DataType? resolvedEnum = validator.ResolveType(new XmlQualifiedName("CoverageEnum", TestNamespace));

            Assert.Multiple(() =>
            {
                Assert.That(resolvedAlias, Is.Not.Null);
                Assert.That(resolvedAlias, Is.InstanceOf<ComplexType>());
                Assert.That(resolvedAlias!.Name, Is.EqualTo("CoverageDerived"));
                Assert.That(resolvedEnum, Is.Not.Null);
                Assert.That(resolvedEnum, Is.InstanceOf<EnumeratedType>());
                Assert.That(resolvedEnum!.Name, Is.EqualTo("CoverageEnum"));
            });
        }

        [Test]
        public void ResolveTypeReturnsNullForEmptyOrUnknownName()
        {
            TypeDictionaryValidator validator = CreateValidatedValidator();

            DataType? resolvedEmpty = validator.ResolveType(new XmlQualifiedName());
            DataType? resolvedUnknown = validator.ResolveType(new XmlQualifiedName("Unknown", TestNamespace));

            Assert.Multiple(() =>
            {
                Assert.That(resolvedEmpty, Is.Null);
                Assert.That(resolvedUnknown, Is.Null);
            });
        }

        [Test]
        public void ValidateEnumerationWithoutValuesThrowsInvalidOperationException()
        {
            var validator = new TypeDictionaryValidator(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(EnumWithoutValuesDictionary));

            InvalidOperationException? exception =
                Assert.Throws<InvalidOperationException>(() => validator.Validate(stream));

            Assert.That(exception!.Message, Does.Contain("EmptyEnum"));
        }

        [Test]
        public void IsExcludedMatchesReleaseStatusPurposeAndCategory()
        {
            var draftType = new DataType { ReleaseStatus = ReleaseStatus.Draft, Category = "Experimental" };
            var generatorType = new DataType { Purpose = DataTypePurpose.CodeGenerator };
            string[] draft = ["Draft"];
            string[] experimental = ["Experimental"];
            string[] released = ["Released"];
            string[] codeGenerator = ["CodeGenerator"];
            string[] normal = ["Normal"];

            Assert.Multiple(() =>
            {
                Assert.That(TypeDictionaryValidator.IsExcluded(draft, draftType), Is.True);
                Assert.That(TypeDictionaryValidator.IsExcluded(experimental, draftType), Is.True);
                Assert.That(TypeDictionaryValidator.IsExcluded(released, draftType), Is.False);
                Assert.That(TypeDictionaryValidator.IsExcluded(null, draftType), Is.False);
                Assert.That(TypeDictionaryValidator.IsExcluded(codeGenerator, generatorType), Is.True);
                Assert.That(TypeDictionaryValidator.IsExcluded(normal, generatorType), Is.False);
            });
        }

        [Test]
        public void IsExcludedMatchesReleaseStatusForEnumeratedValueAndField()
        {
            var deprecatedValue = new EnumeratedValue { ReleaseStatus = ReleaseStatus.Deprecated };
            var candidateField = new FieldType { ReleaseStatus = ReleaseStatus.RC };
            string[] deprecated = ["Deprecated"];
            string[] released = ["Released"];
            string[] candidate = ["RC"];

            Assert.Multiple(() =>
            {
                Assert.That(TypeDictionaryValidator.IsExcluded(deprecated, deprecatedValue), Is.True);
                Assert.That(TypeDictionaryValidator.IsExcluded(released, deprecatedValue), Is.False);
                Assert.That(TypeDictionaryValidator.IsExcluded(candidate, candidateField), Is.True);
                Assert.That(TypeDictionaryValidator.IsExcluded(released, candidateField), Is.False);
            });
        }

        private static TypeDictionaryValidator CreateValidatedValidator()
        {
            var validator = new TypeDictionaryValidator(LocalFileSystem.Instance);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidDictionary));
            validator.Validate(stream);
            return validator;
        }
    }
}
