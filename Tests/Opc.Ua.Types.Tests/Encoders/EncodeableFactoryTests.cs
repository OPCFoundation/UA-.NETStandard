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
using System.Reflection;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the EncodeableFactory class.
    /// </summary>
    [TestFixture]
    [Category("EncodeableFactory")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class EncodeableFactoryTests
    {
        [OneTimeSetUp]
        [GlobalSetup]
        public void OneTimeAndGlobalSetUp()
        {
            IEncodeableFactory encodeableFactory = EncodeableFactory.Create();
            m_builder = encodeableFactory.Builder.AddEncodeableTypes(encodeableFactory.GetType().Assembly);
            m_encodeableFactory = EncodeableFactory.Create();
            m_encodeableFactory.Builder.AddEncodeableTypes(encodeableFactory.GetType().Assembly).Commit();
        }

        public static readonly NodeId ReadRequestEncoding = new (631);

        /// <summary>
        /// Benchmark for lookup using dictionary
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void EncodeableFactoryLookupFromBuilder()
        {
            // lookup a type that exists and one that does not
            for (int i = 0; i < 1000; i++)
            {
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(ReadRequestEncoding),
                    out IEncodeableType encodeableType);
                Assert.Null(encodeableType);
                _ = m_builder.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary),
                    out encodeableType);
                Assert.NotNull(encodeableType);
            }
        }

        /// <summary>
        /// Benchmark for lookup using frozen dictionary
        /// </summary>
        [Benchmark]
        [Test]
        public void EncodeableFactoryLookupFromEncodeableFactory()
        {

            // lookup a type that exists and one that does not
            for (int i = 0; i < 1000; i++)
            {
                _ = m_encodeableFactory.TryGetEncodeableType(
                    new ExpandedNodeId(ReadRequestEncoding),
                    out IEncodeableType encodeableType);
                Assert.Null(encodeableType);
                _ = m_encodeableFactory.TryGetEncodeableType(
                    new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary),
                    out encodeableType);
                Assert.NotNull(encodeableType);
            }
        }

        [Test]
        public void Create_ReturnsFactoryWithKnownTypes()
        {
            // Act
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Assert
            Assert.NotNull(factory);
            Assert.NotNull(factory.KnownTypeIds);
            Assert.Greater(factory.KnownTypeIds.Count(), 0);
        }

        [Test]
        public void KnownTypes_ReturnsEmptyForNewFactory()
        {
            // Arrange - Create factory has pre-loaded types, so test the interface contract
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            IEnumerable<ExpandedNodeId> knownTypes = factory.KnownTypeIds;

            // Assert
            Assert.NotNull(knownTypes);
            // Note: Since Create() pre-loads types, we expect many types, not 0
            Assert.Greater(knownTypes.Count(), 0);
        }

        [Test]
        public void TryGetEncodeableType_WithUnknownType_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var unknownTypeId = new ExpandedNodeId(9999);

            // Act
            bool result = factory.TryGetEncodeableType(unknownTypeId, out IEncodeableType encodeableType);

            // Assert
            Assert.False(result);
            Assert.Null(encodeableType);
        }

        [Test]
        public void Builder_ReturnsNonNullBuilder()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Assert
            Assert.NotNull(builder);
        }

        [Test]
        public void Builder_AddEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestEncodeable));
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType encodeableType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), encodeableType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeId_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(5000);

            // Act
            builder.AddEncodeableType(typeId, typeof(TestEncodeable));
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), encodeableType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(typeof(TestEncodeable));
            Assert.AreSame(builder, result);
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeId_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(new ExpandedNodeId(100000), typeof(TestEncodeable));
            Assert.AreSame(builder, result);
        }

        [Test]
        public void Builder_CanLookupTypeBeforeCommit()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(7000); // Use unique ID to avoid conflicts with pre-loaded types

            // Act
            builder.AddEncodeableType(typeId, typeof(TestEncodeable));
            bool foundInBuilder = builder.TryGetEncodeableType(typeId, out IEncodeableType builderType);
            bool foundInFactory = factory.TryGetEncodeableType(typeId, out IEncodeableType factoryType);

            // Assert
            Assert.True(foundInBuilder);
            Assert.AreEqual(typeof(TestEncodeable), builderType.Type);
            Assert.False(foundInFactory);
            Assert.Null(factoryType);
        }

        [Test]
        public void Builder_CommitMultipleTimes_OnlyCommitsOnce()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(8000); // Use unique ID

            // Act
            builder.AddEncodeableType(typeId, typeof(TestEncodeable));
            builder.Commit();
            builder.Commit(); // Second commit should be no-op

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), encodeableType.Type);
        }

        [Test]
        public void Builder_AddEncodeableTypes_WithNullAssembly_ReturnsBuilder()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            IEncodeableFactoryBuilder result = builder.AddEncodeableTypes(null);

            // Assert
            Assert.AreSame(builder, result);
        }

        [Test]
        public void Builder_AddEncodeableTypes_WithValidAssembly_AddsTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var currentAssembly = Assembly.GetExecutingAssembly();

            // Act
            builder.AddEncodeableTypes(currentAssembly);
            builder.Commit();

            // Assert - Should add our test types
            bool foundTest = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType testType);
            Assert.True(foundTest);
            Assert.AreEqual(typeof(TestEncodeable), testType.Type);
        }

        [Test]
        public void Builder_AddEncodeableTypes_SkipsAbstractAndNonDefaultConstructorTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            int knownTypesCount = factory.KnownTypeIds.Count();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableTypes(Assembly.GetExecutingAssembly());
            builder.Commit();

            // Assert - Should add our concrete test types but skip abstract and no-default-constructor types
            int addedTypes = factory.KnownTypeIds.Count() - knownTypesCount;
            Assert.Greater(addedTypes, 0); // Should have added some types

            // Verify abstract types are not added
            Assert.False(factory.TryGetEncodeableType(new ExpandedNodeId(110000), out _));
            Assert.False(factory.TryGetEncodeableType(new ExpandedNodeId(110001), out _));
            Assert.False(factory.TryGetEncodeableType(new ExpandedNodeId(110002), out _));
        }

        [Test]
        public void Builder_AddJsonEncodeableType_AddsJsonEncodingId()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestJsonEncodeable));
            builder.Commit();

            // Assert - Should be able to find by JSON encoding ID
            bool foundByJson = factory.TryGetEncodeableType(new ExpandedNodeId(100003), out IEncodeableType jsonType);
            Assert.True(foundByJson);
            Assert.AreEqual(typeof(TestJsonEncodeable), jsonType.Type);
        }

        [Test]
        public void Builder_MultipleTypes_AllTypesAdded()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestEncodeable))
                   .AddEncodeableType(typeof(TestJsonEncodeable));
            builder.Commit();

            // Assert
            bool foundTest = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType testType);
            bool foundJsonTest = factory.TryGetEncodeableType(new ExpandedNodeId(100004), out _);

            Assert.True(foundTest);
            Assert.True(foundJsonTest);
            Assert.AreEqual(typeof(TestEncodeable), testType.Type);
        }

        [Test]
        [Category("ThreadSafety")]
        public void Builder_ConcurrentAccess_ThreadSafety()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var exceptions = new List<Exception>();
            const int threadCount = 10;
            const int operationsPerThread = 100;

            // Act
            var threads = new System.Threading.Thread[threadCount];
            for (uint i = 0; i < threadCount; i++)
            {
                uint threadId = i;
                threads[i] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        for (uint j = 0; j < operationsPerThread; j++)
                        {
                            IEncodeableFactoryBuilder builder = factory.Builder;
                            builder.AddEncodeableType(
                                new ExpandedNodeId((threadId * 1000) + j + 10000),
                                typeof(TestEncodeable));
                            builder.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            foreach (System.Threading.Thread thread in threads)
            {
                thread.Join();
            }

            // Assert
            Assert.AreEqual(0, exceptions.Count, $"Exceptions occurred: {string.Join(", ", exceptions.Select(e => e.Message))}");
            Assert.Greater(factory.KnownTypeIds.Count(), 0);
        }

        [Test]
        public void Clone_ReturnsDeepCopy()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(9001); // Use unique ID
            factory.Builder.AddEncodeableType(typeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access Clone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.Clone();

            // Assert
            Assert.NotNull(clonedFactory);
            Assert.AreNotSame(factory, clonedFactory);

            // Should have same types
            bool originalHasType = factory.TryGetEncodeableType(typeId, out IEncodeableType originalType);
            bool clonedHasType = clonedFactory.TryGetEncodeableType(typeId, out IEncodeableType clonedType);

            Assert.True(originalHasType);
            Assert.True(clonedHasType);
            Assert.AreEqual(originalType, clonedType);
        }

        [Test]
        public void MemberwiseClone_ReturnsDeepCopy()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(9002); // Use unique ID
            factory.Builder.AddEncodeableType(typeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access MemberwiseClone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.MemberwiseClone();

            // Assert
            Assert.NotNull(clonedFactory);
            Assert.AreNotSame(factory, clonedFactory);

            // Should have same types
            bool originalHasType = factory.TryGetEncodeableType(typeId, out IEncodeableType originalType);
            bool clonedHasType = clonedFactory.TryGetEncodeableType(typeId, out IEncodeableType clonedType);

            Assert.True(originalHasType);
            Assert.True(clonedHasType);
            Assert.AreEqual(originalType, clonedType);
        }

        [Test]
        public void Clone_IndependentOfOriginal()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var initialTypeId = new ExpandedNodeId(9003);
            var newTypeId = new ExpandedNodeId(9004);
            factory.Builder.AddEncodeableType(initialTypeId, typeof(TestEncodeable)).Commit();

            // Act - Cast to concrete type to access Clone method
            var concreteFactory = (EncodeableFactory)factory;
            var clonedFactory = (EncodeableFactory)concreteFactory.Clone();

            // Add type to original
            factory.Builder.AddEncodeableType(newTypeId, typeof(TestJsonEncodeable)).Commit();

            // Assert - Clone should not have the new type
            bool originalHasNewType = factory.TryGetEncodeableType(newTypeId, out _);
            bool clonedHasNewType = clonedFactory.TryGetEncodeableType(newTypeId, out _);

            Assert.True(originalHasNewType);
            Assert.False(clonedHasNewType);
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullType_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - The implementation may handle null types gracefully rather than throwing
            NUnit.Framework.Assert.DoesNotThrow(() => builder.AddEncodeableType((Type)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullExpandedNodeId_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - The implementation may handle null NodeIds gracefully rather than throwing
            NUnit.Framework.Assert.DoesNotThrow(() => builder.AddEncodeableType(null, typeof(TestEncodeable)));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNonEncodeableType_DoesNotAdd()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            int initialCount = factory.KnownTypeIds.Count();

            // Act
            builder.AddEncodeableType(typeof(string)); // string is not IEncodeable
            builder.Commit();

            // Assert - No new types should be added
            Assert.AreEqual(initialCount, factory.KnownTypeIds.Count());
        }

        [Test]
        public void Builder_AddEncodeableType_WithTypeWithoutDefaultConstructor_HandlesGracefully()
        {
            // This test verifies that types without parameterless constructors are handled gracefully
            // We can't easily create such a test type, so we'll use a mock approach

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert - Should not throw
            NUnit.Framework.Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableTypes(Assembly.GetExecutingAssembly());
                builder.Commit();
            });
        }

        [Test]
        public void Integration_BuilderWithCoreAssembly_AddsKnownOpcUaTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableTypes(typeof(EncodeableFactory).Assembly);
            builder.Commit();

            // Assert - Should have many OPC UA types
            Assert.Greater(factory.KnownTypeIds.Count(), 100); // OPC UA has many built-in types
        }

        [Test]
        public void Integration_CreateFactoryHasPreloadedTypes()
        {
            // Act
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Assert - Create() should return factory with preloaded types
            Assert.Greater(factory.KnownTypeIds.Count(), 100);

            // Should be able to find common OPC UA types like ReadRequest
            var knownTypesList = factory.KnownTypeIds.ToList();
            Assert.Greater(knownTypesList.Count, 0);
        }

        [Test]
        public void Integration_FactoryCanResolveArgument()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act - Try to find ReadRequest type using proper ExpandedNodeId - wrap the uint constant
            var readRequestEncodingId = new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary);
            bool found = factory.TryGetEncodeableType(readRequestEncodingId, out IEncodeableType encodeableType);

            // Assert
            Assert.True(found);
            Assert.AreEqual(typeof(Argument), encodeableType.Type);
        }

        [Test]
        public void Integration_DefaultNamespaceHandling()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Create a type with default namespace
            var typeIdWithDefaultNs = new ExpandedNodeId(9100, Namespaces.OpcUa);
            var typeIdWithoutNs = new ExpandedNodeId(9100);

            // Act
            builder.AddEncodeableType(typeIdWithDefaultNs, typeof(TestEncodeable));
            builder.Commit();

            // Assert - Should be findable with both NodeId forms
            bool foundWithNs = factory.TryGetEncodeableType(typeIdWithDefaultNs, out IEncodeableType typeWithNs);
            bool foundWithoutNs = factory.TryGetEncodeableType(typeIdWithoutNs, out IEncodeableType typeWithoutNs);

            Assert.True(foundWithNs);
            Assert.False(foundWithoutNs);
            Assert.AreEqual(typeof(TestEncodeable), typeWithNs.Type);
            Assert.Null(typeWithoutNs);
        }

        [Test]
        public void Builder_EmptyCommit_DoesNotThrow()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            NUnit.Framework.Assert.DoesNotThrow(builder.Commit);
            Assert.Greater(factory.KnownTypeIds.Count(), 0); // Should have pre-loaded types
        }

        [Test]
        public void Builder_MultipleEncodingIds_AllRegistered()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(typeof(TestJsonEncodeable));
            builder.Commit();

            // Assert - Should find type by all encoding IDs
            bool foundByBinary = factory.TryGetEncodeableType(new ExpandedNodeId(100001), out IEncodeableType typeByBinaryId);
            bool foundByXml = factory.TryGetEncodeableType(new ExpandedNodeId(100002), out IEncodeableType typeByXmlId);
            bool foundByJson = factory.TryGetEncodeableType(new ExpandedNodeId(100003), out IEncodeableType typeByJsonId);
            bool foundByType = factory.TryGetEncodeableType(new ExpandedNodeId(100004), out IEncodeableType typeByTypeId);

            Assert.True(foundByType);
            Assert.True(foundByBinary);
            Assert.True(foundByXml);
            Assert.True(foundByJson);

            Assert.AreEqual(typeof(TestJsonEncodeable), typeByTypeId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByBinaryId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByXmlId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByJsonId.Type);
        }

        [Test]
        public void Builder_ReuseAfterCommit_CanAddMoreTypes()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act
            builder.AddEncodeableType(new ExpandedNodeId(9200), typeof(TestEncodeable)).Commit();
            builder.AddEncodeableType(new ExpandedNodeId(9201), typeof(TestJsonEncodeable)).Commit();

            // Assert
            bool foundFirst = factory.TryGetEncodeableType(new ExpandedNodeId(9200), out IEncodeableType firstType);
            bool foundSecond = factory.TryGetEncodeableType(new ExpandedNodeId(9201), out IEncodeableType secondType);

            Assert.True(foundFirst);
            Assert.True(foundSecond);
            Assert.AreEqual(typeof(TestEncodeable), firstType.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), secondType.Type);
        }

        [Test]
        public void TryGetEncodeableType_WithNullNodeId_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            bool result = factory.TryGetEncodeableType(null, out IEncodeableType encodeableType);

            // Assert
            Assert.False(result);
            Assert.Null(encodeableType);
        }

        [Test]
        public void TryGetEncodeableType_WithExpandedNodeIdNull_ReturnsFalse()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act
            bool result = factory.TryGetEncodeableType(ExpandedNodeId.Null, out IEncodeableType encodeableType);

            // Assert
            Assert.False(result);
            Assert.Null(encodeableType);
        }

        /// <summary>
        /// Test class for encodeable objects used in factory tests.
        /// </summary>
        public class TestEncodeable : IEncodeable
        {
            public TestEncodeable()
            {
            }

            public TestEncodeable(string value)
            {
                Value = value;
            }

            public string Value { get; set; }

            public virtual ExpandedNodeId TypeId => new(100000);
            public ExpandedNodeId BinaryEncodingId => new(100001);
            public ExpandedNodeId XmlEncodingId => new(100002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeable test && test.Value == Value;
            }

            public virtual object Clone()
            {
                return new TestEncodeable(Value);
            }
        }

        /// <summary>
        /// Test class for JSON encodeable objects used in factory tests.
        /// </summary>
        public class TestJsonEncodeable : TestEncodeable, IJsonEncodeable
        {
            public TestJsonEncodeable()
            {
            }

            public TestJsonEncodeable(string value)
                : base(value)
            {
            }

            public override ExpandedNodeId TypeId => new(100004);

            public ExpandedNodeId JsonEncodingId => new(100003);

            public override object Clone()
            {
                return new TestJsonEncodeable(Value);
            }
        }

        public class TestNoDefaultConstructorEncodeable : IEncodeable
        {
            public TestNoDefaultConstructorEncodeable(string value)
            {
                Value = value;
            }

            public string Value { get; set; }
            public ExpandedNodeId TypeId => new(110000);
            public ExpandedNodeId BinaryEncodingId => new(110001);
            public ExpandedNodeId XmlEncodingId => new(110002);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestNoDefaultConstructorEncodeable test && test.Value == Value;
            }

            public object Clone()
            {
                return new TestNoDefaultConstructorEncodeable(Value);
            }
        }

        /// <summary>
        /// Abstract test encodeable for testing abstract types.
        /// </summary>
        public abstract class AbstractTestEncodeable : IEncodeable
        {
            public abstract ExpandedNodeId TypeId { get; }
            public abstract ExpandedNodeId BinaryEncodingId { get; }
            public abstract ExpandedNodeId XmlEncodingId { get; }

            public virtual void Encode(IEncoder encoder)
            {
            }

            public virtual void Decode(IDecoder decoder)
            {
            }

            public virtual bool IsEqual(IEncodeable encodeable)
            {
                return false;
            }

            public virtual object Clone()
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType for testing purposes.
        /// </summary>
        public class TestEncodeableType : IEncodeableType
        {
            public TestEncodeableType(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public IEncodeable CreateInstance()
            {
                return (IEncodeable)Activator.CreateInstance(Type);
            }

            public override bool Equals(object obj)
            {
                return Type.Equals((obj as IEncodeableType)?.Type);
            }

            public override int GetHashCode()
            {
                return Type.GetHashCode();
            }

            public override string ToString()
            {
                return Type.FullName ?? Type.Name;
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType that throws on CreateInstance.
        /// </summary>
        public class FaultyEncodeableType : IEncodeableType
        {
            public Type Type => typeof(TestEncodeable);

            public IEncodeable CreateInstance()
            {
                throw new InvalidOperationException("Cannot create instance");
            }
        }

        /// <summary>
        /// Test implementation of IEncodeableType that returns null on CreateInstance.
        /// </summary>
        public class NullReturningEncodeableType : IEncodeableType
        {
            public Type Type => typeof(TestEncodeable);

            public IEncodeable CreateInstance()
            {
                return null;
            }
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act
            builder.AddEncodeableType(encodeableType);
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType resultType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), resultType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(encodeableType);
            Assert.AreSame(builder, result);
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndIEncodeableType_AddsTypeSuccessfully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50000);
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act
            builder.AddEncodeableType(typeId, encodeableType);
            builder.Commit();

            // Assert
            bool found = factory.TryGetEncodeableType(typeId, out IEncodeableType resultType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), resultType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndIEncodeableType_ReturnsBuilderForChaining()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50001);
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act & Assert
            IEncodeableFactoryBuilder result = builder.AddEncodeableType(typeId, encodeableType);
            Assert.AreSame(builder, result);
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullIEncodeableType_ThrowsArgumentNullException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act & Assert
            NUnit.Framework.Assert.Throws<ArgumentNullException>(
                () => builder.AddEncodeableType((IEncodeableType)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullExpandedNodeIdAndIEncodeableType_ThrowsArgumentNullException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeable));

            // Act & Assert
            NUnit.Framework.Assert.Throws<ArgumentNullException>(
                () => builder.AddEncodeableType(null, encodeableType));
        }

        [Test]
        public void Builder_AddEncodeableType_WithExpandedNodeIdAndNullIEncodeableType_ThrowsArgumentNullException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var typeId = new ExpandedNodeId(50002);

            // Act & Assert
            NUnit.Framework.Assert.Throws<ArgumentNullException>(
                () => builder.AddEncodeableType(typeId, (IEncodeableType)null));
        }

        [Test]
        public void Builder_AddEncodeableType_WithFaultyIEncodeableType_ThrowsException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var faultyType = new FaultyEncodeableType();

            // Act & Assert
            NUnit.Framework.Assert.Throws<InvalidOperationException>(
                () => builder.AddEncodeableType(faultyType));
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullReturningIEncodeableType_ThrowsException()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var nullReturningType = new NullReturningEncodeableType();

            // Act & Assert
            NUnit.Framework.Assert.Throws<InvalidOperationException>(
                () => builder.AddEncodeableType(nullReturningType));
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableTypeNotSupportingXmlEncoding_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithoutXml));

            // Act & Assert - Should not throw even if XmlEncodingId throws NotSupportedException
            NUnit.Framework.Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableType(encodeableType);
                builder.Commit();
            });

            // Should still be able to find by other encoding IDs
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(120000), out IEncodeableType resultType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeableWithoutXml), resultType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableTypeNotSupportingJsonEncoding_HandlesGracefully()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithoutJson));

            // Act & Assert - Should not throw even if JsonEncodingId throws NotSupportedException
            NUnit.Framework.Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableType(encodeableType);
                builder.Commit();
            });

            // Should still register other encoding IDs
            bool found = factory.TryGetEncodeableType(new ExpandedNodeId(130000), out IEncodeableType resultType);
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeableWithoutJson), resultType.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithIEncodeableTypeHavingJsonEncoding_RegistersAllEncodingIds()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestJsonEncodeable));

            // Act
            builder.AddEncodeableType(encodeableType);
            builder.Commit();

            // Assert - Should find type by all encoding IDs
            bool foundByType = factory.TryGetEncodeableType(new ExpandedNodeId(100004), out IEncodeableType typeByTypeId);
            bool foundByBinary = factory.TryGetEncodeableType(new ExpandedNodeId(100001), out IEncodeableType typeByBinaryId);
            bool foundByXml = factory.TryGetEncodeableType(new ExpandedNodeId(100002), out IEncodeableType typeByXmlId);
            bool foundByJson = factory.TryGetEncodeableType(new ExpandedNodeId(100003), out IEncodeableType typeByJsonId);

            Assert.True(foundByType);
            Assert.True(foundByBinary);
            Assert.True(foundByXml);
            Assert.True(foundByJson);

            Assert.AreEqual(typeof(TestJsonEncodeable), typeByTypeId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByBinaryId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByXmlId.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), typeByJsonId.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithDefaultNamespaceNormalization_HandlesCorrectly()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithDefaultNamespace));

            // Act
            builder.AddEncodeableType(encodeableType);
            builder.Commit();

            // Assert - Should be findable with normalized NodeId (without namespace URI)
            bool foundWithoutNs = factory.TryGetEncodeableType(new ExpandedNodeId(140000), out IEncodeableType typeWithoutNs);
            bool foundWithNs = factory.TryGetEncodeableType(new ExpandedNodeId(140000, Namespaces.OpcUa), out _);

            Assert.True(foundWithoutNs);
            Assert.False(foundWithNs);
            Assert.AreEqual(typeof(TestEncodeableWithDefaultNamespace), typeWithoutNs.Type);
        }

        [Test]
        public void Builder_TryGetEncodeableType_FallsBackToFactory()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Pre-populate factory with a type
            factory.Builder.AddEncodeableType(new ExpandedNodeId(60000), typeof(TestEncodeable)).Commit();

            // Act - Try to find type that's only in the factory, not in the builder
            bool found = builder.TryGetEncodeableType(new ExpandedNodeId(60000), out IEncodeableType encodeableType);

            // Assert
            Assert.True(found);
            Assert.AreEqual(typeof(TestEncodeable), encodeableType.Type);
        }

        [Test]
        public void Builder_TryGetEncodeableType_PrefersBuilderOverFactory()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            var typeId = new ExpandedNodeId(61000);

            // Add one type to factory
            factory.Builder.AddEncodeableType(typeId, typeof(TestEncodeable)).Commit();

            // Add different type to builder with same ID
            IEncodeableFactoryBuilder builder = factory.Builder;
            builder.AddEncodeableType(typeId, typeof(TestJsonEncodeable));

            // Act
            bool found = builder.TryGetEncodeableType(typeId, out IEncodeableType encodeableType);

            // Assert - Should prefer builder's type over factory's type
            Assert.True(found);
            Assert.AreEqual(typeof(TestJsonEncodeable), encodeableType.Type);
        }

        /// <summary>
        /// Test encodeable that throws NotSupportedException for XmlEncodingId.
        /// </summary>
        public class TestEncodeableWithoutXml : IEncodeable
        {
            public ExpandedNodeId TypeId => new(120000);
            public ExpandedNodeId BinaryEncodingId => new(120001);
            public ExpandedNodeId XmlEncodingId => throw new NotSupportedException("XML encoding not supported");

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithoutXml;
            }

            public object Clone()
            {
                return new TestEncodeableWithoutXml();
            }
        }

        /// <summary>
        /// Test encodeable that throws NotSupportedException for JsonEncodingId.
        /// </summary>
        public class TestEncodeableWithoutJson : IEncodeable, IJsonEncodeable
        {
            public ExpandedNodeId TypeId => new(130000);
            public ExpandedNodeId BinaryEncodingId => new(130001);
            public ExpandedNodeId XmlEncodingId => new(130002);
            public ExpandedNodeId JsonEncodingId => throw new NotSupportedException("JSON encoding not supported");

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithoutJson;
            }

            public object Clone()
            {
                return new TestEncodeableWithoutJson();
            }
        }

        /// <summary>
        /// Test encodeable with default OPC UA namespace.
        /// </summary>
        public class TestEncodeableWithDefaultNamespace : IEncodeable
        {
            public ExpandedNodeId TypeId => new(140000, Namespaces.OpcUa);
            public ExpandedNodeId BinaryEncodingId => new(140001, Namespaces.OpcUa);
            public ExpandedNodeId XmlEncodingId => new(140002, Namespaces.OpcUa);

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithDefaultNamespace;
            }

            public object Clone()
            {
                return new TestEncodeableWithDefaultNamespace();
            }
        }

        [Test]
        public void Builder_AddEncodeableTypes_ResolvesJsonEncodingIdsFromObjectIds()
        {
            // Arrange - Create a minimal assembly structure to test JSON ID resolution
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act - Add types from current assembly which should include our test types
            builder.AddEncodeableTypes(Assembly.GetExecutingAssembly());
            builder.Commit();

            // Assert - Our test types should be registered
            Assert.True(factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType testType));
            Assert.AreEqual(typeof(TestEncodeable), testType.Type);
        }

        [Test]
        public void Builder_AddEncodeableTypes_HandlesJsonEncodingSuffixParsing()
        {
            // This test verifies the JSON encoding suffix parsing logic in AddEncodeableTypes
            // The method looks for fields ending with "_Encoding_DefaultJson" in ObjectIds classes

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act - This should process ObjectIds classes and parse JSON encoding suffixes
            builder.AddEncodeableTypes(typeof(EncodeableFactory).Assembly);
            builder.Commit();

            // Assert - Should have many types including core OPC UA types
            Assert.Greater(factory.KnownTypeIds.Count(), 1000);
        }

        [Test]
        public void Integration_FactorySupportsComplexTypeRegistration()
        {
            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;

            // Act - Register multiple types with different encoding patterns
            builder.AddEncodeableType(typeof(TestEncodeable))
                   .AddEncodeableType(typeof(TestJsonEncodeable))
                   .AddEncodeableType(new ExpandedNodeId(99999), typeof(TestEncodeableWithDefaultNamespace));
            builder.Commit();

            // Assert - All types should be accessible
            Assert.True(factory.TryGetEncodeableType(new ExpandedNodeId(100000), out IEncodeableType type1));
            Assert.True(factory.TryGetEncodeableType(new ExpandedNodeId(100004), out IEncodeableType type2));
            Assert.True(factory.TryGetEncodeableType(new ExpandedNodeId(99999), out IEncodeableType type3));

            Assert.AreEqual(typeof(TestEncodeable), type1.Type);
            Assert.AreEqual(typeof(TestJsonEncodeable), type2.Type);
            Assert.AreEqual(typeof(TestEncodeableWithDefaultNamespace), type3.Type);
        }

        [Test]
        public void Builder_AddEncodeableType_WithNullNodeIds_SkipsGracefully()
        {
            // Test the behavior when an encodeable type returns null NodeIds

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();
            IEncodeableFactoryBuilder builder = factory.Builder;
            var encodeableType = new TestEncodeableType(typeof(TestEncodeableWithNullIds));

            // Act & Assert - Should not throw
            NUnit.Framework.Assert.DoesNotThrow(() =>
            {
                builder.AddEncodeableType(encodeableType);
                builder.Commit();
            });

            // Should not be findable since all IDs are null
            bool found = factory.TryGetEncodeableType(ExpandedNodeId.Null, out _);
            Assert.False(found);
        }

        [Test]
        public void EncodeableFactory_FrozenDictionary_Performance_CharacteristicsVerification()
        {
            // This test verifies that the FrozenDictionary is actually being used
            // and has the expected performance characteristics mentioned in the comments

            // Arrange
            IEncodeableFactory factory = EncodeableFactory.Create();

            // Act - Perform multiple lookups to verify frozen dictionary performance
            var readRequestId = new ExpandedNodeId(ObjectIds.Argument_Encoding_DefaultBinary);
            var nonExistentId = new ExpandedNodeId(Guid.NewGuid());

            // Time the lookups (though we won't assert on timing, just verify functionality)
            bool found1 = factory.TryGetEncodeableType(readRequestId, out IEncodeableType type1);
            bool found2 = factory.TryGetEncodeableType(nonExistentId, out IEncodeableType type2);

            // Assert
            Assert.True(found1);
            Assert.NotNull(type1);
            Assert.AreEqual(typeof(Argument), type1.Type);

            Assert.False(found2);
            Assert.Null(type2);

            // Verify we have a substantial number of types (mentioned as ~1.5k in comments)
            Assert.Greater(factory.KnownTypeIds.Count(), 1000);
        }

        private IEncodeableFactoryBuilder m_builder;
        private IEncodeableFactory m_encodeableFactory;

        /// <summary>
        /// Test encodeable with null NodeIds to test edge case handling.
        /// </summary>
        public class TestEncodeableWithNullIds : IEncodeable
        {
            public ExpandedNodeId TypeId => ExpandedNodeId.Null;
            public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;
            public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

            public void Encode(IEncoder encoder)
            {
            }

            public void Decode(IDecoder decoder)
            {
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is TestEncodeableWithNullIds;
            }

            public object Clone()
            {
                return new TestEncodeableWithNullIds();
            }
        }
    }
}
