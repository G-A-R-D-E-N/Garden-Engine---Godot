using System.Diagnostics;
using System.Text;

namespace GameDataGuard;

internal sealed class Slice(Stream parent, long start, long length) : Stream
{
    private long position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int wanted = (int)Math.Min(count, Math.Max(0, length - position));
        if (wanted == 0) return 0;
        parent.Position = start + position;
        int read = parent.Read(buffer, offset, wanted);
        position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => position + offset,
        _ => length + offset,
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class GitBlobs : IDisposable
{
    private readonly Process process;
    private readonly Stream input;
    private readonly Stream output;

    public GitBlobs(string root)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in new[] { "-C", root, "cat-file", "--batch" }) start.ArgumentList.Add(argument);
        process = Process.Start(start)!;
        input = process.StandardInput.BaseStream;
        output = new BufferedStream(process.StandardOutput.BaseStream, 1 << 16);
    }

    public Stream Open(string id)
    {
        input.Write(Encoding.ASCII.GetBytes(id + "\n"));
        input.Flush();
        var header = new StringBuilder();
        for (int next = output.ReadByte(); next != '\n'; next = output.ReadByte())
        {
            if (next < 0) throw new InvalidOperationException("git cat-file ended while reading " + id);
            header.Append((char)next);
        }
        string[] fields = header.ToString().Split(' ');
        if (fields.Length != 3 || fields[1] != "blob" || !long.TryParse(fields[2], out long size)) throw new InvalidOperationException("git cat-file cannot read blob " + id + ": " + header);
        return new Blob(output, size);
    }

    public void Dispose()
    {
        input.Dispose();
        process.WaitForExit();
        process.Dispose();
    }

    private sealed class Blob(Stream source, long size) : Stream
    {
        private long remaining = size + 1;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => size;
        public override long Position { get => size + 1 - remaining; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int wanted = (int)Math.Min(count, Math.Max(0, remaining - 1));
            if (wanted == 0) return 0;
            int read = source.Read(buffer, offset, wanted);
            if (read == 0) throw new EndOfStreamException("git cat-file output ended inside a blob");
            remaining -= read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var sink = new byte[1 << 16];
                while (remaining > 0)
                {
                    int read = source.Read(sink, 0, (int)Math.Min(sink.Length, remaining));
                    if (read == 0) break;
                    remaining -= read;
                }
                remaining = 0;
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
