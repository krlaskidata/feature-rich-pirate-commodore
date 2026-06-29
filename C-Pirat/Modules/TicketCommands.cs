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

    public class TicketCommands : ModuleBase<SocketCommandContext>
    {
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