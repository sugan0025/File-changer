using System.Buffers.Binary;
using System.Text;
using Filee.Core.Web;

namespace Filee.Core.Tests;

public static class TestDragData
{
    private const int DescriptorSize = 592;
    private const int SizeHighOffset = 64;
    private const int AttributesOffset = 36;
    private const int NameOffset = 72;
    private const int NameChars = 260;
    private const uint FdAttributes = 0x4;
    private const uint FdFileSize = 0x40;
    private const uint FileAttributeDirectory = 0x10;

    /// <summary>Builds a FileGroupDescriptorW (for tests and fakes).</summary>
    public static byte[] BuildFileGroupDescriptor(IReadOnlyList<FileDescriptor> entries)
    {
        var data = new byte[4 + entries.Count * DescriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = data.AsSpan(4 + i * DescriptorSize, DescriptorSize);
            var flags = FdAttributes | (entries[i].Size is null ? 0 : FdFileSize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, flags);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[AttributesOffset..], entries[i].IsFolder ? FileAttributeDirectory : 0x80);
            if (entries[i].Size is { } size)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(entry[SizeHighOffset..], (uint)(size >> 32));
                BinaryPrimitives.WriteUInt32LittleEndian(entry[(SizeHighOffset + 4)..], (uint)size);
            }
            var name = entries[i].Name.Length >= NameChars ? entries[i].Name[..(NameChars - 1)] : entries[i].Name;
            Encoding.Unicode.GetBytes(name).CopyTo(entry[NameOffset..]);
        }
        return data;
    }
}

