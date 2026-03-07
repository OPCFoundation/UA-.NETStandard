using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Utils
{
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NumericRangeTests
    {
        [Test]
        public void Equals_ForDifferingSubRanges_ReturnsFalse()
        {
            var range1 = new NumericRange(1, 5)
            {
                SubRanges = [new NumericRange(1, 3)]
            };

            var range2 = new NumericRange(1, 5)
            {
                SubRanges = [new NumericRange(2, 4)]
            };

            Assert.That(range1.Equals(range2), Is.False);
        }
    }
}
