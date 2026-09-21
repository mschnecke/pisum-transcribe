namespace Pisum.Transcribe.Tests;

/// <summary>
/// A response body that returns <c>data</c> and then ends as <c>end</c> says. It cannot seek, so
/// <see cref="StreamContent"/> announces no <c>Content-Length</c> unless a test sets one.
/// </summary>
public sealed class ResponseBodyStream(byte[] data, ResponseBodyStream.End end = ResponseBodyStream.End.Complete)
    : Stream
{
    public enum End
    {
        /// <summary>The body ends normally.</summary>
        Complete,

        /// <summary>The connection fails.</summary>
        ConnectionReset,

        /// <summary>No more data arrives until the read is cancelled.</summary>
        Stall,
    }

    private int _position;

    /// <summary>
    /// Completes when all data was read.
    /// </summary>
    public TaskCompletionSource AllDataRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int BytesRead => _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < data.Length)
        {
            var count = Math.Min(buffer.Length, data.Length - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        AllDataRead.TrySetResult();
        switch (end)
        {
            case End.ConnectionReset:
                throw new IOException("The connection was reset.");
            case End.Stall:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }

        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
