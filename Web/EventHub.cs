using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Saturn.Web
{
    public class EventHub
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

        public int SubscriberCount => _subscribers.Count;

        public void Publish(string eventName, object? payload = null)
        {
            var json = JsonSerializer.Serialize(payload ?? new { }, JsonOptions);
            var frame = $"event: {eventName}\ndata: {json}\n\n";

            foreach (var channel in _subscribers.Values)
            {
                channel.Writer.TryWrite(frame);
            }
        }

        public async Task StreamAsync(HttpResponse response, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _subscribers[id] = channel;

            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";

            try
            {
                await response.WriteAsync(": connected\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);

                // The pending read must survive keep-alive rounds: starting a new
                // ReadAsync each loop would leave abandoned readers racing on the
                // channel and frames could vanish into a task nobody awaits.
                Task<string>? readTask = null;
                while (!cancellationToken.IsCancellationRequested)
                {
                    readTask ??= channel.Reader.ReadAsync(cancellationToken).AsTask();
                    var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(20), cancellationToken));

                    if (completed == readTask)
                    {
                        await response.WriteAsync(await readTask, cancellationToken);
                        readTask = null;
                    }
                    else
                    {
                        await response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    }

                    await response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _subscribers.TryRemove(id, out _);
            }
        }
    }
}
