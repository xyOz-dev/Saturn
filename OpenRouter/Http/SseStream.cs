using System.Runtime.CompilerServices;
using System.Text;

namespace Saturn.OpenRouter.Http
{
    public static class SseStream
    {
        public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);

            string? currentEventName = null;
            StringBuilder? dataBuffer = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (line is null)
                    break;

                if (line.Length == 0)
                {
                    // A blank line terminates the event; multi-line data fields are joined with '\n'.
                    if (dataBuffer != null)
                    {
                        yield return new SseEvent
                        {
                            Event = currentEventName,
                            Data = dataBuffer.ToString(),
                            IsComment = false
                        };
                        dataBuffer = null;
                    }
                    currentEventName = null;
                    continue;
                }

                if (line.StartsWith(":", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    currentEventName = line.Substring("event:".Length).TrimStart();
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var data = line.Substring("data:".Length).TrimStart();
                    if (dataBuffer == null)
                    {
                        dataBuffer = new StringBuilder(data);
                    }
                    else
                    {
                        dataBuffer.Append('\n').Append(data);
                    }
                }
            }

            // Flush a trailing event if the stream ended without a terminating blank line.
            if (!cancellationToken.IsCancellationRequested && dataBuffer != null)
            {
                yield return new SseEvent
                {
                    Event = currentEventName,
                    Data = dataBuffer.ToString(),
                    IsComment = false
                };
            }
        }
    }
}
