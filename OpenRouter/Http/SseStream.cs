using System.Runtime.CompilerServices;
using System.Text;

namespace Saturn.OpenRouter.Http
{
    /// <summary>
    /// Minimal Server-Sent Events (SSE) stream parser.
    /// Yields an <see cref="SseEvent"/> for lines starting with "data: ".
    /// Comment lines starting with ":" are ignored. Cancellation is honored.
    /// </summary>
    public static class SseStream
    {
        /// <summary>
        /// Read SSE events from a UTF-8 byte stream.
        /// For this milestone, yields one event per "data: " line and ignores comments.
        /// </summary>
        /// <param name="stream">The source SSE byte stream.</param>
        /// <param name="cancellationToken">Cancellation token to stop reading.</param>
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
                    // Event delimiter; reset accumulated fields for the next event.
                    currentEventName = null;
                    continue;
                }

                // Ignore comments per SSE spec (": ....")
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

                // Minimal skeleton: ignore other fields for now (id, retry, etc.).
            }
        }
    }
}