using Google.Protobuf;
using System.Buffers.Binary;

namespace SalesforceGrpc.Extensions;
public static class ConversionExtensions {
    /// <summary>
    /// Returns the big endian value of the input bytestring.
    /// </summary>
    /// <param name="byteString"></param>
    /// <returns>The big endian value</returns>
    /// <exception cref="ArgumentOutOfRangeException">ArgumentOutOfRangeException</exception>
    /// <exception cref="ArgumentNullException">NullReferenceException</exception>
    public static long ToLongBE(this ByteString byteString) {
        if(byteString is null) throw new ArgumentNullException(nameof(byteString));
        return BinaryPrimitives.ReadInt64BigEndian(byteString.ToByteArray());
    }
}
