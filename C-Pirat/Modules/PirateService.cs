using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Discord.WebSocket;
using Discord;
using System.Timers;

namespace PiratBotCSharp.Modules
{
    public class PirateTicketConfig
    {
        public ulong LogChannelId { get; set; }
        public ulong? TicketCategoryId { get; set; }
        public ulong? SupportRoleId { get; set; }
        public List<ulong> SupportRoleIds { get; set; } = new List<ulong>(); // New field for multiple roles
        
        // Setup tracking for continuation feature
        public string? SetupStep { get; set; }
        public ulong? TicketMessageChannelId { get; set; }
        public ulong? TicketMessageId { get; set; }
        public DateTime? LastSetupAttempt { get; set; }
    }

    public class PirateSecurityConfig
    {
        public bool Enabled { get; set; }
        public ulong? LogChannelId { get; set; }
    }

    public class PirateTicketMeta
    {
        public ulong UserId { get; set; }
        public string? Category { get; set; }
        public ulong GuildId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Username { get; set; }
    }

    public class PiratePlayer
    {
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public long Gold { get; set; }
        public DateTime LastDaily { get; set; }
        public DateTime LastWork { get; set; }
        public int DailyStreak { get; set; }
        public int CrewSize { get; set; }
        public Dictionary<string, int> Inventory { get; set; } = new Dictionary<string, int>();
        public string Birthday { get; set; } = "";
    }

    public class PirateBirthday
    {
        public ulong UserId { get; set; }
        public string Birthday { get; set; } = "";
        
        // Helper property to parse birthday as DateTime
        public DateTime BirthdayDate 
        { 
            get 
            { 
                if (DateTime.TryParseExact(Birthday, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out var date))
                    return date;
                return DateTime.MinValue;
            } 
        }
    }

    public static class PirateService 
    {
        private const string PIRATE_TICKETS_CONFIG_FILE = "pirate_tickets_config.json";
        private static Dictionary<ulong, PirateTicketConfig> _ticketConfigs = LoadPirateTicketsConfig();
        public static ConcurrentDictionary<ulong, PirateTicketMeta> TicketMetas = new();
        public static readonly ConcurrentDictionary<ulong, System.Timers.Timer> _autoCloseTimers = new();
        
        // MEMORY OPTIMIZATION: Limited collections with cleanup
        private static Dictionary<ulong, PirateSecurityConfig> _securityConfigs = new();
        private static Dictionary<ulong, List<PiratePlayer>> _players = new();
        private static Dictionary<ulong, List<PirateBirthday>> _birthdays = new();
        private static Dictionary<ulong, ulong> _bumpReminderChannels = new();
        private static Dictionary<ulong, DateTime> _lastBumpTimes = new();
        private static Dictionary<ulong, ulong> _birthdayChannels = new();
        
        // Memory optimization constants
        private const int MAX_PLAYERS_PER_GUILD = 500; // Reduced from 3000 to 500 for memory optimization
        private const int MAX_BIRTHDAYS_PER_GUILD = 500; // Reduced from 2000 to 500 for memory optimization
        private const int MAX_GUILDS_TRACKED = 100; // Limit total guilds
        private static DateTime _lastCleanup = DateTime.UtcNow;

