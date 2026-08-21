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

        // A decoded size wildly out of proportion to the input should fail gracefully (false + error)
        // rather than attempt a huge allocation.
        [Test]
        public void TryDecodeStages_ImplausiblyLargeDecodedSize_ReturnsFalseWithErrorWithoutHugeAllocation()
        {
            var data = new byte[24];
            WriteUInt32LE(data, 0, 0x534D4F4C); // "SMOL" header magic
            WriteUInt32LE(data, 4, 0x00010000); // smolVersion 0, headerVersion 0x00010000 (in valid range)
            WriteUInt32LE(data, 8, 0); // generator
            WriteUInt32LE(data, 12, 0); // bound
            WriteUInt32LE(data, 16, 0); // schema
            WriteUInt32LE(data, 20, 0x7FFFFFFC); // decodedSize: implausibly large, ~2 GiB, multiple of 4

            var result = SmolV.TryDecodeStages(data, out var vertexSpirv, out var fragmentSpirv, out var error);

            Assert.IsFalse(result);
            Assert.IsNull(vertexSpirv);
            Assert.IsNull(fragmentSpirv);
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
