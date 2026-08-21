using System;
using NUnit.Framework;
using Tesearis.SmolVDecoder;

namespace Tesearis.SmolVDecoder.Tests
{
    // Error-path smoke tests only.
    [TestFixture]
    public class SmolVTests
    {
        [Test]
        public void TryDecodeStages_EmptyInput_ReturnsFalseWithError()
        {
            var result = SmolV.TryDecodeStages(Array.Empty<byte>(), out var vertexSpirv, out var fragmentSpirv, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(vertexSpirv);
            Assert.IsNull(fragmentSpirv);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void TryDecodeStages_MissingSmolMagic_ReturnsFalseWithError()
        {
            var data = new byte[32]; // all zeros - no "SMOL" magic anywhere

            var result = SmolV.TryDecodeStages(data, out var vertexSpirv, out var fragmentSpirv, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(vertexSpirv);
            Assert.IsNull(fragmentSpirv);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }
    }
}