        private static Dictionary<ulong, PirateTicketConfig> LoadPirateTicketsConfig()
        {
            try
            {
                if (!File.Exists(PIRATE_TICKETS_CONFIG_FILE)) return new Dictionary<ulong, PirateTicketConfig>();
                var txt = File.ReadAllText(PIRATE_TICKETS_CONFIG_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, PirateTicketConfig>>(txt);
                return d ?? new Dictionary<ulong, PirateTicketConfig>();
            }
            catch { return new Dictionary<ulong, PirateTicketConfig>(); }
        }

        private static void SavePirateTicketsConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_ticketConfigs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PIRATE_TICKETS_CONFIG_FILE, txt);
            }
            catch { }
        }
        
        public static PirateTicketConfig GetTicketConfig(ulong guildId)
        {
            // Always reload config from disk to ensure latest settings
            _ticketConfigs = LoadPirateTicketsConfig();
            if (_ticketConfigs.TryGetValue(guildId, out var e)) return e; 
            return new PirateTicketConfig();
        }
        
        public static void SetTicketConfig(ulong guildId, PirateTicketConfig config)
        {
            _ticketConfigs[guildId] = config;
            SavePirateTicketsConfig();
        }

        public static void RemoveTicketConfig(ulong guildId)
        {
            _ticketConfigs.Remove(guildId);
            SavePirateTicketsConfig();
        }

        public static PirateSecurityConfig GetSecurityConfig(ulong guildId)
        {
            if (!_securityConfigs.ContainsKey(guildId))
                _securityConfigs[guildId] = new PirateSecurityConfig();
            return _securityConfigs[guildId];
        }

        public static void SetSecurityConfig(ulong guildId, PirateSecurityConfig config)
        {
            _securityConfigs[guildId] = config;
        }

        public static PiratePlayer GetPlayer(ulong guildId, ulong userId)
        {
            if (!_players.ContainsKey(guildId))
                _players[guildId] = new List<PiratePlayer>();
            
            var player = _players[guildId].FirstOrDefault(p => p.UserId == userId);
            if (player == null)
            {
                player = new PiratePlayer { GuildId = guildId, UserId = userId };
                _players[guildId].Add(player);
            }
            return player;
        }

        public static void SavePlayer(PiratePlayer player) 
        {
            // Already saved in memory
        }

        public static List<PiratePlayer> GetAllPlayers(ulong guildId)
        {
            if (!_players.ContainsKey(guildId))
                _players[guildId] = new List<PiratePlayer>();
            return _players[guildId];
        }

        public static List<PirateBirthday> GetAllBirthdays(ulong guildId)
        {
            if (!_birthdays.ContainsKey(guildId))
                _birthdays[guildId] = new List<PirateBirthday>();
            return _birthdays[guildId];
        }

        public static void SetBumpReminderChannel(ulong guildId, ulong channelId) 
        {
            _bumpReminderChannels[guildId] = channelId;
        }

        public static void SetBirthdayChannel(ulong guildId, ulong channelId) 
        {
            _birthdayChannels[guildId] = channelId;
        }
        
        public static ulong? GetBirthdayChannel(ulong guildId) 
        {
            return _birthdayChannels.TryGetValue(guildId, out var channelId) ? channelId : null;
        }
        
        public static ulong? GetBumpReminderChannel(ulong guildId) 
        {
            return _bumpReminderChannels.TryGetValue(guildId, out var channelId) ? channelId : null;
        }
        
        public static DateTime GetLastBumpTime(ulong guildId) 
        {
            return _lastBumpTimes.TryGetValue(guildId, out var time) ? time : DateTime.UtcNow.AddHours(-3);
        }
        
        public static void SetLastBumpTime(ulong guildId, DateTime timestamp) 
        {
            _lastBumpTimes[guildId] = timestamp;
        }
        public static void SetBirthday(ulong userId, DateTime birthday, string username) 
        {
            // Find across all guilds for now - could be improved to be guild-specific
            foreach (var guildBirthdays in _birthdays.Values)
            {
                var existing = guildBirthdays.FirstOrDefault(b => b.UserId == userId);
                if (existing != null)
                {
                    existing.Birthday = birthday.ToString("dd.MM.yyyy");
                    return;
                }
            }
            
            // Add to first available guild or create new list
            var firstGuild = _birthdays.Keys.FirstOrDefault();
            if (firstGuild == 0)
            {
                firstGuild = 1; // Default guild ID
                _birthdays[firstGuild] = new List<PirateBirthday>();
            }
            
            _birthdays[firstGuild].Add(new PirateBirthday { UserId = userId, Birthday = birthday.ToString("dd.MM.yyyy") });
        }
        
        public static PirateBirthday? GetBirthday(ulong userId)
        {
            foreach (var guildBirthdays in _birthdays.Values)
            {
                var birthday = guildBirthdays.FirstOrDefault(b => b.UserId == userId);
                if (birthday != null) return birthday;
            }
            return null;
        }
        
        public static void RemoveBirthday(ulong userId)
        {
            foreach (var guildBirthdays in _birthdays.Values)
            {
                var birthday = guildBirthdays.FirstOrDefault(b => b.UserId == userId);
                if (birthday != null)
                {
                    guildBirthdays.Remove(birthday);
                    return;
                }
            }
        }
        
        public static Dictionary<ulong, PirateBirthday> GetAllBirthdays()
        {
            var result = new Dictionary<ulong, PirateBirthday>();
            foreach (var guildBirthdays in _birthdays.Values)
            {
                foreach (var birthday in guildBirthdays)
                {
                    result[birthday.UserId] = birthday;
                }
            }
            return result;
        }

        public static void HandleSelectMenuInteraction(SocketMessageComponent component) { }
        public static void HandleButtonInteraction(SocketMessageComponent component) { }

        // MEMORY OPTIMIZATION: Cleanup pirate service memory 
        public static void CleanupPirateMemory()
        {
            try
            {
                // Only run cleanup every 12 hours
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(12))
                    return;
                
                _lastCleanup = DateTime.UtcNow;
                var cleanupResults = new List<string>();
                
                // Cleanup players: limit per guild
                foreach (var kvp in _players.ToList())
                {
                    var guildId = kvp.Key;
                    var playerList = kvp.Value;
                    
                    if (playerList.Count > MAX_PLAYERS_PER_GUILD)
                    {
                        // Keep players with highest gold or most recent activity
                        var playersToKeep = playerList
                            .OrderByDescending(p => p.Gold)
                            .ThenByDescending(p => p.LastDaily)
                            .Take(MAX_PLAYERS_PER_GUILD)
                            .ToList();
                        
                        var removedCount = playerList.Count - MAX_PLAYERS_PER_GUILD;
                        _players[guildId] = playersToKeep;
                        cleanupResults.Add($"Removed {removedCount} old pirate players from guild {guildId}");
                    }
                }
                
                // Cleanup birthdays: limit per guild
                foreach (var kvp in _birthdays.ToList())
                {
                    var guildId = kvp.Key;
                    var birthdayList = kvp.Value;
                    
                    if (birthdayList.Count > MAX_BIRTHDAYS_PER_GUILD)
                    {
                        var birthdaysToKeep = birthdayList
                            .Take(MAX_BIRTHDAYS_PER_GUILD)
                            .ToList();
                        
                        var removedCount = birthdayList.Count - MAX_BIRTHDAYS_PER_GUILD;
                        _birthdays[guildId] = birthdaysToKeep;
                        cleanupResults.Add($"Removed {removedCount} old birthdays from guild {guildId}");
                    }
                }
                
                // Limit total guilds tracked
                if (_players.Count > MAX_GUILDS_TRACKED)
                {
                    var guildsToRemove = _players.Keys
                        .OrderBy(_ => Guid.NewGuid()) // Random selection
                        .Take(_players.Count - MAX_GUILDS_TRACKED)
                        .ToList();
                    
                    foreach (var guildId in guildsToRemove)
                    {
                        _players.Remove(guildId);
                        _birthdays.Remove(guildId);
                        _securityConfigs.Remove(guildId);
                        _bumpReminderChannels.Remove(guildId);
                        _lastBumpTimes.Remove(guildId);
                        _birthdayChannels.Remove(guildId);
                    }
                    
                    if (guildsToRemove.Count > 0)
                        cleanupResults.Add($"Removed data for {guildsToRemove.Count} excess guilds");
                }
                
                // Clean old bump times (older than 30 days)
                var oldBumpTimes = _lastBumpTimes
                    .Where(kvp => DateTime.UtcNow - kvp.Value > TimeSpan.FromDays(30))
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var guildId in oldBumpTimes)
                    _lastBumpTimes.Remove(guildId);
                
                if (oldBumpTimes.Count > 0)
                    cleanupResults.Add($"Cleaned {oldBumpTimes.Count} old bump times");
                
                if (cleanupResults.Count > 0)
                    Console.WriteLine($"🏴‍☠️ Pirate memory cleanup: {string.Join(", ", cleanupResults)}");
                else
                    Console.WriteLine($"🏴‍☠️ Pirate memory cleanup: No cleanup needed. Players: {_players.Values.Sum(list => list.Count)}, Birthdays: {_birthdays.Values.Sum(list => list.Count)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error during pirate memory cleanup: {ex.Message}");
            }
        }

        // Advanced Ticket Management Methods
        public static async Task<(bool success, SocketTextChannel? channel, ulong channelId)> CreateTicketChannelAsync(SocketGuild guild, SocketUser user, string category)
        {
            try
            {
                var config = GetTicketConfig(guild.Id);
                var random = new Random();
                var randomNumber = random.Next(1000, 9999);
                var channelName = $"┣s-ticket-{user.Username}-{randomNumber}";

                Console.WriteLine($"[PirateTicketDebug] Starting ticket creation for {user.Username} in guild {guild.Name}");

                var overwrites = new List<Overwrite>
                {
                    new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
                };

                // Add support roles if configured
                var roleIds = config.SupportRoleIds?.Any() == true ? config.SupportRoleIds : 
                             (config.SupportRoleId.HasValue ? new List<ulong> { config.SupportRoleId.Value } : new List<ulong>());

                foreach (var roleId in roleIds)
                {
                    overwrites.Add(new Overwrite(roleId, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));
                    Console.WriteLine($"[PirateTicketDebug] Added permissions for support role ID: {roleId}");
                }

                // DEBUG: Log category ID and found category name
                if (config?.TicketCategoryId.HasValue == true)
                {
                    var cat = guild.GetCategoryChannel(config.TicketCategoryId.Value);
                    Console.WriteLine($"[PirateTicketDebug] TicketCategoryId: {config.TicketCategoryId.Value}");
                    Console.WriteLine($"[PirateTicketDebug] Found category: {(cat != null ? cat.Name : "NOT FOUND")} (ID: {config.TicketCategoryId.Value})");
                }

                Console.WriteLine($"[PirateTicketDebug] Creating channel with name: {channelName}");

                var restChannel = await guild.CreateTextChannelAsync(channelName, properties =>
                {
                    if (config?.TicketCategoryId.HasValue == true)
                        properties.CategoryId = config.TicketCategoryId.Value;
                    properties.Topic = $"Support ticket for {user.Username} - Category: {category}";
                    properties.PermissionOverwrites = overwrites;
                });

                Console.WriteLine($"[PirateTicketDebug] Channel created successfully: {restChannel.Name} (ID: {restChannel.Id})");

                // Store ticket metadata using REST channel ID
                TicketMetas[restChannel.Id] = new PirateTicketMeta
                {
                    UserId = user.Id,
                    Category = category,
                    GuildId = guild.Id,
                    Username = user.Username
                };

                Console.WriteLine($"[PirateTicketDebug] Ticket metadata stored for channel {restChannel.Id}");

                // Send initial ticket message using REST channel directly
                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Ahoy! Welcome to your Support Ticket!")
                    .WithDescription($"**Greetings {user.Mention}!**\n\n" +
                                   "Our pirate crew will assist ye as soon as possible.\n" +
                                   "Please describe your issue in detail below.\n\n" +
                                   "**Category:** {category}\n" +
                                   "**Created:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                    .WithColor(0x40E0D0)
                    .WithFooter("🏴‍☠️ Mary Red Support System")
                    .WithCurrentTimestamp();

                var components = new ComponentBuilder()
                    .WithButton("🔒 Close Ticket", "pirate_close_ticket", ButtonStyle.Danger, new Emoji("🔒"))
                    .Build();

                await restChannel.SendMessageAsync(embed: embed.Build(), components: components);

                // Get SocketTextChannel for return
                var socketChannel = guild.GetTextChannel(restChannel.Id);

                // Start auto-close timer (24 hours)
                StartAutoCloseTimer(restChannel.Id);

                return (true, socketChannel, restChannel.Id);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PirateTicketDebug] Error creating ticket: {ex.Message}");
                return (false, null, 0);
            }
        }

        public static async Task CloseTicketChannel(SocketTextChannel channel, SocketUser? closedBy = null)
        {
            try
            {
                if (!TicketMetas.TryRemove(channel.Id, out var meta))
                {
                    Console.WriteLine($"[PirateTicketDebug] No metadata found for channel {channel.Id}");
                    // Still proceed with deletion
                }

                // Remove auto-close timer
                if (_autoCloseTimers.TryRemove(channel.Id, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }

                // Try to save transcript
                if (meta != null)
                {
                    await SaveTranscriptAsync(channel.Guild, channel, meta);
                }

                // Delete the channel
                await channel.DeleteAsync();

                Console.WriteLine($"[PirateTicketDebug] Successfully closed ticket channel {channel.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PirateTicketDebug] Error closing ticket: {ex.Message}");
            }
        }

        private static void StartAutoCloseTimer(ulong channelId)
        {
            var timer = new System.Timers.Timer(24 * 60 * 60 * 1000); // 24 hours
            timer.Elapsed += async (sender, e) =>
            {
                try
                {
                    // Find the channel - this is a simplified approach
                    Console.WriteLine($"[PirateTicketDebug] Auto-closing ticket {channelId} due to timeout");
                    
                    // Remove from timers first
                    if (_autoCloseTimers.TryRemove(channelId, out var t))
                    {
                        t.Stop();
                        t.Dispose();
                    }

                    // The actual channel cleanup would need to be handled differently
                    // since we don't have direct access to the guild from here
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PirateTicketDebug] Error in auto-close timer: {ex.Message}");
                }
            };
            timer.Start();
            _autoCloseTimers[channelId] = timer;
        }

        public static async Task<bool> SaveTranscriptAsync(SocketGuild guild, ITextChannel channel, PirateTicketMeta meta)
        {
            try
            {
                var messages = await channel.GetMessagesAsync(500).FlattenAsync();
                var orderedMessages = messages.OrderBy(m => m.Timestamp);

                var transcript = "=== PIRATE TICKET TRANSCRIPT ===\n";
                transcript += $"Ticket: {channel.Name}\n";
                transcript += $"Creator: {meta.Username} ({meta.UserId})\n";
                transcript += $"Category: {meta.Category}\n";
                transcript += $"Created: {meta.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC\n";
                transcript += $"Closed: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n";
                transcript += "============================\n\n";

                foreach (var msg in orderedMessages)
                {
                    if (!msg.Author.IsBot || !string.IsNullOrEmpty(msg.Content))
                    {
                        transcript += $"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss}] {msg.Author.Username}#{msg.Author.Discriminator}: {msg.Content}\n";

                        if (msg.Attachments.Any())
                        {
                            foreach (var attachment in msg.Attachments)
                            {
                                transcript += $"   [Attachment: {attachment.Filename} - {attachment.Url}]\n";
                            }
                        }
                    }
                }

                var filename = $"pirate_ticket_{meta.GuildId}_{channel.Id}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt";
                await File.WriteAllTextAsync(filename, transcript);

                var cfg = GetTicketConfig(meta.GuildId);
                if (cfg != null && cfg.LogChannelId != 0)
                {
                    var logChan = guild.GetTextChannel(cfg.LogChannelId);
                    if (logChan != null)
                    {
                        try
                        {
                            await logChan.SendFileAsync(filename, $"🏴‍☠️ Ticket transcript from **{channel.Name}** (created by <@{meta.UserId}>)");
                        }
                        catch { }
                    }
                }

                try { File.Delete(filename); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving pirate ticket transcript: {ex.Message}");
                return false;
            }
        }

        public static async Task HandleTicketInteraction(SocketInteraction component)
        {
            try
            {
                if (component is SocketMessageComponent messageComponent)
                {
                    if (messageComponent.Data.CustomId == "create_ticket")
                    {
                        await CreateTicketFromButton(messageComponent);
                    }
                    else if (messageComponent.Data.CustomId.StartsWith("ticket_close_"))
                    {
                        await CloseTicketFromButton(messageComponent);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling ticket interaction: {ex.Message}");
            }
        }

        private static async Task CreateTicketFromButton(SocketMessageComponent component)
        {
            // Implementation would go here for creating ticket from button
            await component.RespondAsync("🏴‍☠️ **Creating pirate ticket...**", ephemeral: true);
        }

        private static async Task CloseTicketFromButton(SocketMessageComponent component)
        {
            // Implementation would go here for closing ticket from button
            await component.RespondAsync("🏴‍☠️ **Closing pirate ticket...**", ephemeral: true);
        }
    }
}