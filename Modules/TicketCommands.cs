using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    // Ticket Service Classes
    public class TicketConfigEntry
    {
        public ulong LogChannelId { get; set; }
        public ulong? TicketCategoryId { get; set; }
        public ulong? SupportRoleId { get; set; }
        
        // Setup tracking for continuation feature
        public string? SetupStep { get; set; }
        public ulong? TicketMessageChannelId { get; set; }
        public ulong? TicketMessageId { get; set; }
        public DateTime? LastSetupAttempt { get; set; }
    }

    public static class TicketService
    {
        private const string TICKETS_CONFIG_FILE = "pirat_tickets_config.json";
        private static Dictionary<ulong, TicketConfigEntry> _cfg = LoadTicketsConfig();
        public static ConcurrentDictionary<ulong, TicketMeta> TicketMetas = new();
        private static readonly ConcurrentDictionary<ulong, System.Timers.Timer> _autoCloseTimers = new();
        
        // MEMORY OPTIMIZATION: Cleanup constants
        private static DateTime _lastCleanup = DateTime.UtcNow;
        private const int MAX_TICKET_METAS = 200; // Reduced from 2000 to 200 for memory optimization
        private const int CLEANUP_INTERVAL_HOURS = 6; // Cleanup every 6 hours

        private static Dictionary<ulong, TicketConfigEntry> LoadTicketsConfig()
        {
            try
            {
                if (!File.Exists(TICKETS_CONFIG_FILE)) return new Dictionary<ulong, TicketConfigEntry>();
                var txt = File.ReadAllText(TICKETS_CONFIG_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, TicketConfigEntry>>(txt);
                return d ?? new Dictionary<ulong, TicketConfigEntry>();
            }
            catch { return new Dictionary<ulong, TicketConfigEntry>(); }
        }

        private static void SaveTicketsConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(TICKETS_CONFIG_FILE, txt);
            }
            catch { }
        }

        public static TicketConfigEntry? GetConfig(ulong guildId)
        {
            // Always reload config from disk to ensure latest settings
            _cfg = LoadTicketsConfig();
            if (_cfg.TryGetValue(guildId, out var e)) return e; return null;
        }

        public static void SetConfig(ulong guildId, TicketConfigEntry cfg)
        {
            _cfg[guildId] = cfg; SaveTicketsConfig();
        }

        public static void RemoveConfig(ulong guildId)
        {
            _cfg.Remove(guildId);
            SaveTicketsConfig();
        }

        public class TicketMeta
        {
            public ulong UserId { get; set; }
            public string? Category { get; set; }
            public ulong GuildId { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public string? Username { get; set; }
        }
        
        // MEMORY OPTIMIZATION: Cleanup orphaned tickets
        public static void CleanupTicketMemory()
        {
            try
            {
                // Only run cleanup every 6 hours
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS))
                    return;
                
                _lastCleanup = DateTime.UtcNow;
                var removedCount = 0;
                
                // If too many tickets, remove oldest ones
                if (TicketMetas.Count > MAX_TICKET_METAS)
                {
                    var oldestTickets = TicketMetas
                        .OrderBy(kvp => kvp.Value.CreatedAt)
                        .Take(TicketMetas.Count - MAX_TICKET_METAS)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var channelId in oldestTickets)
                    {
                        if (TicketMetas.TryRemove(channelId, out _))
                            removedCount++;
                        
                        // Also cleanup timers
                        if (_autoCloseTimers.TryRemove(channelId, out var timer))
                        {
                            timer.Stop();
                            timer.Dispose();
                        }
                    }
                    
                    if (removedCount > 0)
                        Console.WriteLine($"🏴‍☠️ Pirate ticket cleanup: Removed {removedCount} old tickets. Active: {TicketMetas.Count}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error during pirate ticket cleanup: {ex.Message}");
            }
        }
    }

    [Group("ticket")]
    public class TicketCommands : ModuleBase<SocketCommandContext>
    {
        private async Task<SocketMessage?> NextMessageAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessage?>();
            var expectedUserId = Context.User.Id;
            var expectedChannelId = Context.Channel.Id;
            var client = Context.Client;
            
            Console.WriteLine($"[NextMessageAsync-TICKET] Waiting for message from User={expectedUserId} in Channel={expectedChannelId}");

            Task Handler(SocketMessage message)
            {
                Console.WriteLine($"[NextMessageAsync-TICKET] Handler triggered: Author={message.Author.Id}, Channel={message.Channel.Id}, Content='{message.Content}'");
                Console.WriteLine($"[NextMessageAsync-TICKET] Expecting: Author={expectedUserId}, Channel={expectedChannelId}");
                
                if (message.Channel.Id == expectedChannelId && message.Author.Id == expectedUserId && !message.Author.IsBot)
                {
                    Console.WriteLine("[NextMessageAsync-TICKET] MATCH! Setting result.");
                    tcs.TrySetResult(message);
                }
                else
                {
                    Console.WriteLine("[NextMessageAsync-TICKET] No match.");
                }
                return Task.CompletedTask;
            }

            client.MessageReceived += Handler;

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            client.MessageReceived -= Handler;

            if (completedTask == tcs.Task)
            {
                Console.WriteLine("[NextMessageAsync-TICKET] Task completed successfully!");
                return await tcs.Task;
            }

            Console.WriteLine("[NextMessageAsync-TICKET] Timeout reached!");
            return null;
        }

        [Command("setup")]
        [Summary("Setup ticket system (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetupTicketSystemAsync()
        {
            await ReplyAsync("🎫 **Pirate Ticket System**\n\nThis is a simplified ticket system for the Pirate Bot. Use the commands to interact with tickets.");
        }

        [Command("close")]
        [Summary("Close the current ticket")]
        public async Task CloseTicketAsync([Remainder] string reason = "No reason provided")
        {
            await ReplyAsync($"🔒 **Ticket Close Requested**\n\nReason: {reason}\n\nThis would close the ticket in a full implementation.");
        }

        // Add missing interaction handlers
        public static async Task HandleSelectMenuInteraction(SocketMessageComponent interaction)
        {
            try
            {
                await interaction.DeferAsync();
                Console.WriteLine($"Ticket select menu interaction: {interaction.Data.CustomId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling ticket select menu: {ex.Message}");
            }
        }

        public static async Task HandleButtonInteraction(SocketMessageComponent interaction)
        {
            try
            {
                await interaction.DeferAsync();
                Console.WriteLine($"Ticket button interaction: {interaction.Data.CustomId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling ticket button: {ex.Message}");
            }
        }
    }
}