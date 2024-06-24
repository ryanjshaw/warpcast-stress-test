using System.Collections.Specialized;
using System.Diagnostics;

public static class IntExtensions
{
    #region Methods

    public static byte ClipToByte(this int value)
        => value < Byte.MinValue ? Byte.MinValue
            : value > Byte.MaxValue ? Byte.MaxValue
            : (byte)value;

    internal static int ToBitsPerPixel(this int colorCount)
    {
        if (colorCount == 1)
            return 1;

        // Bits per pixel is actually ceiling of log2(maxColors)
        // We could use BitOperations.Log2 but that returns the floor value so we should combine it with BitOperations.IsPow2,
        // which is available only starting with .NET 6 and in the end it would be slower for typical values not larger than 256.
        int bpp = 0;
        for (int n = colorCount - 1; n > 0; n >>= 1)
            bpp++;

        return bpp;
    }

    internal static int RoundUpToPowerOf2(this uint value)
    {
        // In .NET 6 and above there is a BitOperations.RoundUpToPowerOf2
        --value;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return (int)(value + 1);
    }

    internal static int GetMask(this BitVector32.Section section) => section.Mask << section.Offset;

    internal static int Abs(this int i)
    {
        // Math.Abs is still slower, even after the fix in https://github.com/dotnet/runtime/issues/24626
        Debug.Assert(i != Int32.MinValue);
        return i >= 0 ? i : -i;
    }

    #endregion
}