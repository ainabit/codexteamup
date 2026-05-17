using System.Text;

namespace CodexTeamUp.Service;

public static class HttpRequestReader
{
    public static async Task<string?> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (buffer[0] == (byte)'\n')
            {
                if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                {
                    bytes.RemoveAt(bytes.Count - 1);
                }

                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            bytes.Add(buffer[0]);
        }
    }

    public static async Task<string> ReadUtf8BodyAsync(Stream stream, int contentLength, CancellationToken cancellationToken = default)
    {
        if (contentLength <= 0)
        {
            return string.Empty;
        }

        var buffer = new byte[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, contentLength - read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
