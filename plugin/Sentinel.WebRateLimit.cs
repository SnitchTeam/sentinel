using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace Oxide.Plugins
{
    public class SentinelRateLimiter
    {
        private readonly int _limit;
        private readonly TimeSpan _window;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _buckets = new();

        public SentinelRateLimiter(int limitPerMinute, int windowSeconds = 60)
        {
            _limit = limitPerMinute;
            _window = TimeSpan.FromSeconds(windowSeconds);
        }

        public bool IsAllowed(string clientIp, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            if (_limit <= 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(clientIp))
            {
                clientIp = "unknown";
            }

            var now = DateTime.UtcNow;
            var queue = _buckets.GetOrAdd(clientIp, _ => new Queue<DateTime>());

            lock (queue)
            {
                while (queue.Count > 0 && (now - queue.Peek()) > _window)
                {
                    queue.Dequeue();
                }

                if (queue.Count >= _limit)
                {
                    var oldest = queue.Peek();
                    var retryAfter = oldest.Add(_window) - now;
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    return false;
                }

                queue.Enqueue(now);
                return true;
            }
        }

        public void Reset(string clientIp)
        {
            if (!string.IsNullOrEmpty(clientIp))
            {
                _buckets.TryRemove(clientIp, out _);
            }
        }
    }
}
