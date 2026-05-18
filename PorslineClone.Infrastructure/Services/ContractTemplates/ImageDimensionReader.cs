using System.Buffers.Binary;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

internal static class ImageDimensionReader
{
    public static (int Width, int Height)? TryRead(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P')
        {
            var w = BinaryPrimitives.ReadInt32BigEndian(data[16..20]);
            var h = BinaryPrimitives.ReadInt32BigEndian(data[20..24]);
            if (w > 0 && h > 0)
                return (w, h);
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
            return TryReadJpeg(data);

        return null;
    }

    private static (int Width, int Height)? TryReadJpeg(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                var h = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]);
                var w = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]);
                if (w > 0 && h > 0)
                    return (w, h);
                return null;
            }

            if (i + 3 >= data.Length)
                break;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
            if (segmentLength < 2)
                break;
            i += 2 + segmentLength;
        }

        return null;
    }
}
