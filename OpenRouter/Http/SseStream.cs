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

            while (!reader.EndOfStream)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                    break;

                if (line.Length == 0)
                {
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
                    yield return new SseEvent
                    {
                        Event = currentEventName,
                        Data = data,
                        IsComment = false
                    };
                    continue;
                }
            }
        }
    }
}