using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace HamiPdf.Services;

internal static class LaunchProtocol
{
    private const int MaximumBytes = 1024 * 1024;
    public static async Task WriteAsync(Stream stream, string[] paths, CancellationToken cancellation)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(paths);
        if (data.Length > MaximumBytes || paths.Length > 256) throw new InvalidDataException("Too many files in one open request.");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(data, cancellation);
        await stream.FlushAsync(cancellation);
    }
    public static async Task<string[]> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 2 || length > MaximumBytes) throw new InvalidDataException("Invalid launch request size.");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, cancellation);
        var paths = JsonSerializer.Deserialize<string[]>(data) ?? throw new InvalidDataException("Invalid launch request.");
        if (paths.Length > 256 || paths.Any(p => p == null || p.Length > 32767)) throw new InvalidDataException("Invalid launch paths.");
        return paths;
    }
}
