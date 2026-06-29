using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PiratBotCSharp.Modules
{
    public static class MessageProtectionService
    {
        private static readonly SemaphoreSlim MessageProcessingSlots = new(120, 120);
        private static readonly ConcurrentDictionary<ulong, Queue<DateTime>> CommandWindows = new();
        private const int CommandsPerWindow = 6;
        private const int WindowSeconds = 10;

        public static async Task<bool> TryEnterMessagePipelineAsync()
        {
            try
            {
                return await MessageProcessingSlots.WaitAsync(TimeSpan.FromMilliseconds(100));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCommandRateLimited(ulong userId)
        {
            var now = DateTime.UtcNow;
            var window = CommandWindows.GetOrAdd(userId, _ => new Queue<DateTime>());

            lock (window)
            {
                while (window.Count > 0 && (now - window.Peek()).TotalSeconds > WindowSeconds)
                {
                    window.Dequeue();
                }

                if (window.Count >= CommandsPerWindow)
                {
                    return true;
                }

                window.Enqueue(now);
                return false;
            }
        }

        public static void ExitMessagePipeline()
        {
            try
            {
                MessageProcessingSlots.Release();
            }
            catch { }
        }

        public static void ClearRateLimitForUser(ulong userId)
        {
            CommandWindows.TryRemove(userId, out _);
        }
    }
}
