using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Discord;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PiratBotCSharp.Modules
{
    public class VoiceTemplate
    {
        public string Name { get; set; } = "🏴‍☠️ Pirate Voice";
        public int Limit { get; set; } = 0;
    }

    public class VoiceConfig
    {
        public ulong? JoinToCreateChannel { get; set; }
        public ulong? JoinToCreateCategory { get; set; }
        public ulong? VoiceChannelCategory { get; set; }
        public ulong? VoiceLogChannel { get; set; }
        public ulong? AllowedRole { get; set; }
        public Dictionary<ulong, ActiveChannelInfo> ActiveChannels { get; set; } = new();
        public Dictionary<string, VoiceTemplate> Templates { get; set; } = new() { ["custom"] = new VoiceTemplate() };
        
        // Neue Unterstützung für mehrere Join-to-Create Channels
        public List<ulong> JoinToCreateChannels { get; set; } = new();
    }

    public class ActiveChannelInfo 
    { 
        public ulong OwnerId { get; set; }
        public long CreatedAt { get; set; }
        public string Template { get; set; } = "custom";
        public bool IsPrivate { get; set; } = false;
    }

    public class VoiceLogEntry
    {
        public ulong UserId { get; set; }
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
        public string ChannelName { get; set; } = "";
        public long Timestamp { get; set; }
    }

    public class VoiceUserStats
    {
        public string Username { get; set; } = "";
        public int TotalJoins { get; set; }
        public int ChannelsCreated { get; set; }
        public long TotalTimeSpent { get; set; }
        public long LastActivity { get; set; }
    }

    public class VoiceLogs
    {
        public List<VoiceLogEntry> Logs { get; set; } = new();
        public Dictionary<ulong, VoiceUserStats> Stats { get; set; } = new();
        
        // MEMORY OPTIMIZATION: Cleanup methods
        public void CleanupOldLogs(int maxLogs = 1000)
        {
            if (Logs.Count > maxLogs)
            {
                // Keep only the newest logs
                Logs = Logs.OrderByDescending(log => log.Timestamp).Take(maxLogs).ToList();
            }
        }
        
        public void CleanupInactiveUsers(int maxUsers = 1000, int inactiveDays = 30) // Reduced from 5000 to 1000 for memory optimization
        {
            if (Stats.Count > maxUsers)
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-inactiveDays).ToUnixTimeSeconds();
                var inactiveUsers = Stats
                    .Where(kvp => kvp.Value.LastActivity < cutoff)
                    .OrderBy(kvp => kvp.Value.LastActivity)
                    .Take(Stats.Count - maxUsers)
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                foreach (var userId in inactiveUsers)
                    Stats.Remove(userId);
            }
        }
    }

    public static class PirateVoiceService
    {
        private const string VOICE_CONFIG_FILE = "pirate_voice_config.json";
        private const string VOICE_LOGS_FILE = "pirate_voice_logs.json";
        private static Dictionary<ulong, VoiceConfig> _configs = LoadVoiceConfigs();
        private static VoiceLogs _logs = LoadVoiceLogs();

        // Get Voice Config für spezifischen Guild
        public static VoiceConfig GetConfig(ulong guildId)
        {
            if (!_configs.TryGetValue(guildId, out var config))
            {
                config = new VoiceConfig();
                _configs[guildId] = config;
                SaveVoiceConfigs();
            }
            return config;
        }

        // Set Voice Config für spezifischen Guild
        public static void SetConfig(ulong guildId, VoiceConfig config)
        {
            _configs[guildId] = config;
            SaveVoiceConfigs();
        }

        // Remove Voice Config für spezifischen Guild
        public static void RemoveConfig(ulong guildId)
        {
            if (_configs.ContainsKey(guildId))
            {
                _configs.Remove(guildId);
                SaveVoiceConfigs();
            }
        }

        private static Dictionary<ulong, VoiceConfig> LoadVoiceConfigs()
        {
            try
            {
                if (!File.Exists(VOICE_CONFIG_FILE)) return new Dictionary<ulong, VoiceConfig>();
                var txt = File.ReadAllText(VOICE_CONFIG_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, VoiceConfig>>(txt) ?? new Dictionary<ulong, VoiceConfig>();
            }
            catch { return new Dictionary<ulong, VoiceConfig>(); }
        }

        private static void SaveVoiceConfigs()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(VOICE_CONFIG_FILE, txt);
            }
            catch { }
        }

        private static VoiceLogs LoadVoiceLogs()
        {
            try
            {
                if (!File.Exists(VOICE_LOGS_FILE)) return new VoiceLogs();
                var txt = File.ReadAllText(VOICE_LOGS_FILE);
                return JsonSerializer.Deserialize<VoiceLogs>(txt) ?? new VoiceLogs();
            }
            catch { return new VoiceLogs(); }
        }

        private static void SaveVoiceLogs()
        {
            try
            {
                // MEMORY OPTIMIZATION: Cleanup before saving
                _logs.CleanupOldLogs();
                _logs.CleanupInactiveUsers();
                
                var txt = JsonSerializer.Serialize(_logs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(VOICE_LOGS_FILE, txt);
            }
            catch (Exception ex) { Console.WriteLine("🏴‍☠️ Failed to save voice logs: " + ex); }
        }

        public static void SetVoiceLogChannel(ulong guildId, ulong channelId)
        {
            var config = GetConfig(guildId);
            config.VoiceLogChannel = channelId;
            SetConfig(guildId, config);
        }

        public static void SetVoiceCategory(ulong guildId, ulong categoryId)
        {
            var config = GetConfig(guildId);
            config.VoiceChannelCategory = categoryId;
            SetConfig(guildId, config);
        }

        public static void SetAllowedRole(ulong guildId, ulong roleId)
        {
            var config = GetConfig(guildId);
            config.AllowedRole = roleId;
            SetConfig(guildId, config);
        }

        public static void SetJoinToCreateChannel(ulong guildId, ulong channelId, ulong categoryId)
        {
            var config = GetConfig(guildId);
            
            // Für Rückwärtskompatibilität: Erstes Setup verwendet die alten Properties
            if (config.JoinToCreateChannel == null)
            {
                config.JoinToCreateChannel = channelId;
                config.JoinToCreateCategory = categoryId;
            }
            
            // Für mehrere Channels: Füge zu Liste hinzu ohne zu überschreiben
            if (!config.JoinToCreateChannels.Contains(channelId))
            {
                config.JoinToCreateChannels.Add(channelId);
            }
            
            SetConfig(guildId, config);
            
            Console.WriteLine($"🏴‍☠️ Added Join-to-Create Channel {channelId} in category {categoryId}");
            Console.WriteLine($"🏴‍☠️ Total Join-to-Create Channels: {config.JoinToCreateChannels.Count + 1}"); // +1 für old system
        }

        public static async Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            try
            {
                var guild = (oldState.VoiceChannel ?? newState.VoiceChannel)?.Guild;
                if (guild == null) return;

                var member = guild.GetUser(user.Id);
                if (member == null) return;

                // User joined a channel
                if (oldState.VoiceChannel == null && newState.VoiceChannel != null)
                {
                    await HandleUserJoined(member, newState.VoiceChannel);
                }
                // User left a channel
                else if (oldState.VoiceChannel != null && newState.VoiceChannel == null)
                {
                    await HandleUserLeft(member, oldState.VoiceChannel);
                }
                // User moved between channels
                else if (oldState.VoiceChannel != null && newState.VoiceChannel != null && oldState.VoiceChannel.Id != newState.VoiceChannel.Id)
                {
                    await HandleUserLeft(member, oldState.VoiceChannel);
                    await HandleUserJoined(member, newState.VoiceChannel);
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"🏴‍☠️ Error in HandleVoiceStateUpdated: {ex.Message}"); 
            }
        }

        private static async Task HandleUserJoined(SocketGuildUser member, SocketVoiceChannel channel)
        {
            var cfg = GetConfig(member.Guild.Id);
            
            // Check if user joined ANY Join-to-Create Channel (backwards compatibility + new system)
            bool isJoinToCreateChannel = false;
            
            // Check old system (backwards compatibility)
            if (cfg?.JoinToCreateChannel != null && channel.Id == cfg.JoinToCreateChannel.Value)
            {
                isJoinToCreateChannel = true;
            }
            
            // Check new system (multiple Join-to-Create Channels)
            if (cfg?.JoinToCreateChannels != null && cfg.JoinToCreateChannels.Contains(channel.Id))
            {
                isJoinToCreateChannel = true;
            }
            
            if (!isJoinToCreateChannel) return;

            Console.WriteLine($"🏴‍☠️ {member.Username} joined Join-to-Create channel: {channel.Name}");

            // Check role permission
            if (cfg.AllowedRole.HasValue)
            {
                var allowedRole = member.Guild.GetRole(cfg.AllowedRole.Value);
                if (allowedRole != null && !member.Roles.Contains(allowedRole))
                {
                    Console.WriteLine($"🏴‍☠️ {member.Username} lacks required role");
                    return;
                }
            }

            await CreateVoiceChannel(member, channel.Guild, cfg);
        }

        private static async Task HandleUserLeft(SocketGuildUser member, SocketVoiceChannel channel)
        {
            var cfg = GetConfig(member.Guild.Id);
            if (cfg?.ActiveChannels == null || !cfg.ActiveChannels.ContainsKey(channel.Id)) return;

            Console.WriteLine($"🏴‍☠️ {member.Username} left pirate channel: {channel.Name}");

            // Log the voice activity
            await LogVoiceActivity(member, "Left", channel.Name);

            // Delete channel if empty (with retry mechanism)
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await Task.Delay(100 + (i * 100)); // 100ms, 200ms, 300ms, 400ms, 500ms delays
                        
                        var guild = channel.Guild;
                        var currentChannel = guild.GetVoiceChannel(channel.Id);
                        var currentConfig = GetConfig(guild.Id); // Get fresh config each time
                        
                        if (currentChannel == null)
                        {
                            currentConfig.ActiveChannels.Remove(channel.Id);
                            SetConfig(guild.Id, currentConfig);
                            return;
                        }
                        
                        // Count only real users (not bots, not deafened/muted in weird states)
                        var realUsers = currentChannel.Users.Where(u => !u.IsBot && u.VoiceChannel?.Id == currentChannel.Id).ToList();
                        
                        if (realUsers.Count == 0)
                        {
                            Console.WriteLine($"🏴‍☠️ Deleting empty pirate cabin: {currentChannel.Name}");
                            await currentChannel.DeleteAsync(new RequestOptions { AuditLogReason = "🏴‍☠️ Pirate cabin empty" });
                            
                            currentConfig.ActiveChannels.Remove(channel.Id);
                            SetConfig(guild.Id, currentConfig);
                            
                            Console.WriteLine($"🏴‍☠️ Successfully deleted: {channel.Name}");
                            return;
                        }
                        else
                        {
                            Console.WriteLine($"🏴‍☠️ Cabin {currentChannel.Name} still has {realUsers.Count} real pirates (total: {currentChannel.Users.Count})");
                            foreach (var u in currentChannel.Users)
                            {
                                Console.WriteLine($"  - {u.Username} (Bot: {u.IsBot}, VoiceChannel: {u.VoiceChannel?.Name ?? "null"})");
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"🏴‍☠️ Deletion attempt {i + 1} failed: {ex.Message}");
                    }
                }
            });
        }

        private static async Task CreateVoiceChannel(SocketGuildUser member, SocketGuild guild, VoiceConfig cfg)
        {
            try
            {
                var template = cfg.Templates.ContainsKey("custom") ? cfg.Templates["custom"] : cfg.Templates.Values.First();
                var channelName = $"🏴‍☠️ {member.Username}'s Cabin";
                var categoryId = cfg.VoiceChannelCategory ?? cfg.JoinToCreateCategory;

                Console.WriteLine($"🏴‍☠️ Creating pirate cabin: {channelName}");

                var newChannel = await guild.CreateVoiceChannelAsync(channelName, prop =>
                {
                    if (categoryId.HasValue) prop.CategoryId = categoryId.Value;
                    prop.UserLimit = template.Limit;
                    prop.PermissionOverwrites = new[] {
                        new Overwrite(member.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow, speak: PermValue.Allow, manageChannel: PermValue.Allow))
                    };
                });

                // Move user to new channel
                try 
                { 
                    await member.ModifyAsync(properties => properties.ChannelId = newChannel.Id);
                    Console.WriteLine($"🏴‍☠️ Moved {member.Username} to {newChannel.Name}");
                } 
                catch (Exception moveEx) 
                { 
                    Console.WriteLine($"🏴‍☠️ Failed to move pirate: {moveEx.Message}");
                }

                // Add to active channels
                cfg.ActiveChannels[newChannel.Id] = new ActiveChannelInfo 
                { 
                    OwnerId = member.Id, 
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Template = "custom"
                };
                SetConfig(guild.Id, cfg);

                Console.WriteLine($"🏴‍☠️ Successfully created cabin for {member.Username}");

                // Log the voice activity
                await LogVoiceActivity(member, "Created Cabin", newChannel.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error creating pirate cabin: {ex.Message}");
            }
        }

        private static async Task LogVoiceActivity(SocketGuildUser member, string action, string channelName)
        {
            try
            {
                var config = GetConfig(member.Guild.Id);
                if (config.VoiceLogChannel == null) return;

                var guild = member.Guild;
                var logChannel = guild.GetTextChannel(config.VoiceLogChannel.Value);
                if (logChannel == null) return;

                // Create log entry
                var logEntry = new VoiceLogEntry
                {
                    UserId = member.Id,
                    Username = member.Username,
                    Action = action,
                    ChannelName = channelName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // Save to logs
                _logs.Logs.Add(logEntry);
                SaveVoiceLogs();

                // Send to Discord channel
                var embed = new EmbedBuilder()
                    .WithAuthor(member.Username, member.GetAvatarUrl() ?? member.GetDefaultAvatarUrl())
                    .WithDescription($"🏴‍☠️ **{action}** `{channelName}`")
                    .WithColor(action.Contains("Left") ? Color.Red : Color.Green)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error logging voice activity: {ex.Message}");
            }
        }

        // Public methods for commands
        public static ActiveChannelInfo? GetActiveChannelByOwner(ulong guildId, ulong ownerId) => GetConfig(guildId).ActiveChannels.Values.FirstOrDefault(a => a.OwnerId == ownerId);
        public static VoiceLogs GetLogs() => _logs;
        public static void RemoveActiveChannel(ulong guildId, ulong channelId) 
        { 
            var config = GetConfig(guildId);
            if (config.ActiveChannels.ContainsKey(channelId)) 
            { 
                config.ActiveChannels.Remove(channelId); 
                SetConfig(guildId, config);
            } 
        }
    }

    public class VoiceCommands : ModuleBase<SocketCommandContext>
    {
        [Command("pirate-voice-setup")]
        [Summary("🏴‍☠️ Setup pirate voice system - creates everything automatically!")]
        [RequirePirateAdmin]
        public async Task AutoVoiceSetupAsync()
        {
            try
            {
                // Create Auto-Voice category
                var category = await Context.Guild.CreateCategoryChannelAsync("🏴‍☠️ Pirate Voice Channels");
                
                // Create Join-to-Create channel
                var joinChannel = await Context.Guild.CreateVoiceChannelAsync("➕ Join to Create Cabin", properties =>
                {
                    properties.CategoryId = category.Id;
                    properties.UserLimit = 0;
                });

                // Create log channel
                var logChannel = await Context.Guild.CreateTextChannelAsync("🏴‍☠️-voice-logs", properties =>
                {
                    properties.CategoryId = category.Id;
                });

                // Save configuration
                PirateVoiceService.SetJoinToCreateChannel(Context.Guild.Id, joinChannel.Id, category.Id);
                PirateVoiceService.SetVoiceCategory(Context.Guild.Id, category.Id);
                PirateVoiceService.SetVoiceLogChannel(Context.Guild.Id, logChannel.Id);

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Pirate Voice System Setup Complete!")
                    .WithColor(Color.Gold)
                    .WithDescription("**Ahoy! Your pirate voice system is ready to sail! ⚓**")
                    .AddField("📂 Category", category.Name, true)
                    .AddField("➕ Join Channel", joinChannel.Mention, true)
                    .AddField("📋 Log Channel", logChannel.Mention, true)
                    .AddField("🏴‍☠️ How it works:", 
                        "• Join the **➕ Join to Create Cabin** channel\n" +
                        "• A new private pirate cabin will be created for you\n" +
                        "• You'll be automatically moved to your new cabin\n" +
                        "• Cabin gets deleted when empty\n" +
                        "• Use `?voicename <name>` to rename your cabin\n" +
                        "• Use `?voicelimit <number>` to set user limit", false)
                    .WithFooter("🏴‍☠️ Barbossa Auto Voice System")
                    .WithCurrentTimestamp()
                    .Build();

                await ReplyAsync(embed: embed);

                Console.WriteLine($"🏴‍☠️ Pirate Voice System set up for {Context.Guild.Name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ **Setup failed, ye scurvy dog!** {ex.Message}");
                Console.WriteLine($"🏴‍☠️ Pirate Voice Setup Error: {ex}");
            }
        }

        [Command("voicesetup")]
        [Summary("🏴‍☠️ Setup pirate voice system with custom settings (Admin only)")]
        [RequirePirateAdmin]
        public async Task VoiceSetupAsync(ulong logChannelId, ulong voiceCategoryId, ulong roleId)
        {
            try
            {
                // Validate inputs
                var logChannel = Context.Guild.GetTextChannel(logChannelId);
                var voiceCategory = Context.Guild.GetCategoryChannel(voiceCategoryId);
                var allowedRole = Context.Guild.GetRole(roleId);

                if (logChannel == null)
                {
                    await ReplyAsync("❌ Invalid log channel ID, matey!");
                    return;
                }
                if (voiceCategory == null)
                {
                    await ReplyAsync("❌ Invalid voice category ID, ye landlubber!");
                    return;
                }
                if (allowedRole == null)
                {
                    await ReplyAsync("❌ Invalid role ID, arr!");
                    return;
                }

                // Update configuration
                PirateVoiceService.SetVoiceLogChannel(Context.Guild.Id, logChannelId);
                PirateVoiceService.SetVoiceCategory(Context.Guild.Id, voiceCategoryId);
                PirateVoiceService.SetAllowedRole(Context.Guild.Id, roleId);

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Pirate Voice System Configured!")
                    .WithColor(Color.Gold)
                    .AddField("📋 Log Channel", logChannel.Mention, true)
                    .AddField("📂 Voice Category", voiceCategory.Name, true)
                    .AddField("🏴‍☠️ Allowed Role", allowedRole.Mention, true)
                    .WithDescription("**Pirate voice system configured successfully!** ⚔️\n\n" +
                        "Use `?create [category-id]` to create Join-to-Create channels")
                    .WithFooter("🏴‍☠️ Barbossa Voice System")
                    .WithCurrentTimestamp()
                    .Build();
                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ **Setup failed:** {ex.Message}");
            }
        }

        [Command("create")]
        [Summary("🏴‍☠️ Create additional Join-to-Create pirate cabin (Admin only)")]
        [RequirePirateAdmin]
        public async Task CreateJoinToCreateAsync(ulong categoryId)
        {
            try
            {
                var category = Context.Guild.GetCategoryChannel(categoryId);
                if (category == null)
                {
                    await ReplyAsync("❌ Invalid category ID, matey!");
                    return;
                }

                var channel = await Context.Guild.CreateVoiceChannelAsync("➕ Create Pirate Cabin", properties =>
                {
                    properties.CategoryId = categoryId;
                    properties.UserLimit = 0;
                });

                PirateVoiceService.SetJoinToCreateChannel(Context.Guild.Id, channel.Id, categoryId);

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ New Join-to-Create Cabin Created!")
                    .WithColor(Color.Gold)
                    .AddField("⚓ Channel", channel.Mention, true)
                    .AddField("📂 Category", category.Name, true)
                    .WithDescription("**Ahoy! Another Join-to-Create cabin is ready for yer crew!** ⚔️\n\n" +
                        "Pirates can now join this channel to create their own private cabins!\n" +
                        "This works parallel to all other Join-to-Create channels!")
                    .WithFooter("🏴‍☠️ Barbossa - Supporting unlimited Join-to-Create channels!")
                    .WithCurrentTimestamp()
                    .Build();
                await ReplyAsync(embed: embed);

                Console.WriteLine($"🏴‍☠️ Created additional Join-to-Create channel for {Context.Guild.Name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ **Creation failed, ye scurvy dog!** {ex.Message}");
                Console.WriteLine($"🏴‍☠️ Error creating Join-to-Create channel: {ex}");
            }
        }

        [Command("voice-cleanup")]
        [Summary("🏴‍☠️ Clean up all empty pirate cabins (Admin only)")]
        [RequirePirateAdmin]
        public async Task VoiceCleanupAsync()
        {
            try
            {
                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                if (config?.VoiceChannelCategory == null)
                {
                    await ReplyAsync("❌ Pirate voice system not configured, matey! Use `?pirate-voice-setup` first.");
                    return;
                }

                var category = Context.Guild.GetCategoryChannel(config.VoiceChannelCategory.Value);
                if (category == null)
                {
                    await ReplyAsync("❌ Voice category not found, ye landlubber!");
                    return;
                }

                int cleanedCount = 0;
                var activeChannelsToRemove = new List<ulong>();

                foreach (var channelId in config.ActiveChannels.Keys.ToList())
                {
                    var channel = Context.Guild.GetVoiceChannel(channelId);
                    if (channel == null || channel.Users.Count == 0)
                    {
                        if (channel != null)
                        {
                            await channel.DeleteAsync(new RequestOptions { AuditLogReason = "🏴‍☠️ Pirate cabin cleanup - empty" });
                            Console.WriteLine($"🏴‍☠️ Deleted empty cabin: {channel.Name}");
                        }
                        activeChannelsToRemove.Add(channelId);
                        cleanedCount++;
                    }
                }

                // Remove from active channels
                foreach (var channelId in activeChannelsToRemove)
                {
                    PirateVoiceService.RemoveActiveChannel(Context.Guild.Id, channelId);
                }

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Pirate Cabin Cleanup Complete!")
                    .WithDescription($"**Cleaned up {cleanedCount} empty pirate cabins, arr!** ⚓")
                    .WithColor(cleanedCount > 0 ? Color.Green : Color.Orange)
                    .WithFooter("🏴‍☠️ Keep yer ship tidy, matey!")
                    .WithCurrentTimestamp()
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ **Cleanup failed:** {ex.Message}");
                Console.WriteLine($"🏴‍☠️ Voice cleanup error: {ex}");
            }
        }

        [Command("remove-voice-setup")]
        [Summary("🏴‍☠️ Remove entire pirate voice system (Admin only)")]
        [RequirePirateAdmin]
        public async Task RemoveVoiceSetupAsync()
        {
            try
            {
                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                
                // Delete all active channels first
                foreach (var channelId in config.ActiveChannels.Keys.ToList())
                {
                    var channel = Context.Guild.GetVoiceChannel(channelId);
                    if (channel != null)
                    {
                        try
                        {
                            await channel.DeleteAsync(new RequestOptions { AuditLogReason = "🏴‍☠️ Voice system removal" });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"🏴‍☠️ Could not delete channel {channel.Name}: {ex.Message}");
                        }
                    }
                }

                // Remove configuration
                PirateVoiceService.RemoveConfig(Context.Guild.Id);

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Pirate Voice System Removed!")
                    .WithDescription("**The entire pirate voice system has been removed from yer ship!** ⚓\n\n" +
                        "All active cabins have been deleted.\n" +
                        "Use `?pirate-voice-setup` to set it up again.")
                    .WithColor(Color.Red)
                    .WithFooter("🏴‍☠️ Farewell, voice system!")
                    .WithCurrentTimestamp()
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ **Removal failed:** {ex.Message}");
            }
        }

        [Command("voicename")]
        [Summary("🏴‍☠️ Rename your pirate cabin")]
        public async Task VoiceNameAsync([Remainder] string name)
        {
            try
            {
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice cabin to rename it, matey!");
                    return;
                }

                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only rename pirate cabins created by the system!");
                    return;
                }

                await userChannel.ModifyAsync(p => p.Name = $"🏴‍☠️ {name}");
                await ReplyAsync($"✅ Pirate cabin renamed to **🏴‍☠️ {name}**");
                
                Console.WriteLine($"🏴‍☠️ {Context.User.Username} renamed cabin to: {name}");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to rename cabin: {ex.Message}");
            }
        }

        [Command("voicelimit")]
        [Summary("🏴‍☠️ Set user limit for your pirate cabin (0-99, 0=unlimited)")]
        public async Task VoiceLimitAsync(int limit)
        {
            try
            {
                if (limit < 0 || limit > 99)
                {
                    await ReplyAsync("❌ Limit must be between 0 and 99 (0 = unlimited)");
                    return;
                }

                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice cabin, matey!");
                    return;
                }

                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only set limits on pirate cabins!");
                    return;
                }

                await userChannel.ModifyAsync(p => p.UserLimit = limit);
                
                var limitText = limit == 0 ? "unlimited" : limit.ToString();
                await ReplyAsync($"✅ Pirate cabin limit set to **{limitText}**");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to set limit: {ex.Message}");
            }
        }

        [Command("voicelock")]
        [Summary("🏴‍☠️ Make your pirate cabin private")]
        public async Task VoiceLockAsync()
        {
            try
            {
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice cabin to lock it, matey!");
                    return;
                }

                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only lock pirate cabins created by the system!");
                    return;
                }

                var channelInfo = config.ActiveChannels[userChannel.Id];
                if (channelInfo.OwnerId != Context.User.Id)
                {
                    await ReplyAsync("❌ Only the cabin owner can lock it, arr!");
                    return;
                }

                // Set permissions to deny @everyone from viewing and connecting
                await userChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, 
                    new OverwritePermissions(viewChannel: PermValue.Deny, connect: PermValue.Deny));

                channelInfo.IsPrivate = true;
                config.ActiveChannels[userChannel.Id] = channelInfo;
                PirateVoiceService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync("🔒 **Pirate cabin locked!** Only you and invited crew members can enter, arr!");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to lock cabin: {ex.Message}");
            }
        }

        [Command("voiceunlock")]
        [Summary("🏴‍☠️ Make your pirate cabin public")]
        public async Task VoiceUnlockAsync()
        {
            try
            {
                var userChannel = (Context.User as SocketGuildUser)?.VoiceChannel;
                if (userChannel == null)
                {
                    await ReplyAsync("❌ You must be in a voice cabin to unlock it, matey!");
                    return;
                }

                var config = PirateVoiceService.GetConfig(Context.Guild.Id);
                if (!config.ActiveChannels.ContainsKey(userChannel.Id))
                {
                    await ReplyAsync("❌ You can only unlock pirate cabins created by the system!");
                    return;
                }

                var channelInfo = config.ActiveChannels[userChannel.Id];
                if (channelInfo.OwnerId != Context.User.Id)
                {
                    await ReplyAsync("❌ Only the cabin owner can unlock it, arr!");
                    return;
                }

                // Remove the deny permissions for @everyone
                await userChannel.RemovePermissionOverwriteAsync(Context.Guild.EveryoneRole);

                channelInfo.IsPrivate = false;
                config.ActiveChannels[userChannel.Id] = channelInfo;
                PirateVoiceService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync("🔓 **Pirate cabin unlocked!** All crew members can now enter, arr!");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Failed to unlock cabin: {ex.Message}");
            }
        }

        [Command("voicehelp")]
        [Summary("🏴‍☠️ Show pirate voice system help")]
        public async Task VoiceHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Pirate Voice System Help")
                .WithColor(0x8B4513)
                .AddField("**Admin Setup Commands**",
                    "`?pirate-voice-setup` - Auto setup with default settings\n" +
                    "`?voicesetup [log-id] [category-id] [role-id]` - Manual setup\n" +
                    "`?create [category-id]` - Create additional Join-to-Create cabins\n" +
                    "`?voice-cleanup` - Clean up empty cabins\n" +
                    "`?remove-voice-setup` - Remove entire voice system", false)
                .AddField("**Pirate Cabin Commands**",
                    "`?voicename [name]` - Rename your pirate cabin\n" +
                    "`?voicelimit [0-99]` - Set crew limit (0=unlimited)\n" +
                    "`?voicelock` - Make your cabin private\n" +
                    "`?voiceunlock` - Make your cabin public", false)
                .AddField("**🏴‍☠️ How it works:**",
                    "• Join any **➕ Create** channel\n" +
                    "• Your private pirate cabin is created automatically\n" +
                    "• You get full control over your cabin\n" +
                    "• Cabin is deleted when empty\n" +
                    "• **Multiple Join-to-Create channels supported!**", false)
                .WithFooter("🏴‍☠️ Barbossa - The most advanced pirate voice system!")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }
    }
}