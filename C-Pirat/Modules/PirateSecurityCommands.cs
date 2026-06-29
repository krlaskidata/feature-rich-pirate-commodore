using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    // =========================
    // Config Models
    // =========================

    public class SecurityConfigEntry
    {
        // Master switch
        public bool Enabled { get; set; } = false;

        // Where security events get logged (optional)
        public ulong? LogChannelId { get; set; } = null;
        public ulong? WarnChannelId { get; set; } = null;
        public ulong? AdminRoleId { get; set; } = null;
        public List<ulong> AdminRoleIds { get; set; } = new();
        public ulong? SecurityTicketCategoryId { get; set; } = null;
        public ulong? Age18RoleId { get; set; } = null;
        public ulong? VerifyRoleId { get; set; } = null;
        public ulong? Age18VoiceChannelId { get; set; } = null;

        // Behavior toggles (Sapphire-like calm defaults)
        public bool DeleteOnHighSeverity { get; set; } = true;
        public bool WarnUserOnMediumPlus { get; set; } = true;
        public bool TimeoutEnabled { get; set; } = false;
        public bool AutoBanSuspiciousUsers { get; set; } = false;

        // Timeout only after strikes threshold for High/Critical
        public int TimeoutSeconds { get; set; } = 600; // 10 min
        public int MaxStrikesBeforeTimeout { get; set; } = 3;

        // Prevent bot spamming warnings/logs for the same user repeatedly
        public int CooldownSeconds { get; set; } = 30;

        // Scam rules (false-positive safe)
        public bool BlockInvites { get; set; } = true;
        public bool BlockLinks { get; set; } = false; // default off; scam uses link+keyword gating anyway

        // Link whitelist (channels where links are allowed even if BlockLinks is on)
        public List<ulong> AllowedLinkChannelIds { get; set; } = new();

        // Ignore admins by default (also hard-coded in handler)
        public List<ulong> IgnoreRoleIds { get; set; } = new();
        public List<ulong> IgnoreChannelIds { get; set; } = new();
    }

    public class CleanupIntervalEntry
    {
        public ulong ChannelId { get; set; }
        public bool Enabled { get; set; } = true;
        public int IntervalHours { get; set; } = 1;
    }

    // =========================
    // Moderation Models
    // =========================

    internal enum ViolationCategory
    {
        None,
        Invite,
        GenericLink,
        Spam,
        Scam,
        HardLanguage,
        SoftLanguage,
        SerbianLanguage,
        NsfwAttachment,
        Caps,
        Gibberish,
        LinkSpam
    }

    internal enum ViolationSeverity
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }

    internal sealed class ViolationResult
    {
        public bool IsViolation => Severity != ViolationSeverity.None && Category != ViolationCategory.None;
        public ViolationCategory Category { get; set; } = ViolationCategory.None;
        public ViolationSeverity Severity { get; set; } = ViolationSeverity.None;
        public string Reason { get; set; } = "—";
        public string Matched { get; set; } = "—";

        // Decision flags (computed)
        public bool ShouldDelete { get; set; }
        public bool ShouldWarn { get; set; }
        public bool ShouldStrike { get; set; }
        public bool ShouldTimeout { get; set; }
    }

    internal sealed class StrikeEntry
    {
        public int Strikes { get; set; }
        public DateTimeOffset LastActionAt { get; set; } = DateTimeOffset.MinValue;
    }

    internal sealed class SecurityStrikeEntryDto
    {
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public int Strikes { get; set; }
        public DateTimeOffset LastActionAt { get; set; }
    }

    internal sealed class SecurityAppealTicketMeta
    {
        public ulong ChannelId { get; set; }
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public List<ulong> AdminRoleIds { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string Reason { get; set; } = "Security appeal";
        public string Category { get; set; } = "Unknown";
    }

    // =========================
    // Embeds
    // =========================

    internal static class EmbedFactory
    {
        private static readonly Color Info = new Color(0x2F3136);
        private static readonly Color Ok = new Color(0x57F287);
        private static readonly Color Warn = new Color(0xFEE75C);
        private static readonly Color Bad = new Color(0xED4245);

        public static Embed BuildInfo(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Info)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildSuccess(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Ok)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildWarning(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Warn)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildError(string title, string description)
            => new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(Bad)
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

        public static Embed BuildSecurityLog(SocketGuild guild, SocketUserMessage message, ViolationResult v, string actionTaken)
        {
            var user = message.Author;
            var chan = message.Channel;

            var desc = $"**Category:** {v.Category}\n" +
                       $"**Severity:** {v.Severity}\n" +
                       $"**Reason:** {v.Reason}\n" +
                       $"**Matched:** `{SafeSnippet(v.Matched, 120)}`\n" +
                       $"**Action:** {actionTaken}";

            var eb = new EmbedBuilder()
                .WithTitle("Security Event")
                .WithDescription(desc)
                .WithColor(v.Severity >= ViolationSeverity.High ? new Color(0xED4245) : new Color(0xFEE75C))
                .AddField("User", $"{user.Username}#{(user as SocketGuildUser)?.Discriminator ?? "0000"}\n`{user.Id}`", true)
                .AddField("Channel", $"{chan.Name}\n`{chan.Id}`", true)
                .AddField("Message", SafeSnippet(message.Content ?? "—", 300), false)
                .WithFooter($"Guild: {guild.Name} • {guild.Id}")
                .WithTimestamp(DateTimeOffset.UtcNow);

            return eb.Build();
        }

        public static Embed BuildSuspiciousJoinLog(SocketGuildUser user, IEnumerable<string> reasons)
        {
            var guild = user.Guild;
            var desc =
                $"**User:** {user.Mention}\n" +
                $"**User ID:** `{user.Id}`\n" +
                $"**Account Created:** <t:{user.CreatedAt.ToUnixTimeSeconds()}:F> (<t:{user.CreatedAt.ToUnixTimeSeconds()}:R>)\n" +
                $"**Joined:** <t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F> (<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>)\n\n" +
                $"**Signals:**\n- {string.Join("\n- ", reasons)}\n\n" +
                $"**Suggested commands:**\n" +
                $"`?ban {user.Id} <reason>`\n" +
                $"`?kick {user.Id} <reason>`\n" +
                $"`?timeout {user.Id} <minutes> <reason>`";

            return new EmbedBuilder()
                .WithTitle("Suspicious User Joined")
                .WithDescription(desc)
                .WithColor(new Color(0xFEE75C))
                .WithThumbnailUrl(user.GetAvatarUrl(size: 256) ?? user.GetDefaultAvatarUrl())
                .WithFooter($"Guild: {guild.Name} • {guild.Id}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
        }

        private static string SafeSnippet(string input, int max)
        {
            if (string.IsNullOrWhiteSpace(input)) return "—";
            input = input.Replace("`", "'");
            return input.Length <= max ? input : input.Substring(0, max) + "...";
        }
    }

    // =========================
    // Security Service
    // =========================

    public static class SecurityService
    {
        private const string SECURITY_FILE = "security_config.json";
        private const string CLEANUP_INTERVALS_FILE = "cleanup_intervals.json";
        private const string STRIKES_FILE = "security_strikes.json";
        private const string SECURITY_TICKETS_FILE = "security_appeal_tickets.json";

        private static Dictionary<ulong, SecurityConfigEntry> _config = LoadSecurityConfig();
        private static Dictionary<ulong, CleanupIntervalEntry> _cleanupIntervals = LoadCleanupIntervals();
        private static Dictionary<ulong, SecurityAppealTicketMeta> _securityTickets = LoadSecurityTickets();
        private static readonly Dictionary<ulong, System.Timers.Timer> _timers = new Dictionary<ulong, System.Timers.Timer>();

        // Strike tracking: (GuildId, UserId) -> entry - MEMORY OPTIMIZED!
        private static readonly Dictionary<(ulong GuildId, ulong UserId), StrikeEntry> _strikes = LoadStrikes();
        private static readonly object _strikeLock = new();
        
        // Memory optimization constants
        private const int MAX_STRIKES_ENTRIES = 1000; // Reduced from 5000 to 1000 for memory optimization
        private const int MAX_LOG_FILE_SIZE_MB = 10; // Rotate logs at 10MB
        private const int CLEANUP_INTERVAL_HOURS = 24; // Daily cleanup
        
        private static DateTime _lastCleanup = DateTime.UtcNow;

        // Image spam tracking: userId → list of timestamps when they sent image messages
        private static readonly Dictionary<ulong, List<DateTimeOffset>> _imageSendTimes = new();
        private static readonly object _imageLock = new();

        // Link spam tracking: userId → list of timestamps when they sent link messages
        private static readonly Dictionary<ulong, List<DateTimeOffset>> _linkSendTimes = new();
        private static readonly object _linkLock = new();

        // -------------------------
        // Regex (compiled, safe)
        // -------------------------

        private static readonly Regex InviteRegex =
            new(@"(discord\.gg\/|discord(app)?\.com\/invite\/)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LinkRegex =
            new(@"(https?:\/\/|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Scam keywords: ONLY used when message contains a link
        private static readonly List<Regex> ScamKeywords = new()
        {
            new(@"free\s*nitro", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"discord\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"steam\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"crypto\s*airdrop", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"claim\s*(now|here)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"limited\s*time", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\b(nudes?|nude|porn|nsfw|adult)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static readonly List<Regex> StandaloneScamKeywords = new()
        {
            new(@"free\s*nitro", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"discord\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"steam\s*gift", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"claim\s*(now|here)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"limited\s*time", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Hard block: keep only truly severe items (minimal, less false positives)
        private static readonly List<Regex> HardBlockPatterns = new()
        {
            new(@"\b(kys|kill\s+yourself)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bsuicid(e|al)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\b(porn(hub)?|nudes?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bretard(ed)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Soft block: warn/log only (no timeouts)
        private static readonly List<Regex> SoftBlockPatterns = new()
        {
            new(@"\bidiot(ic)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bdumb(ass)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bbastard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bslut\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bhure\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        // Serbian patterns: keep your existing list
        private static readonly List<Regex> SerbianPatterns = new()
        {
            new(@"\bkur(a|ac|cem|cu)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bpick(a|u|e)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bpi(s|ck)(a|u|e)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bjebi(ga|te|em)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bjebem\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\bgovno\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        private static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            return Regex.Replace(
                input.ToLower()
                     .Replace("@", "a")
                     .Replace("4", "a")
                     .Replace("1", "i")
                     .Replace("!", "i")
                     .Replace("0", "o")
                     .Replace("5", "s")
                     .Replace("$", "s")
                     .Replace("3", "e"),
                @"[^a-z\s]",
                ""
            );
        }

        // -------------------------
        // Config load/save
        // -------------------------

        private static Dictionary<ulong, SecurityConfigEntry> LoadSecurityConfig()
        {
            try
            {
                if (!File.Exists(SECURITY_FILE)) return new Dictionary<ulong, SecurityConfigEntry>();
                var txt = File.ReadAllText(SECURITY_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, SecurityConfigEntry>>(txt) ?? new Dictionary<ulong, SecurityConfigEntry>();
                foreach (var entry in d.Values)
                {
                    if (entry.AdminRoleId.HasValue && !entry.AdminRoleIds.Contains(entry.AdminRoleId.Value))
                        entry.AdminRoleIds.Add(entry.AdminRoleId.Value);
                }
                return d;
            }
            catch
            {
                return new Dictionary<ulong, SecurityConfigEntry>();
            }
        }

        private static Dictionary<ulong, CleanupIntervalEntry> LoadCleanupIntervals()
        {
            try
            {
                if (!File.Exists(CLEANUP_INTERVALS_FILE)) return new Dictionary<ulong, CleanupIntervalEntry>();
                var txt = File.ReadAllText(CLEANUP_INTERVALS_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, CleanupIntervalEntry>>(txt);
                return d ?? new Dictionary<ulong, CleanupIntervalEntry>();
            }
            catch
            {
                return new Dictionary<ulong, CleanupIntervalEntry>();
            }
        }

        private static Dictionary<(ulong GuildId, ulong UserId), StrikeEntry> LoadStrikes()
        {
            try
            {
                if (!File.Exists(STRIKES_FILE)) return new Dictionary<(ulong GuildId, ulong UserId), StrikeEntry>();
                var txt = File.ReadAllText(STRIKES_FILE);
                var entries = JsonSerializer.Deserialize<List<SecurityStrikeEntryDto>>(txt);
                if (entries == null) return new Dictionary<(ulong GuildId, ulong UserId), StrikeEntry>();

                var result = new Dictionary<(ulong GuildId, ulong UserId), StrikeEntry>();
                foreach (var entry in entries)
                {
                    result[(entry.GuildId, entry.UserId)] = new StrikeEntry
                    {
                        Strikes = entry.Strikes,
                        LastActionAt = entry.LastActionAt
                    };
                }

                return result;
            }
            catch
            {
                return new Dictionary<(ulong GuildId, ulong UserId), StrikeEntry>();
            }
        }

        private static void SaveStrikes()
        {
            try
            {
                List<SecurityStrikeEntryDto> entries;
                lock (_strikeLock)
                {
                    entries = _strikes.Select(kvp => new SecurityStrikeEntryDto
                    {
                        GuildId = kvp.Key.GuildId,
                        UserId = kvp.Key.UserId,
                        Strikes = kvp.Value.Strikes,
                        LastActionAt = kvp.Value.LastActionAt
                    }).ToList();
                }

                var txt = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(STRIKES_FILE, txt);
            }
            catch { }
        }

        private static Dictionary<ulong, SecurityAppealTicketMeta> LoadSecurityTickets()
        {
            try
            {
                if (!File.Exists(SECURITY_TICKETS_FILE)) return new Dictionary<ulong, SecurityAppealTicketMeta>();
                var txt = File.ReadAllText(SECURITY_TICKETS_FILE);
                var d = JsonSerializer.Deserialize<Dictionary<ulong, SecurityAppealTicketMeta>>(txt);
                return d ?? new Dictionary<ulong, SecurityAppealTicketMeta>();
            }
            catch
            {
                return new Dictionary<ulong, SecurityAppealTicketMeta>();
            }
        }

        private static void SaveSecurityTickets()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_securityTickets, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(SECURITY_TICKETS_FILE, txt);
            }
            catch { }
        }

        private static void SaveSecurityConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(SECURITY_FILE, txt);
            }
            catch { }
        }

        private static void SaveCleanupIntervals()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cleanupIntervals, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(CLEANUP_INTERVALS_FILE, txt);
            }
            catch { }
        }

        public static void SetConfig(ulong guildId, SecurityConfigEntry entry)
        {
            _config[guildId] = entry;
            SaveSecurityConfig();
        }

        public static SecurityConfigEntry GetConfig(ulong guildId)
        {
            return _config.TryGetValue(guildId, out var e) ? e : new SecurityConfigEntry();
        }

        // -------------------------
        // Cleanup interval feature
        // -------------------------

        public static void SetCleanupInterval(ulong guildId, ulong channelId, DiscordSocketClient client, int intervalHours = 1)
        {
            _cleanupIntervals[guildId] = new CleanupIntervalEntry { ChannelId = channelId, IntervalHours = intervalHours };
            SaveCleanupIntervals();

            if (_timers.TryGetValue(guildId, out var existingTimer))
            {
                existingTimer.Stop();
                existingTimer.Dispose();
                _timers.Remove(guildId);
            }

            var timer = new System.Timers.Timer(TimeSpan.FromHours(intervalHours).TotalMilliseconds);
            timer.Elapsed += async (_, __) =>
            {
                try
                {
                    var guild = client.GetGuild(guildId);
                    var channel = guild?.GetTextChannel(channelId);
                    if (channel != null)
                        await PerformScheduledCleanup(channel);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cleanup timer error: {ex.Message}");
                }
            };
            timer.Start();
            _timers[guildId] = timer;
        }

        public static void RemoveCleanupInterval(ulong guildId)
        {
            _cleanupIntervals.Remove(guildId);
            SaveCleanupIntervals();

            if (_timers.TryGetValue(guildId, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                _timers.Remove(guildId);
            }
        }

        public static CleanupIntervalEntry? GetCleanupInterval(ulong guildId)
        {
            return _cleanupIntervals.TryGetValue(guildId, out var entry) ? entry : null;
        }

        internal static SecurityAppealTicketMeta? GetSecurityTicketMeta(ulong channelId)
        {
            return _securityTickets.TryGetValue(channelId, out var meta) ? meta : null;
        }

        public static void RemoveSecurityTicketMeta(ulong channelId)
        {
            if (_securityTickets.Remove(channelId))
                SaveSecurityTickets();
        }

        public static async Task HandleSecurityAppealInteractionAsync(SocketMessageComponent component)
        {
            if (component.GuildId == null)
            {
                await component.RespondAsync("This button can only be used in a server.", ephemeral: true);
                return;
            }

            var parts = component.Data.CustomId.Split('|');
            if (parts.Length < 3)
            {
                await component.RespondAsync("Invalid appeal button.", ephemeral: true);
                return;
            }

            if (!ulong.TryParse(parts[1], out var guildId) || !ulong.TryParse(parts[2], out var targetUserId))
            {
                await component.RespondAsync("Invalid appeal payload.", ephemeral: true);
                return;
            }

            if (component.User.Id != targetUserId)
            {
                await component.RespondAsync("This button is not assigned to your warning. Only the warned/tagged user can open this appeal.", ephemeral: true);
                return;
            }

            var guild = (component.Channel as SocketGuildChannel)?.Guild;
            if (guild == null)
            {
                await component.RespondAsync("Server not found.", ephemeral: true);
                return;
            }

            var cfg = GetConfig(guildId);
            if (!cfg.AdminRoleIds.Any())
            {
                await component.RespondAsync("No admin roles configured in security setup.", ephemeral: true);
                return;
            }

            if (cfg.LogChannelId == null)
            {
                await component.RespondAsync("No security log channel configured.", ephemeral: true);
                return;
            }

            var user = guild.GetUser(targetUserId);
            if (user == null)
            {
                await component.RespondAsync("User not found in this server.", ephemeral: true);
                return;
            }

            var existingTicket = _securityTickets.Values.FirstOrDefault(t =>
                t.GuildId == guildId && t.UserId == targetUserId && guild.GetTextChannel(t.ChannelId) != null);

            if (existingTicket != null)
            {
                await component.RespondAsync($"You already have an open appeal: <#{existingTicket.ChannelId}>", ephemeral: true);
                return;
            }

            var categoryId = cfg.SecurityTicketCategoryId;
            if (categoryId == null || guild.GetCategoryChannel(categoryId.Value) == null)
            {
                var createdCategory = await guild.CreateCategoryChannelAsync("Security Appeals");
                categoryId = createdCategory.Id;
                cfg.SecurityTicketCategoryId = categoryId;
                SetConfig(guildId, cfg);
            }

            var overwrites = new List<Overwrite>
            {
                new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                new Overwrite(user.Id, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow))
            };

            foreach (var adminRoleId in cfg.AdminRoleIds)
            {
                var adminRole = guild.GetRole(adminRoleId);
                if (adminRole != null)
                    overwrites.Add(new Overwrite(adminRole.Id, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow, manageChannel: PermValue.Allow)));
            }

            var ticketChannel = await guild.CreateTextChannelAsync($"security-appeal-{user.Username}".ToLowerInvariant(), props =>
            {
                props.CategoryId = categoryId;
                props.PermissionOverwrites = overwrites;
                props.Topic = $"Security appeal for user {user.Id}";
            });

            var meta = new SecurityAppealTicketMeta
            {
                ChannelId = ticketChannel.Id,
                GuildId = guildId,
                UserId = targetUserId,
                AdminRoleIds = cfg.AdminRoleIds.ToList(),
                Reason = "User appealed a moderation warning"
            };

            _securityTickets[ticketChannel.Id] = meta;
            SaveSecurityTickets();

            var intro = new EmbedBuilder()
                .WithTitle("Security Appeal Ticket")
                .WithDescription("Describe why you think this warning should be reviewed.\nUse `?close-sticket` when finished.")
                .WithColor(new Color(0x5865F2))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            var roleMentions = cfg.AdminRoleIds
                .Select(id => guild.GetRole(id))
                .Where(r => r != null)
                .Select(r => r!.Mention);
            var mention = $"{user.Mention} {string.Join(" ", roleMentions)}".Trim();
            await ticketChannel.SendMessageAsync(mention, embed: intro);

            await component.RespondAsync($"Appeal ticket created: {ticketChannel.Mention}", ephemeral: true);
        }

        public static async Task<(bool success, string message)> CloseSecurityTicketAsync(SocketGuild guild, SocketTextChannel channel, SocketGuildUser closedBy)
        {
            if (!_securityTickets.TryGetValue(channel.Id, out var meta))
                return (false, "This channel is not a security appeal ticket.");

            var cfg = GetConfig(guild.Id);
            var isOwner = meta.UserId == closedBy.Id;
            var hasAdminRole = cfg.AdminRoleIds.Any(id => closedBy.Roles.Any(r => r.Id == id));
            var isAdminPerm = closedBy.GuildPermissions.Administrator;

            if (!isOwner && !hasAdminRole && !isAdminPerm)
                return (false, "You can only close your own appeal ticket or need the configured admin role.");

            var transcript = await BuildChannelTranscriptAsync(channel);
            if (cfg.LogChannelId.HasValue)
            {
                var logChannel = guild.GetTextChannel(cfg.LogChannelId.Value);
                if (logChannel != null)
                {
                    await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(transcript));
                    await logChannel.SendFileAsync(ms, $"security-appeal-{channel.Id}.txt",
                        text: $"Security appeal transcript for {channel.Mention} (closed by {closedBy.Mention})");
                }
            }

            RemoveSecurityTicketMeta(channel.Id);
            await channel.DeleteAsync(new RequestOptions { AuditLogReason = "Security appeal closed" });

            return (true, "Security ticket closed.");
        }

        private static async Task<string> BuildChannelTranscriptAsync(SocketTextChannel channel)
        {
            var allMessages = new List<IMessage>();
            ulong? before = null;

            while (true)
            {
                var batch = before == null
                    ? await channel.GetMessagesAsync(100).FlattenAsync()
                    : await channel.GetMessagesAsync(before.Value, Direction.Before, 100).FlattenAsync();

                var list = batch.OrderBy(m => m.Timestamp).ToList();
                if (list.Count == 0)
                    break;

                allMessages.AddRange(list);
                before = list.MinBy(m => m.Id)?.Id;

                if (list.Count < 100)
                    break;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Channel: #{channel.Name} ({channel.Id})");
            sb.AppendLine($"Guild: {channel.Guild.Name} ({channel.Guild.Id})");
            sb.AppendLine($"Exported: {DateTimeOffset.UtcNow:O}");
            sb.AppendLine(new string('-', 80));

            foreach (var msg in allMessages.OrderBy(m => m.Timestamp))
            {
                var author = msg.Author is SocketGuildUser gu ? $"{gu.DisplayName} ({msg.Author.Id})" : $"{msg.Author.Username} ({msg.Author.Id})";
                var content = string.IsNullOrWhiteSpace(msg.Content) ? "[no text]" : msg.Content;
                sb.AppendLine($"[{msg.Timestamp:yyyy-MM-dd HH:mm:ss}] {author}: {content}");

                if (msg.Attachments.Count > 0)
                {
                    foreach (var att in msg.Attachments)
                        sb.AppendLine($"  attachment: {att.Url}");
                }
            }

            return sb.ToString();
        }

        private static async Task PerformScheduledCleanup(SocketTextChannel channel)
        {
            try
            {
                bool hasMore = true;
                int totalDeleted = 0;

                while (hasMore)
                {
                    var messages = await channel.GetMessagesAsync(100).FlattenAsync();
                    var deleteable = messages.Where(x =>
                        DateTimeOffset.UtcNow - x.Timestamp < TimeSpan.FromDays(14) &&
                        !x.IsPinned).ToList();

                    if (!deleteable.Any())
                    {
                        hasMore = false;
                        break;
                    }

                    if (deleteable.Count == 1)
                    {
                        await deleteable.First().DeleteAsync();
                        totalDeleted += 1;
                        hasMore = false;
                    }
                    else
                    {
                        await channel.DeleteMessagesAsync(deleteable);
                        totalDeleted += deleteable.Count;
                    }

                    await Task.Delay(1000);
                }

                if (totalDeleted > 0)
                {
                    var embed = EmbedFactory.BuildInfo(
                        "Scheduled Cleanup Completed",
                        $"Deleted **{totalDeleted}** messages in {channel.Mention} (excluding pinned messages).");

                    var notification = await channel.SendMessageAsync(embed: embed);

                    _ = Task.Delay(45000).ContinueWith(async _ =>
                    {
                        try { await notification.DeleteAsync(); } catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scheduled cleanup error: {ex.Message}");
            }
        }

        // -------------------------
        // Join suspicious user feature
        // -------------------------

        public static async Task HandleUserJoinedAsync(SocketGuildUser user)
        {
            try
            {
                var cfg = GetConfig(user.Guild.Id);
                if (!cfg.Enabled) return;

                var signals = new List<(string reason, int points)>();

                var accountAge = DateTimeOffset.UtcNow - user.CreatedAt;
                if (accountAge.TotalDays < 3)
                    signals.Add(($"Very new account ({accountAge.TotalDays:F0} days old)", 5));
                else if (accountAge.TotalDays < 10)
                    signals.Add(($"New account ({accountAge.TotalDays:F0} days old)", 3));

                var username = user.Username.ToLowerInvariant();
                var suspiciousUsernamePatterns = new[]
                {
                    @"discord\.gg", @"bit\.ly", @"tinyurl", @"shorturl",
                    @"admin\d+", @"moderator\d+", @"staff\d+",
                    @"[0-9]{8,}",
                    @"^[a-z]{1,3}[0-9]{4,}$",
                    @"free.*nitro", @"nitro.*free", @"giveaway",
                    @"crypto", @"bitcoin", @"trade", @"invest",
                    @"porn", @"sex", @"nude"
                };

                foreach (var pattern in suspiciousUsernamePatterns)
                {
                    if (Regex.IsMatch(username, pattern, RegexOptions.IgnoreCase))
                    {
                        signals.Add(($"Suspicious username pattern: `{pattern}`", 2));
                        break;
                    }
                }

                if (user.GetAvatarUrl(size: 256) == null)
                    signals.Add(("No custom profile picture", 2));

                if (signals.Count == 0) return;

                var score = signals.Sum(s => s.points);
                var reasons = signals.Select(s => $"{s.reason} (+{s.points})").ToList();

                if (cfg.AutoBanSuspiciousUsers && score >= 4)
                {
                    await AutoBanUserAsync(user, reasons, score);
                }
                else
                {
                    await LogSuspiciousUser(user, reasons);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleUserJoined error: {ex.Message}");
            }
        }

        public static async Task HandleImageSpamAsync(SocketGuildUser user, SocketGuild guild)
        {
            try
            {
                var cfg = GetConfig(guild.Id);
                if (!cfg.Enabled || !cfg.AutoBanSuspiciousUsers) return;

                var now = DateTimeOffset.UtcNow;
                lock (_imageLock)
                {
                    if (!_imageSendTimes.ContainsKey(user.Id))
                        _imageSendTimes[user.Id] = new List<DateTimeOffset>();

                    _imageSendTimes[user.Id].Add(now);
                    _imageSendTimes[user.Id].RemoveAll(t => (now - t).TotalSeconds > 30);

                    if (_imageSendTimes[user.Id].Count < 3) return;
                    _imageSendTimes.Remove(user.Id);
                }

                var joinedAgo = now - user.JoinedAt;
                if (joinedAgo?.TotalHours > 1) return;

                var reasons = new List<string>
                {
                    $"Sent 3+ images within 30 seconds of joining (+3)",
                    $"Account age: {(now - user.CreatedAt).TotalDays:F0} days"
                };
                await AutoBanUserAsync(user, reasons, 3);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HandleImageSpam error: {ex.Message}");
            }
        }

        private static async Task AutoBanUserAsync(SocketGuildUser user, List<string> reasons, int score)
        {
            try
            {
                var cfg = GetConfig(user.Guild.Id);
                var auditReason = $"Auto-ban: Suspicious account (score {score}) — " + string.Join("; ", reasons);
                await user.Guild.AddBanAsync(user, 7, auditReason);

                if (!cfg.LogChannelId.HasValue) return;
                var logChannel = user.Guild.GetTextChannel(cfg.LogChannelId.Value);
                if (logChannel == null) return;

                var embed = new EmbedBuilder()
                    .WithTitle("🔨 Auto-Ban: Suspicious Account")
                    .WithColor(new Color(0xED4245))
                    .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                    .WithDescription($"**User:** {user.Mention} (`{user.Id}`)\n**Username:** {user.Username}\n**Account created:** <t:{user.CreatedAt.ToUnixTimeSeconds()}:R>\n**Risk score:** {score}/10")
                    .AddField("Signals detected", string.Join("\n", reasons.Select(r => $"• {r}")), false)
                    .AddField("Action taken", "User permanently banned.\nMessages from the last **7 days** deleted.", false)
                    .WithFooter("Auto-moderation — PiratBot")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await logChannel.SendMessageAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AutoBanUser error: {ex.Message}");
            }
        }

        private static async Task LogSuspiciousUser(SocketGuildUser user, List<string> reasons)
        {
            try
            {
                var cfg = GetConfig(user.Guild.Id);
                if (!cfg.LogChannelId.HasValue) return;

                var logChannel = user.Guild.GetTextChannel(cfg.LogChannelId.Value);
                if (logChannel == null) return;

                var embed = EmbedFactory.BuildSuspiciousJoinLog(user, reasons);
                await logChannel.SendMessageAsync(embed: embed);

                try
                {
                    var logEntry = new
                    {
                        timestamp = DateTimeOffset.UtcNow,
                        type = "suspicious_join",
                        guildId = user.Guild.Id,
                        guildName = user.Guild.Name,
                        userId = user.Id,
                        username = $"{user.Username}#{user.Discriminator}",
                        accountAgeDays = (DateTimeOffset.UtcNow - user.CreatedAt).TotalDays,
                        reasons
                    };
                    WriteToRotatingLogFile("suspicious_users.jsonl", JsonSerializer.Serialize(logEntry));
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LogSuspiciousUser error: {ex.Message}");
            }
        }

        // -------------------------
        // Message moderation
        // -------------------------

        public static async Task HandleMessageAsync(SocketMessage rawMessage)
        {
            try
            {
                if (rawMessage is not SocketUserMessage message) return;
                if (message.Author.IsBot) return;
                if (message.Channel is not SocketTextChannel tchan) return;

                var guild = tchan.Guild;
                if (guild == null) return;

                var cfg = GetConfig(guild.Id);
                
                // DEBUG: Log message for troubleshooting
                Console.WriteLine($"[SECURITY] Guild={guild.Name} ({guild.Id}), User={message.Author.Username}, Enabled={cfg.Enabled}, Content='{message.Content}'");
                
                if (!cfg.Enabled) return;

                // Ignore configured channels
                if (cfg.IgnoreChannelIds.Contains(tchan.Id)) return;

                // Ignore admins (as you already did)
                if (message.Author is SocketGuildUser gUser)
                {
                    if (gUser.GuildPermissions.Administrator) return;
                    if (cfg.IgnoreRoleIds != null && cfg.IgnoreRoleIds.Count > 0)
                    {
                        if (gUser.Roles.Any(r => cfg.IgnoreRoleIds.Contains(r.Id)))
                            return;
                    }
                }

                var rawContent = message.Content ?? string.Empty;
                var normalized = Normalize(rawContent);

                // 0a) CAPS check — delete silently, no DM, no strike
                var letters = rawContent.Where(char.IsLetter).ToArray();
                if (letters.Length > 15 && letters.Count(c => char.IsUpper(c)) / (double)letters.Length > 0.8)
                {
                    try { await message.DeleteAsync(); } catch { }
                    return;
                }

                // 0b) Gibberish — single very long word (no spaces, >35 chars, not a URL)
                var words = rawContent.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 1 && words[0].Length > 35 && !Regex.IsMatch(words[0], @"https?://|www\."))
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Gibberish,
                        Severity = ViolationSeverity.High,
                        Reason = "Gibberish message detected",
                        Matched = "long random string"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                bool containsInvite = InviteRegex.IsMatch(rawContent);
                bool containsLink = containsInvite || LinkRegex.IsMatch(rawContent);

                // 0c) Link spam — same user sends 4+ messages with links within 60 seconds
                if (containsLink && message.Author is SocketGuildUser linkUser)
                {
                    var now = DateTimeOffset.UtcNow;
                    bool isLinkSpam = false;
                    lock (_linkLock)
                    {
                        if (!_linkSendTimes.ContainsKey(linkUser.Id))
                            _linkSendTimes[linkUser.Id] = new List<DateTimeOffset>();
                        _linkSendTimes[linkUser.Id].Add(now);
                        _linkSendTimes[linkUser.Id].RemoveAll(t => (now - t).TotalSeconds > 60);
                        isLinkSpam = _linkSendTimes[linkUser.Id].Count >= 4;
                        if (isLinkSpam) _linkSendTimes.Remove(linkUser.Id);
                    }
                    if (isLinkSpam)
                    {
                        var v = new ViolationResult
                        {
                            Category = ViolationCategory.LinkSpam,
                            Severity = ViolationSeverity.High,
                            Reason = "Repeated link spam (4+ links in 60 seconds)",
                            Matched = "repeated links"
                        };
                        await HandleViolationAsync(message, guild, cfg, v);
                        return;
                    }
                }
                
                Console.WriteLine($"[SECURITY-CHECK] Invite={containsInvite}, Link={containsLink}, BlockInvites={cfg.BlockInvites}, BlockLinks={cfg.BlockLinks}");

                // 1) Invites
                if (cfg.BlockInvites && containsInvite)
                {
                    Console.WriteLine($"[SECURITY-VIOLATION] User {message.Author.Username} posted invite link");
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Invite,
                        Severity = ViolationSeverity.High,
                        Reason = "Invite link detected",
                        Matched = "discord invite"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 2) Spam: single character repetition OR repeated word/phrase pattern
                bool isCharSpam = Regex.IsMatch(rawContent, @"([a-zA-Z0-9])\1{6,}");
                bool isPhraseSpam = Regex.IsMatch(rawContent, @"(.{2,30}?)\1{4,}", RegexOptions.IgnoreCase);
                if (isCharSpam || isPhraseSpam)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Spam,
                        Severity = ViolationSeverity.High,
                        Reason = "Spam pattern detected",
                        Matched = isCharSpam ? "repeated characters" : "repeated phrase/word"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 3) Scam: ONLY if link + keyword
                bool scamKeyword = containsLink && ScamKeywords.Any(r => r.IsMatch(normalized));
                Console.WriteLine($"[SECURITY-CHECK] ScamCheck: Link={containsLink}, HasScamKeyword={scamKeyword}");
                
                if (scamKeyword)
                {
                    Console.WriteLine($"[SECURITY-VIOLATION] User {message.Author.Username} posted potential scam (link + keyword)");
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Scam,
                        Severity = ViolationSeverity.High,
                        Reason = "Potential scam message (keyword + link)",
                        Matched = "scam keyword + link"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                var standaloneScam = StandaloneScamKeywords.FirstOrDefault(r => r.IsMatch(normalized));
                if (standaloneScam != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.Scam,
                        Severity = ViolationSeverity.High,
                        Reason = "Suspicious scam keyword detected",
                        Matched = standaloneScam.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 4) Block generic links (optional, conservative)
                if (cfg.BlockLinks && containsLink && !cfg.AllowedLinkChannelIds.Contains(tchan.Id))
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.GenericLink,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Link posting is restricted in this channel",
                        Matched = "link"
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 5) Hard language
                var hardMatch = HardBlockPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (hardMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.HardLanguage,
                        Severity = ViolationSeverity.Critical,
                        Reason = "Severe language detected",
                        Matched = hardMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 6) Soft language
                var softMatch = SoftBlockPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (softMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.SoftLanguage,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Inappropriate language detected",
                        Matched = softMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 7) Serbian language patterns
                var serbMatch = SerbianPatterns.FirstOrDefault(r => r.IsMatch(normalized));
                if (serbMatch != null)
                {
                    var v = new ViolationResult
                    {
                        Category = ViolationCategory.SerbianLanguage,
                        Severity = ViolationSeverity.Medium,
                        Reason = "Inappropriate language detected",
                        Matched = serbMatch.ToString()
                    };
                    await HandleViolationAsync(message, guild, cfg, v);
                    return;
                }

                // 8) Attachments (filename scan)
                if (message.Attachments != null && message.Attachments.Count > 0)
                {
                    foreach (var att in message.Attachments)
                    {
                        var name = (att.Filename ?? "").ToLowerInvariant();
                        if (Regex.IsMatch(name, "(nude|nudes|porn|dick|boobs|sex|pussy|tits|vagina|penis|clit|anal|nsfw|xxx|18\\+)",
                            RegexOptions.IgnoreCase))
                        {
                            var v = new ViolationResult
                            {
                                Category = ViolationCategory.NsfwAttachment,
                                Severity = ViolationSeverity.High,
                                Reason = "NSFW attachment detected",
                                Matched = name
                            };
                            await HandleViolationAsync(message, guild, cfg, v);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SecurityService error: " + ex);
            }
        }

        private static async Task HandleViolationAsync(SocketUserMessage message, SocketGuild guild, SecurityConfigEntry cfg, ViolationResult v)
        {
            // Decide action flags (calm, Sapphire-ish)
            // Default mapping:
            // - Medium: warn + log, do not delete unless configured by "DeleteOnHighSeverity" (only applies to High+)
            // - High: delete + warn + log
            // - Critical: delete + warn + strike + optional timeout after threshold
            v.ShouldWarn = cfg.WarnUserOnMediumPlus && v.Severity >= ViolationSeverity.Medium;
            v.ShouldDelete = cfg.DeleteOnHighSeverity && v.Severity >= ViolationSeverity.High;
            v.ShouldStrike = v.Severity >= ViolationSeverity.High;

            if (v.Category == ViolationCategory.Invite)
            {
                v.ShouldDelete = true;
                v.ShouldWarn = true;
                v.ShouldStrike = true;
                v.ShouldTimeout = true;
            }

            // MEMORY OPTIMIZATION: Cleanup old strikes periodically
            PerformMemoryCleanupIfNeeded();

            // Cooldown check: prevent spammy bot behavior
            var authorId = message.Author.Id;
            var key = (guild.Id, authorId);
            StrikeEntry entry;

            lock (_strikeLock)
            {
                if (!_strikes.TryGetValue(key, out entry!))
                {
                    // MEMORY CHECK: Don't add new entries if we're at capacity
                    if (_strikes.Count >= MAX_STRIKES_ENTRIES)
                    {
                        // Remove oldest entries (those with oldest LastActionAt)
                        var oldestEntries = _strikes
                            .OrderBy(kvp => kvp.Value.LastActionAt)
                            .Take(_strikes.Count / 4) // Remove 25% of oldest entries
                            .Select(kvp => kvp.Key)
                            .ToList();
                        
                        foreach (var oldKey in oldestEntries)
                            _strikes.Remove(oldKey);
                    }
                    
                    entry = new StrikeEntry();
                    _strikes[key] = entry;
                }

                var cd = TimeSpan.FromSeconds(Math.Max(5, cfg.CooldownSeconds));
                if (DateTimeOffset.UtcNow - entry.LastActionAt < cd)
                {
                    // Still log quietly (optional), but avoid DM / repeated actions
                    v.ShouldWarn = false;
                }

                entry.LastActionAt = DateTimeOffset.UtcNow;

                if (v.ShouldStrike)
                    entry.Strikes += 1;

                SaveStrikes();

                // Timeout only if enabled, severity high+, and strikes threshold reached
                if (cfg.TimeoutEnabled && v.Severity >= ViolationSeverity.High && entry.Strikes >= Math.Max(1, cfg.MaxStrikesBeforeTimeout))
                    v.ShouldTimeout = true;
            }

            string actionTaken = "Log";

            // Delete first (if configured) to prevent further spread
            if (v.ShouldDelete)
            {
                try { await message.DeleteAsync(); actionTaken = "Deleted"; }
                catch { actionTaken = "Attempted delete"; }
            }

            // Warn user via DM
            if (v.ShouldWarn)
            {
                try
                {
                    var dm = EmbedFactory.BuildWarning(
                        "Moderation Notice",
                        $"Your message was flagged in **{guild.Name}**.\n\n" +
                        $"**Reason:** {v.Reason}\n" +
                        $"**Category:** {v.Category}\n\n" +
                        $"Please follow the server rules.");

                    await message.Author.SendMessageAsync(embed: dm);
                    actionTaken = actionTaken == "Deleted" ? "Deleted + Warned" : "Warned";
                }
                catch
                {
                    // DM failed: do nothing (still log to mod channel)
                }
            }

            if (v.ShouldWarn && cfg.WarnChannelId.HasValue)
            {
                try
                {
                    var warnChannel = guild.GetTextChannel(cfg.WarnChannelId.Value);
                    if (warnChannel != null)
                    {
                        var warnEmbed = new EmbedBuilder()
                            .WithTitle("Moderation Notice")
                            .WithDescription(
                                $"User: {message.Author.Mention}\n" +
                                $"Channel: <#{message.Channel.Id}>\n" +
                                $"Reason: {v.Reason}\n" +
                                $"Category: {v.Category}\n" +
                                $"Severity: {v.Severity}")
                            .WithColor(new Color(0xFEE75C))
                            .WithTimestamp(DateTimeOffset.UtcNow)
                            .Build();

                        var button = new ComponentBuilder()
                            .WithButton("Bei Server-admin Einspruch erheben", $"security_appeal|{guild.Id}|{message.Author.Id}", ButtonStyle.Secondary)
                            .Build();

                        await warnChannel.SendMessageAsync(embed: warnEmbed, components: button);
                        actionTaken += " + ServerWarn";
                    }
                }
                catch
                {
                }
            }

            // Timeout (only if enabled and threshold reached)
            if (v.ShouldTimeout && message.Author is SocketGuildUser gUser)
            {
                try
                {
                    var duration = v.Category == ViolationCategory.Invite
                        ? TimeSpan.FromHours(2)
                        : TimeSpan.FromSeconds(Math.Max(60, cfg.TimeoutSeconds));
                    await gUser.SetTimeOutAsync(duration, new RequestOptions { AuditLogReason = "Security system timeout threshold reached" });
                    actionTaken += " + Timeout";
                }
                catch
                {
                    actionTaken += " + Timeout failed";
                }
            }

            // Log channel (embed-only)
            if (cfg.LogChannelId.HasValue)
            {
                try
                {
                    var ch = guild.GetTextChannel(cfg.LogChannelId.Value);
                    if (ch != null)
                    {
                        var embed = EmbedFactory.BuildSecurityLog(guild, message, v, actionTaken);
                        await ch.SendMessageAsync(embed: embed);
                    }
                }
                catch { }
            }

            // Append jsonl log with rotation (MEMORY OPTIMIZED)
            try
            {
                var log = new
                {
                    time = DateTimeOffset.UtcNow,
                    guildId = guild.Id,
                    guildName = guild.Name,
                    userId = message.Author.Id,
                    userTag = message.Author.Username,
                    category = v.Category.ToString(),
                    severity = v.Severity.ToString(),
                    action = actionTaken,
                    matched = v.Matched,
                    content = message.Content
                };
                WriteToRotatingLogFile("security_logs_main.jsonl", JsonSerializer.Serialize(log));
            }
            catch { }
        }

        // =========================
        // MEMORY OPTIMIZATION METHODS
        // =========================
        
        private static void PerformMemoryCleanupIfNeeded()
        {
            // Only run cleanup every 24 hours
            if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromHours(CLEANUP_INTERVAL_HOURS))
                return;

            _lastCleanup = DateTime.UtcNow;
            
            lock (_strikeLock)
            {
                // Remove strikes older than 7 days
                var cutoffDate = DateTimeOffset.UtcNow.AddDays(-7);
                var expiredKeys = _strikes
                    .Where(kvp => kvp.Value.LastActionAt < cutoffDate)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in expiredKeys)
                    _strikes.Remove(key);

                if (expiredKeys.Count > 0)
                    SaveStrikes();
                
                Console.WriteLine($"Security cleanup: Removed {expiredKeys.Count} expired strike entries. Current count: {_strikes.Count}");
            }
        }
        
        private static void WriteToRotatingLogFile(string fileName, string jsonContent)
        {
            try
            {
                var fileInfo = new FileInfo(fileName);
                
                // Rotate log if it exceeds size limit
                if (fileInfo.Exists && fileInfo.Length > MAX_LOG_FILE_SIZE_MB * 1024 * 1024)
                {
                    var backupName = $"{fileName}.{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak";
                    File.Move(fileName, backupName);
                    Console.WriteLine($"Rotated log file {fileName} to {backupName}");
                }
                
                File.AppendAllText(fileName, jsonContent + "\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to rotating log {fileName}: {ex.Message}");
            }
        }
    }

    // =========================
    // Commands (prefix !)
    // =========================

    public class SecurityCommands : ModuleBase<SocketCommandContext>
    {
        private sealed record ModerationContext(string Reason, string? MessageLink, ulong? IgnoredAdminId);

        [Command("setsecuritymod")]
        [Summary("Setup security system (Admin only)")]
        [RequirePirateAdmin]
        public async Task SecuritySetupAsync()
        {
            try
            {
                var ask = EmbedFactory.BuildInfo(
                    "Security Setup",
                    "Provide the **Channel ID** for the security log channel.\n" +
                    "Or type `new-securechan` to create a new log channel (requires a category ID).");

                await ReplyAsync(embed: ask);

                var channelResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (channelResponse == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `?setsecuritymod` again."));
                    return;
                }

                ulong logChannelId;
                ITextChannel logChannel;
                ulong? setupCategoryId = null;

                if (channelResponse.Content.Trim().Equals("new-securechan", StringComparison.OrdinalIgnoreCase))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildInfo("Category Required", "Provide the **Category ID** where the log channel should be created."));

                    var categoryResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                    if (categoryResponse == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `?setsecuritymod` again."));
                        return;
                    }

                    if (!ulong.TryParse(categoryResponse.Content.Trim(), out var categoryId))
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Category ID was invalid."));
                        return;
                    }

                    var category = Context.Guild.GetCategoryChannel(categoryId);
                    if (category == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Category not found in this server."));
                        return;
                    }

                    logChannel = await Context.Guild.CreateTextChannelAsync("security-log", props => props.CategoryId = categoryId);
                    logChannelId = logChannel.Id;
                    setupCategoryId = categoryId;

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                        "Channel Created",
                        $"Created security log channel: {logChannel.Mention}\nCategory: **{category.Name}**"));
                }
                else if (ulong.TryParse(channelResponse.Content.Trim(), out logChannelId))
                {
                    logChannel = Context.Guild.GetTextChannel(logChannelId);
                    if (logChannel == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Channel not found in this server."));
                        return;
                    }

                    setupCategoryId = logChannel is SocketTextChannel sLog && sLog.CategoryId.HasValue
                        ? sLog.CategoryId.Value
                        : null;

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess("Channel Set", $"Security log channel set to {logChannel.Mention}"));
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Provide a valid Channel ID or type `new-securechan`."));
                    return;
                }

                await ReplyAsync(embed: EmbedFactory.BuildInfo(
                    "Warn Channel Setup",
                    "Provide the **Channel ID** for moderation warnings in server.\n" +
                    "Or type `new-warnchan` to create one automatically."));

                var warnResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (warnResponse == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `?setsecuritymod` again."));
                    return;
                }

                ulong warnChannelId;
                ITextChannel warnChannel;

                if (warnResponse.Content.Trim().Equals("new-warnchan", StringComparison.OrdinalIgnoreCase))
                {
                    warnChannel = await Context.Guild.CreateTextChannelAsync("security-warn", props =>
                    {
                        if (setupCategoryId.HasValue)
                            props.CategoryId = setupCategoryId.Value;
                    });
                    warnChannelId = warnChannel.Id;

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                        "Warn Channel Created",
                        $"Created warn channel: {warnChannel.Mention}"));
                }
                else if (ulong.TryParse(warnResponse.Content.Trim(), out warnChannelId))
                {
                    warnChannel = Context.Guild.GetTextChannel(warnChannelId);
                    if (warnChannel == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Warn channel not found in this server."));
                        return;
                    }

                    await ReplyAsync(embed: EmbedFactory.BuildSuccess("Warn Channel Set", $"Warnings will be sent to {warnChannel.Mention}"));
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Provide a valid Channel ID or type `new-warnchan`."));
                    return;
                }

                await ReplyAsync(embed: EmbedFactory.BuildInfo(
                    "Admin Role Setup",
                    "Provide the **Role ID** for the security admin role (required for appeal tickets)."));

                var roleResponse = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (roleResponse == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `?setsecuritymod` again."));
                    return;
                }

                if (!ulong.TryParse(roleResponse.Content.Trim(), out var adminRoleId))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Role ID was invalid."));
                    return;
                }

                var adminRole = Context.Guild.GetRole(adminRoleId);
                if (adminRole == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Not Found", "Role not found in this server."));
                    return;
                }

                var config = new SecurityConfigEntry
                {
                    Enabled = true,
                    LogChannelId = logChannelId,
                    WarnChannelId = warnChannelId,
                    AdminRoleId = adminRoleId,
                    SecurityTicketCategoryId = setupCategoryId,
                    // Defaults already set in model; keep calm behavior
                };

                SecurityService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Security Enabled",
                    $"Security monitoring is now active.\nLog Channel: <#{logChannelId}>\nWarn Channel: <#{warnChannelId}>\nAdmin Role: <@&{adminRoleId}>\n\n" +
                    "Enabled checks:\n" +
                    "- Invite detection\n" +
                    "- Spam detection\n" +
                    "- Scam (link + keyword)\n" +
                    "- Language filters (severity-based)\n" +
                    "- Attachment filename scan\n" +
                    "- Suspicious join logging"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Setup Failed", ex.Message));
            }
        }

        [Command("disable")]
        [Summary("Disable security system (Admin only)")]
        [RequirePirateAdmin]
        public async Task SecurityDisableAsync()
        {
            try
            {
                var cfg = SecurityService.GetConfig(Context.Guild.Id);
                cfg.Enabled = false;
                SecurityService.SetConfig(Context.Guild.Id, cfg);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess("Security Disabled", "Security monitoring is now disabled for this server."));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Disable Failed", ex.Message));
            }
        }

        [Command("status")]
        [Summary("Check security system status (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SecurityStatusAsync()
        {
            try
            {
                var config = SecurityService.GetConfig(Context.Guild.Id);

                var adminRolesText = config.AdminRoleIds.Any()
                    ? string.Join(", ", config.AdminRoleIds.Select(id => $"<@&{id}>"))
                    : "Not set";

                var desc =
                    $"**Enabled:** {(config.Enabled ? "Yes" : "No")}\n" +
                    $"**Log Channel:** {(config.LogChannelId.HasValue ? $"<#{config.LogChannelId.Value}>" : "Not set")}\n" +
                    $"**Warn Channel:** {(config.WarnChannelId.HasValue ? $"<#{config.WarnChannelId.Value}>" : "Not set")}\n" +
                    $"**Admin Roles:** {adminRolesText}\n" +
                    $"**18+ Role:** {(config.Age18RoleId.HasValue ? $"<@&{config.Age18RoleId.Value}>" : "Not set")}\n" +
                    $"**18+ Voice:** {(config.Age18VoiceChannelId.HasValue ? $"<#{config.Age18VoiceChannelId.Value}>" : "Not set")}\n" +
                    $"**Block Invites:** {(config.BlockInvites ? "Yes" : "No")}\n" +
                    $"**Block Links:** {(config.BlockLinks ? "Yes" : "No")}";

                await ReplyAsync(embed: EmbedFactory.BuildInfo("Security Status", desc));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Status Failed", ex.Message));
            }
        }

        [Command("timeouts")]
        [Summary("List all members currently in timeout")]
        [RequirePirateAdmin]
        public async Task ListTimeoutsAsync()
        {
            await Context.Guild.DownloadUsersAsync();

            var now = DateTimeOffset.UtcNow;
            var timedOut = Context.Guild.Users
                .Where(u => u.TimedOutUntil.HasValue && u.TimedOutUntil.Value > now)
                .OrderBy(u => u.TimedOutUntil!.Value)
                .ToList();

            if (!timedOut.Any())
            {
                await ReplyAsync(embed: EmbedFactory.BuildInfo("Active Timeouts", "No members are currently in timeout."));
                return;
            }

            var lines = timedOut.Select(u =>
            {
                var remaining = u.TimedOutUntil!.Value - now;
                var remainingText = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m"
                    : $"{remaining.Minutes}m {remaining.Seconds}s";
                return $"{u.Mention} — expires <t:{u.TimedOutUntil.Value.ToUnixTimeSeconds()}:R> ({remainingText} remaining)";
            });

            var embed = new EmbedBuilder()
                .WithTitle($"Active Timeouts — {timedOut.Count} member{(timedOut.Count == 1 ? "" : "s")}")
                .WithDescription(string.Join("\n", lines))
                .WithColor(new Color(0xFEE75C))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("security-add-admin")]
        [Summary("Add a role to the admin roles list")]
        [RequirePirateAdmin]
        public async Task SecurityAddAdminAsync(SocketRole role)
        {
            var config = SecurityService.GetConfig(Context.Guild.Id);
            if (config.AdminRoleIds.Contains(role.Id))
            {
                await ReplyAsync(embed: EmbedFactory.BuildInfo("Already Configured", $"{role.Mention} is already in the admin roles list."));
                return;
            }
            config.AdminRoleIds.Add(role.Id);
            SecurityService.SetConfig(Context.Guild.Id, config);
            await ReplyAsync(embed: EmbedFactory.BuildInfo("Admin Role Added",
                $"{role.Mention} has been added as an admin role.\n**Total admin roles:** {config.AdminRoleIds.Count}"));
        }

        [Command("security-autoban")]
        [Summary("Enable or disable automatic banning of suspicious new accounts (on/off)")]
        [RequirePirateAdmin]
        public async Task SecurityAutobanAsync(string toggle)
        {
            if (toggle.ToLower() is not ("on" or "off"))
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Option", "Use `?security-autoban on` or `?security-autoban off`."));
                return;
            }

            var config = SecurityService.GetConfig(Context.Guild.Id);
            config.AutoBanSuspiciousUsers = toggle.ToLower() == "on";
            SecurityService.SetConfig(Context.Guild.Id, config);

            await ReplyAsync(embed: EmbedFactory.BuildInfo("Auto-Ban Updated",
                config.AutoBanSuspiciousUsers
                    ? "✅ Auto-ban is now **enabled**.\n\nThe bot will automatically ban accounts with a suspicion score ≥ 4:\n• Account < 10 days old → 3 pts\n• Account < 3 days old → 5 pts\n• No profile picture → 2 pts\n• Suspicious username → 2 pts\n• Image spam (3+ in 30s) → 3 pts\n\nAll messages from the last **7 days** will be deleted on ban."
                    : "❌ Auto-ban is now **disabled**.\nSuspicious users will still be logged, but not banned automatically."));
        }

        [Command("security-autotimeout")]
        [Summary("Enable or disable automatic timeout for repeated violations (on/off)")]
        [RequirePirateAdmin]
        public async Task SecurityAutoTimeoutAsync(string toggle)
        {
            if (toggle.ToLower() is not ("on" or "off"))
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Option", "Use `?security-autotimeout on` or `?security-autotimeout off`."));
                return;
            }

            var config = SecurityService.GetConfig(Context.Guild.Id);
            config.TimeoutEnabled = toggle.ToLower() == "on";
            SecurityService.SetConfig(Context.Guild.Id, config);

            await ReplyAsync(embed: EmbedFactory.BuildInfo("Auto-Timeout Updated",
                config.TimeoutEnabled
                    ? $"✅ Auto-timeout is now **enabled**.\nMembers will be automatically timed out after **{config.MaxStrikesBeforeTimeout} strikes** ({config.TimeoutSeconds}s)."
                    : "❌ Auto-timeout is now **disabled**.\nMembers will only receive warnings for violations."));
        }

        [Command("security-remove-admin")]
        [Summary("Remove a role from the admin roles list")]
        [RequirePirateAdmin]
        public async Task SecurityRemoveAdminAsync(SocketRole role)
        {
            var config = SecurityService.GetConfig(Context.Guild.Id);
            if (!config.AdminRoleIds.Contains(role.Id))
            {
                await ReplyAsync(embed: EmbedFactory.BuildInfo("Not Found", $"{role.Mention} is not in the admin roles list."));
                return;
            }
            config.AdminRoleIds.Remove(role.Id);
            SecurityService.SetConfig(Context.Guild.Id, config);
            await ReplyAsync(embed: EmbedFactory.BuildInfo("Admin Role Removed",
                $"{role.Mention} has been removed.\n**Remaining admin roles:** {config.AdminRoleIds.Count}"));
        }

        [Command("channel18")]
        [Summary("Setup 18+ voice access with a required role")]
        [RequirePirateAdmin]
        [RequireBotPermission(GuildPermission.ManageChannels)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task Channel18Async(ulong roleId)
        {
            try
            {
                var role = Context.Guild.GetRole(roleId);
                if (role == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Role Not Found", "The provided role ID does not exist in this server."));
                    return;
                }

                var config = SecurityService.GetConfig(Context.Guild.Id);
                config.Age18RoleId = roleId;
                SecurityService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync(embed: EmbedFactory.BuildInfo(
                    "18+ Voice Setup",
                    $"Stored 18+ role: <@&{roleId}>\n\n" +
                    "Reply with `have VOICE_CHANNEL_ID` to use an existing voice channel,\n" +
                    "or reply with `create` to create a new 18+ voice channel."));

                var response = await NextMessageAsync(TimeSpan.FromMinutes(1));
                if (response == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Timeout", "Setup timed out. Run `?channel18 ROLE_ID` again."));
                    return;
                }

                IVoiceChannel? targetVoice = null;
                var input = response.Content.Trim();

                if (input.Equals("create", StringComparison.OrdinalIgnoreCase))
                {
                    ulong? categoryId = null;
                    if (Context.Channel is SocketTextChannel currentTextChannel && currentTextChannel.CategoryId.HasValue)
                    {
                        categoryId = currentTextChannel.CategoryId.Value;
                    }

                    targetVoice = await Context.Guild.CreateVoiceChannelAsync("🔞 18+ Voice", props =>
                    {
                        if (categoryId.HasValue)
                        {
                            props.CategoryId = categoryId.Value;
                        }
                    });
                }
                else if (input.StartsWith("have ", StringComparison.OrdinalIgnoreCase))
                {
                    var rawChannelId = input.Substring(5).Trim();
                    if (rawChannelId.StartsWith("<#") && rawChannelId.EndsWith(">"))
                    {
                        rawChannelId = rawChannelId.Trim('<', '#', '>');
                    }

                    if (!ulong.TryParse(rawChannelId, out var voiceChannelId))
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Provide `have VOICE_CHANNEL_ID` or `create`."));
                        return;
                    }

                    targetVoice = Context.Guild.GetVoiceChannel(voiceChannelId);
                    if (targetVoice == null)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Channel Not Found", "Voice channel not found in this server."));
                        return;
                    }
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Input", "Provide `have VOICE_CHANNEL_ID` or `create`."));
                    return;
                }

                await targetVoice.AddPermissionOverwriteAsync(
                    Context.Guild.EveryoneRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Deny));

                await targetVoice.AddPermissionOverwriteAsync(
                    role,
                    new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow, speak: PermValue.Allow));

                await targetVoice.AddPermissionOverwriteAsync(
                    Context.Guild.CurrentUser,
                    new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow, speak: PermValue.Allow, manageChannel: PermValue.Allow));

                config.Age18VoiceChannelId = targetVoice.Id;
                SecurityService.SetConfig(Context.Guild.Id, config);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "18+ Voice Ready",
                    $"Role: <@&{roleId}>\nVoice: <#{targetVoice.Id}>\n\n" +
                    "Rules set:\n- Channel is visible for everyone\n- Only members with the 18+ role can connect"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("18+ Setup Failed", ex.Message));
            }
        }

        [Command("security-filters")]
        [Alias("security-rules", "sfilters")]
        [Summary("List all security categories, triggers and actions")]
        [RequirePirateAdmin]
        public async Task SecurityFiltersAsync()
        {
            var config = SecurityService.GetConfig(Context.Guild.Id);

            var embed = new EmbedBuilder()
                .WithTitle("Security Filters & Consequences")
                .WithColor(new Color(0x2F3136))
                .WithDescription(
                    "**Category: Invite (High)**\n" +
                    "Trigger: discord invite links\n" +
                    "Action: delete + warn + strike\n\n" +
                    "**Category: Spam (Medium)**\n" +
                    "Trigger: repeated character patterns\n" +
                    "Action: warn (and optionally delete if escalated)\n\n" +
                    "**Category: Scam (High)**\n" +
                    "Trigger: link + scam keywords\n" +
                    "Action: delete + warn + strike\n\n" +
                    "**Category: GenericLink (Medium)**\n" +
                    "Trigger: links when `security-toggle-links` enabled\n" +
                    "Action: warn\n\n" +
                    "**Category: HardLanguage (Critical)**\n" +
                    "Trigger: severe blocked words\n" +
                    "Action: delete + warn + strike (+ timeout if enabled)\n\n" +
                    "**Category: SoftLanguage / SerbianLanguage (Medium)**\n" +
                    "Trigger: moderated language lists\n" +
                    "Action: warn\n\n" +
                    "**Category: NsfwAttachment (High)**\n" +
                    "Trigger: suspicious attachment names\n" +
                    "Action: delete + warn + strike")
                .AddField("Current Server Settings",
                    $"Warn Channel: {(config.WarnChannelId.HasValue ? $"<#{config.WarnChannelId.Value}>" : "Not set")}\n" +
                    $"Log Channel: {(config.LogChannelId.HasValue ? $"<#{config.LogChannelId.Value}>" : "Not set")}\n" +
                    $"Admin Role: {(config.AdminRoleId.HasValue ? $"<@&{config.AdminRoleId.Value}>" : "Not set")}\n" +
                    $"Timeout: {(config.TimeoutEnabled ? "Enabled" : "Disabled")} ({config.TimeoutSeconds}s after {config.MaxStrikesBeforeTimeout} strikes)")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("close-sticket")]
        [Summary("Close current security appeal ticket and export transcript to security-log")]
        public async Task CloseSecurityTicketAsync()
        {
            if (Context.Channel is not SocketTextChannel textChannel)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Channel", "This command can only be used in a server text channel."));
                return;
            }

            if (Context.User is not SocketGuildUser guildUser)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Invalid User", "Could not resolve user context."));
                return;
            }

            var result = await SecurityService.CloseSecurityTicketAsync(Context.Guild, textChannel, guildUser);
            if (!result.success)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Close Failed", result.message));
                return;
            }
        }

        [Command("send-discussion")]
        [Summary("Move a discussion range into a private ticket channel and clean the source channel")]
        [RequireBotPermission(GuildPermission.ManageChannels)]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public async Task SendDiscussionAsync(ulong startMessageId, ulong endMessageId)
        {
            try
            {
                if (Context.Channel is not SocketTextChannel sourceChannel)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Channel", "This command can only be used in a server text channel."));
                    return;
                }

                var securityConfig = SecurityService.GetConfig(Context.Guild.Id);
                var configuredAdminRoleIds = securityConfig.AdminRoleIds?.ToHashSet() ?? new HashSet<ulong>();
                if (!configuredAdminRoleIds.Any() && securityConfig.AdminRoleId.HasValue)
                {
                    configuredAdminRoleIds.Add(securityConfig.AdminRoleId.Value);
                }

                if (!configuredAdminRoleIds.Any())
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Missing Security Setup", "No admin roles configured. Run `?setsecuritymod` first."));
                    return;
                }

                if (Context.User is not SocketGuildUser commandUser || !commandUser.Roles.Any(r => configuredAdminRoleIds.Contains(r.Id)))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Access Denied", "Only configured security admin roles can use this command."));
                    return;
                }

                if (startMessageId > endMessageId)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Range", "The first message ID must be older than or equal to the second message ID."));
                    return;
                }

                var ticketConfig = PirateService.GetTicketConfig(Context.Guild.Id);
                if (!ticketConfig.TicketCategoryId.HasValue)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Missing Ticket Setup", "Ticket category is not configured. Run the pirate ticket setup first."));
                    return;
                }

                var category = Context.Guild.GetCategoryChannel(ticketConfig.TicketCategoryId.Value);
                if (category == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Category Not Found", "Configured ticket category no longer exists. Run the pirate ticket setup again."));
                    return;
                }

                var inRangeMessages = await FetchMessagesInRangeAsync(sourceChannel, startMessageId, endMessageId);
                if (inRangeMessages.Count == 0)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("No Messages Found", "No messages were found in the provided range in this channel."));
                    return;
                }

                var participantIds = inRangeMessages
                    .Select(m => m.Author.Id)
                    .Distinct()
                    .ToHashSet();

                var roleIdsForAccess = new HashSet<ulong>(configuredAdminRoleIds);
                if (ticketConfig.SupportRoleId.HasValue)
                {
                    roleIdsForAccess.Add(ticketConfig.SupportRoleId.Value);
                }

                if (ticketConfig.SupportRoleIds != null)
                {
                    foreach (var roleId in ticketConfig.SupportRoleIds)
                    {
                        roleIdsForAccess.Add(roleId);
                    }
                }

                var random = new Random();
                var ticketName = $"discussion-ticket{random.Next(1000, 9999)}";

                var overwrites = new List<Overwrite>
                {
                    new Overwrite(Context.Guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny))
                };

                foreach (var roleId in roleIdsForAccess)
                {
                    var role = Context.Guild.GetRole(roleId);
                    if (role == null)
                    {
                        continue;
                    }

                    overwrites.Add(new Overwrite(role.Id, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));
                }

                foreach (var userId in participantIds)
                {
                    var user = Context.Guild.GetUser(userId);
                    if (user == null)
                    {
                        continue;
                    }

                    overwrites.Add(new Overwrite(user.Id, PermissionTarget.User,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow)));
                }

                var ticketChannel = await Context.Guild.CreateTextChannelAsync(ticketName, props =>
                {
                    props.CategoryId = category.Id;
                    props.PermissionOverwrites = overwrites;
                    props.Topic = $"Discussion export from #{sourceChannel.Name} ({startMessageId} - {endMessageId})";
                });

                int postedItems = 0;

                foreach (var message in inRangeMessages)
                {
                    var discussionEmbed = BuildDiscussionEmbed(Context.Guild, message);
                    await ticketChannel.SendMessageAsync(embed: discussionEmbed);
                    postedItems++;

                    var reactionSummary = await BuildReactionSummaryAsync(message);
                    if (!string.IsNullOrWhiteSpace(reactionSummary))
                    {
                        await SendChunkedMessageAsync(ticketChannel, reactionSummary);
                    }
                }

                int deletedCount = 0;
                foreach (var message in inRangeMessages)
                {
                    try
                    {
                        await message.DeleteAsync();
                        deletedCount++;
                    }
                    catch
                    {
                    }
                }

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Discussion Moved",
                    $"Created {ticketChannel.Mention}\nMessages exported: **{postedItems}**\nOriginal messages deleted: **{deletedCount}**"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Send Discussion Failed", ex.Message));
            }
        }

        // -------------------------
        // Cleanup commands (embed-only)
        // -------------------------

        [Command("cleanup-now")]
        [Summary("Clean all messages in current channel")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public async Task CleanupNowAsync()
        {
            try
            {
                int totalDeleted = 0;

                var allMessages = new List<IMessage>();
                ulong? beforeId = null;

                while (true)
                {
                    var messages = beforeId.HasValue
                        ? await Context.Channel.GetMessagesAsync(beforeId.Value, Direction.Before, 100).FlattenAsync()
                        : await Context.Channel.GetMessagesAsync(100).FlattenAsync();

                    var batch = messages.ToList();
                    if (!batch.Any())
                    {
                        break;
                    }

                    allMessages.AddRange(batch.Where(x => !x.IsPinned && x.Id != Context.Message.Id));

                    if (batch.Count < 100)
                    {
                        break;
                    }

                    beforeId = batch.Min(x => x.Id);
                    if (!beforeId.HasValue || beforeId.Value == 0)
                    {
                        break;
                    }
                }

                var recent = allMessages
                    .Where(x => DateTimeOffset.UtcNow - x.Timestamp < TimeSpan.FromDays(14))
                    .ToList();

                var old = allMessages
                    .Where(x => DateTimeOffset.UtcNow - x.Timestamp >= TimeSpan.FromDays(14))
                    .ToList();

                var textChannel = Context.Channel as ITextChannel;
                if (textChannel != null)
                {
                    foreach (var chunk in recent.Chunk(100))
                    {
                        var chunkList = chunk.ToList();
                        if (chunkList.Count == 0)
                        {
                            continue;
                        }

                        try
                        {
                            if (chunkList.Count > 1)
                            {
                                await textChannel.DeleteMessagesAsync(chunkList);
                                totalDeleted += chunkList.Count;
                            }
                            else
                            {
                                await chunkList[0].DeleteAsync();
                                totalDeleted++;
                            }
                        }
                        catch
                        {
                            foreach (var msg in chunkList)
                            {
                                try { await msg.DeleteAsync(); totalDeleted++; } catch { }
                            }
                        }
                    }
                }

                foreach (var msg in old)
                {
                    try { await msg.DeleteAsync(); totalDeleted++; } catch { }
                }

                try { await Context.Message.DeleteAsync(); totalDeleted++; } catch { }

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Complete",
                    $"Deleted **{totalDeleted}** messages in {Context.Channel.Name}."));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Cleanup Failed", ex.Message));
            }
        }

        [Command("cleanup-intervall")]
        [Alias("setcleanupinterval")]
        [Summary("Set automatic cleanup interval for a channel")]
        public async Task SetCleanupIntervalAsync(ulong channelId, string interval = "1h")
        {
            try
            {
                var channel = Context.Guild.GetTextChannel(channelId);
                if (channel == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Channel Not Found", "Provide a valid channel ID."));
                    return;
                }

                int hours = 1;
                string display = "1 hour";

                if (interval.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                {
                    var hoursStr = interval.Substring(0, interval.Length - 1);
                    if (!int.TryParse(hoursStr, out hours) || hours < 1 || hours > 720)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Interval", "Use format like `10h` (1-720 hours)."));
                        return;
                    }
                    display = $"{hours} hour{(hours > 1 ? "s" : "")}";
                }
                else if (interval.EndsWith("d", StringComparison.OrdinalIgnoreCase))
                {
                    var daysStr = interval.Substring(0, interval.Length - 1);
                    if (!int.TryParse(daysStr, out int days) || days < 1 || days > 30)
                    {
                        await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Interval", "Use format like `5d` (1-30 days)."));
                        return;
                    }
                    hours = days * 24;
                    display = $"{days} day{(days > 1 ? "s" : "")}";
                }
                else
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Format", "Use `10h` or `5d`. Example: `?cleanup-intervall CHANNEL_ID 5d`."));
                    return;
                }

                SecurityService.SetCleanupInterval(Context.Guild.Id, channelId, Context.Client as DiscordSocketClient, hours);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Interval Set",
                    $"Channel: <#{channelId}>\nInterval: **{display}**\nStatus: Active"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        [Command("delcleanup-intervall")]
        [Alias("cleanupdel", "removecleanupinterval")]
        [Summary("Remove automatic cleanup interval for a channel")]
        public async Task RemoveCleanupIntervalAsync(ulong channelId)
        {
            try
            {
                var current = SecurityService.GetCleanupInterval(Context.Guild.Id);
                if (current == null)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Not Configured", "No cleanup interval is set for this server."));
                    return;
                }

                if (current.ChannelId != channelId)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Mismatch", $"Cleanup interval is set for <#{current.ChannelId}>, not <#{channelId}>."));
                    return;
                }

                SecurityService.RemoveCleanupInterval(Context.Guild.Id);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Cleanup Interval Removed",
                    $"Automatic cleanup disabled for <#{channelId}>."));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        // -------------------------
        // Dangerous command (hardened)
        // -------------------------

        [Command("give-love")]
        [Summary("Delete ALL channels and categories (Owner only, requires confirmation phrase).")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [RequireBotPermission(GuildPermission.ManageChannels)]
        public async Task CleanupServerUltraAsync()
        {
            try
            {
                if (Context.Guild.OwnerId != Context.User.Id)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Forbidden", "Only the server owner can run this command."));
                    return;
                }

                var warning = EmbedFactory.BuildWarning(
                    "Destructive Action",
                    "This will delete **all channels and categories**.\n\n" +
                    "To confirm, type exactly:\n" +
                    "`CONFIRM DELETE ALL CHANNELS`\n\n" +
                    "You have 30 seconds.");

                await ReplyAsync(embed: warning);

                var confirmMsg = await NextMessageAsync(TimeSpan.FromSeconds(30));
                if (confirmMsg == null || !confirmMsg.Content.Trim().Equals("CONFIRM DELETE ALL CHANNELS", StringComparison.Ordinal))
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Cancelled", "Confirmation phrase not received. No changes were made."));
                    return;
                }

                await ReplyAsync(embed: EmbedFactory.BuildInfo("Cleanup Started", "Deleting all channels and categories."));

                int deletedChannels = 0;
                int deletedCategories = 0;
                int failed = 0;

                var allChannels = Context.Guild.Channels.ToList();

                foreach (var channel in allChannels.Where(c => c is not SocketCategoryChannel))
                {
                    try { await channel.DeleteAsync(); deletedChannels++; await Task.Delay(500); }
                    catch { failed++; }
                }

                foreach (var channel in allChannels.Where(c => c is SocketCategoryChannel))
                {
                    try { await channel.DeleteAsync(); deletedCategories++; await Task.Delay(500); }
                    catch { failed++; }
                }

                var done = EmbedFactory.BuildSuccess(
                    "Cleanup Complete",
                    $"Deleted Channels: {deletedChannels}\nDeleted Categories: {deletedCategories}\nFailed: {failed}");

                try { await Context.User.SendMessageAsync(embed: done); } catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Error", ex.Message));
            }
        }

        // -------------------------
        // Link/Invite blocking
        // -------------------------

        [Command("security-toggle-links")]
        [Alias("togglelinks")]
        [Summary("Toggle whether links are blocked (Admin only)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ToggleBlockLinksAsync(bool enabled = true)
        {
            try
            {
                var cfg = SecurityService.GetConfig(Context.Guild.Id);
                if (!cfg.Enabled)
                {
                    await ReplyAsync(embed: EmbedFactory.BuildError("Not Enabled", "Security system is not enabled. Run `?setsecuritymod` first."));
                    return;
                }

                cfg.BlockLinks = enabled;
                SecurityService.SetConfig(Context.Guild.Id, cfg);

                await ReplyAsync(embed: EmbedFactory.BuildSuccess(
                    "Link Blocking Updated",
                    $"Link blocking is now **{(enabled ? "ENABLED" : "DISABLED")}**\n\n" +
                    $"This will flag messages with links + suspicious keywords (nudes, porn, free nitro, etc.)"));
            }
            catch (Exception ex)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Failed", ex.Message));
            }
        }

        [Command("kick")]
        [Summary("Kick a member and DM them the reason")]
        [RequirePirateAdmin]
        [RequireUserPermission(GuildPermission.KickMembers)]
        [RequireBotPermission(GuildPermission.KickMembers)]
        public async Task KickAsync(ulong targetUserId, [Remainder] string? details = null)
        {
            await ExecuteModerationAsync("KICK", targetUserId, details, async user =>
            {
                var reason = BuildAuditReason("KICK", details);
                await user.KickAsync(reason, new RequestOptions { AuditLogReason = reason });
            });
        }

        private static Embed BuildDiscussionEmbed(SocketGuild guild, IMessage message)
        {
            var guildUser = message.Author as SocketGuildUser ?? guild.GetUser(message.Author.Id);
            var avatar = message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl();
            var highestRole = guildUser?.Roles
                .Where(r => r.Id != guild.EveryoneRole.Id)
                .OrderByDescending(r => r.Position)
                .FirstOrDefault();

            var highestRoleText = highestRole?.Name ?? "No role";
            var authorName = guildUser?.DisplayName ?? message.Author.Username;

            var content = string.IsNullOrWhiteSpace(message.Content) ? "[no text]" : message.Content;
            if (content.Length > 3900)
            {
                content = content.Substring(0, 3900) + "...";
            }

            if (message.Attachments.Count > 0)
            {
                var attachmentLines = string.Join("\n", message.Attachments.Select(a => a.Url));
                content = content + "\n\nAttachments:\n" + attachmentLines;
                if (content.Length > 3900)
                {
                    content = content.Substring(0, 3900) + "...";
                }
            }

            var sentAtLocal = message.Timestamp.ToLocalTime().ToString("hh:mm tt", CultureInfo.InvariantCulture);
            var userColor = GetDiscussionColorForUser(message.Author.Id);

            return new EmbedBuilder()
                .WithAuthor($"{authorName} | {highestRoleText}", avatar)
                .WithDescription(content)
                .WithColor(userColor)
                .WithFooter($"Original time: {sentAtLocal}")
                .Build();
        }

        private static Color GetDiscussionColorForUser(ulong userId)
        {
            var seed = unchecked((int)(userId ^ (userId >> 32)));
            var random = new Random(seed);

            var hue = random.NextDouble() * 360.0;
            var saturation = 0.55 + (random.NextDouble() * 0.25);
            var value = 0.70 + (random.NextDouble() * 0.20);

            return ColorFromHsv(hue, saturation, value);
        }

        private static Color ColorFromHsv(double hue, double saturation, double value)
        {
            var chroma = value * saturation;
            var hueSection = hue / 60.0;
            var x = chroma * (1 - Math.Abs((hueSection % 2) - 1));
            var m = value - chroma;

            double rPrime;
            double gPrime;
            double bPrime;

            if (hueSection < 1)
            {
                rPrime = chroma;
                gPrime = x;
                bPrime = 0;
            }
            else if (hueSection < 2)
            {
                rPrime = x;
                gPrime = chroma;
                bPrime = 0;
            }
            else if (hueSection < 3)
            {
                rPrime = 0;
                gPrime = chroma;
                bPrime = x;
            }
            else if (hueSection < 4)
            {
                rPrime = 0;
                gPrime = x;
                bPrime = chroma;
            }
            else if (hueSection < 5)
            {
                rPrime = x;
                gPrime = 0;
                bPrime = chroma;
            }
            else
            {
                rPrime = chroma;
                gPrime = 0;
                bPrime = x;
            }

            var r = (byte)Math.Round((rPrime + m) * 255);
            var g = (byte)Math.Round((gPrime + m) * 255);
            var b = (byte)Math.Round((bPrime + m) * 255);

            return new Color(r, g, b);
        }

        private static async Task<string> BuildReactionSummaryAsync(IMessage message)
        {
            if (message is not IUserMessage userMessage || message.Reactions == null || message.Reactions.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();

            foreach (var reaction in message.Reactions)
            {
                List<IUser> users;
                try
                {
                    users = (await userMessage.GetReactionUsersAsync(reaction.Key, 100).FlattenAsync()).ToList();
                }
                catch
                {
                    continue;
                }

                foreach (var user in users.Where(u => !u.IsBot))
                {
                    lines.Add($"*{user.Username} reacted with {reaction.Key} on the message.*");
                }
            }

            return string.Join("\n", lines.Distinct());
        }

        private static async Task SendChunkedMessageAsync(IMessageChannel channel, string content)
        {
            const int limit = 1900;
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (content.Length <= limit)
            {
                await channel.SendMessageAsync(content);
                return;
            }

            var lines = content.Split('\n');
            var buffer = new StringBuilder();

            foreach (var line in lines)
            {
                var candidate = buffer.Length == 0 ? line : buffer + "\n" + line;
                if (candidate.Length > limit)
                {
                    await channel.SendMessageAsync(buffer.ToString());
                    buffer.Clear();
                    buffer.Append(line);
                }
                else
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Append('\n');
                    }
                    buffer.Append(line);
                }
            }

            if (buffer.Length > 0)
            {
                await channel.SendMessageAsync(buffer.ToString());
            }
        }

        private static async Task<List<IMessage>> FetchMessagesInRangeAsync(SocketTextChannel channel, ulong startMessageId, ulong endMessageId)
        {
            var collected = new List<IMessage>();
            ulong? beforeId = null;
            bool reachedStart = false;

            while (!reachedStart)
            {
                var batch = beforeId == null
                    ? await channel.GetMessagesAsync(100).FlattenAsync()
                    : await channel.GetMessagesAsync(beforeId.Value, Direction.Before, 100).FlattenAsync();

                var ordered = batch
                    .OrderByDescending(m => m.Id)
                    .ToList();

                if (ordered.Count == 0)
                {
                    break;
                }

                foreach (var message in ordered)
                {
                    if (message.Id > endMessageId)
                    {
                        continue;
                    }

                    if (message.Id < startMessageId)
                    {
                        reachedStart = true;
                        break;
                    }

                    collected.Add(message);

                    if (message.Id == startMessageId)
                    {
                        reachedStart = true;
                        break;
                    }
                }

                beforeId = ordered.Min(m => m.Id);
                if (ordered.Count < 100)
                {
                    break;
                }
            }

            return collected
                .Where(m => m.Id >= startMessageId && m.Id <= endMessageId)
                .OrderBy(m => m.Id)
                .ToList();
        }

        [Command("ban")]
        [Summary("Ban a member and DM them the reason")]
        [RequirePirateAdmin]
        [RequireUserPermission(GuildPermission.BanMembers)]
        [RequireBotPermission(GuildPermission.BanMembers)]
        public async Task BanAsync(ulong targetUserId, [Remainder] string? details = null)
        {
            await ExecuteModerationAsync("BAN", targetUserId, details, async user =>
            {
                var reason = BuildAuditReason("BAN", details);
                await user.BanAsync(0, reason, new RequestOptions { AuditLogReason = reason });
            });
        }

        [Command("timeout")]
        [Alias("mute")]
        [Summary("Timeout a member and DM them the reason")]
        [RequirePirateAdmin]
        [RequireUserPermission(GuildPermission.ModerateMembers)]
        [RequireBotPermission(GuildPermission.ModerateMembers)]
        public async Task TimeoutAsync(ulong targetUserId, int minutes, [Remainder] string? details = null)
        {
            if (minutes < 1 || minutes > 40320)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("Invalid Timeout", "Use a duration between `1` and `40320` minutes."));
                return;
            }

            await ExecuteModerationAsync("TIMEOUT", targetUserId, details, async user =>
            {
                await user.SetTimeOutAsync(TimeSpan.FromMinutes(minutes), new RequestOptions { AuditLogReason = BuildAuditReason("TIMEOUT", details) });
            }, minutes);
        }

        private async Task ExecuteModerationAsync(string actionTitle, ulong targetUserId, string? details, Func<IGuildUser, Task> moderationAction, int? timeoutMinutes = null)
        {
            IGuildUser? targetUser = Context.Guild.GetUser(targetUserId)
                ?? await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, targetUserId) as IGuildUser;

            if (targetUser == null)
            {
                await ReplyAsync(embed: EmbedFactory.BuildError("User Not Found", "I could not find that user in this server."));
                return;
            }

            var parsed = ParseModerationContext(details);
            var resolvedReason = string.IsNullOrWhiteSpace(parsed.Reason) ? "No reason provided" : parsed.Reason;

            string? linkedMessageText = null;
            string? linkedMessageUrl = null;

            if (!string.IsNullOrWhiteSpace(parsed.MessageLink))
            {
                var messageContext = await TryFetchMessageContextAsync(Context.Guild, parsed.MessageLink);
                linkedMessageText = messageContext.content;
                linkedMessageUrl = messageContext.url;
            }

            try
            {
                await SendModerationDmAsync(targetUser, actionTitle, resolvedReason, linkedMessageText, linkedMessageUrl, timeoutMinutes);
            }
            catch
            {
                // DM failures must never block the moderation action.
            }

            await moderationAction(targetUser);

            var confirmation = new EmbedBuilder()
                .WithTitle(actionTitle)
                .WithColor(actionTitle == "BAN" ? new Color(0xED4245) : actionTitle == "TIMEOUT" ? new Color(0xFEE75C) : new Color(0xF28B82))
                .WithDescription($"**Target:** {targetUser.Mention}\n**Reason:** {resolvedReason}")
                .AddField("Executed By", $"{Context.User.Mention}\n`{Context.User.Id}`", true)
                .AddField("When", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>", true)
                .WithFooter($"Action by {Context.User.Username}", Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl())
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (timeoutMinutes.HasValue)
            {
                confirmation.AddField("Timeout Duration", $"{timeoutMinutes.Value} minute{(timeoutMinutes.Value == 1 ? string.Empty : "s")}", true);
            }

            if (!string.IsNullOrWhiteSpace(linkedMessageText))
            {
                confirmation.AddField("Linked Message", BuildLinkedMessageField(linkedMessageText, linkedMessageUrl), false);
            }

            await ReplyAsync(embed: confirmation.Build());
        }

        private static string BuildAuditReason(string actionTitle, string? details)
        {
            var parsed = ParseModerationContext(details);
            var reason = string.IsNullOrWhiteSpace(parsed.Reason) ? "No reason provided" : parsed.Reason;
            return $"{actionTitle}: {reason}";
        }

        private static ModerationContext ParseModerationContext(string? details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return new ModerationContext(string.Empty, null, null);
            }

            var tokens = details.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            string? messageLink = null;
            ulong? ignoredAdminId = null;

            if (tokens.Count > 0 && LooksLikeDiscordMessageLink(tokens[0]))
            {
                messageLink = tokens[0];
                tokens.RemoveAt(0);
            }

            if (tokens.Count > 1 && ulong.TryParse(tokens[^1], out var adminId))
            {
                ignoredAdminId = adminId;
                tokens.RemoveAt(tokens.Count - 1);
            }

            return new ModerationContext(string.Join(' ', tokens).Trim(), messageLink, ignoredAdminId);
        }

        private static bool LooksLikeDiscordMessageLink(string value)
        {
            return value.Contains("discord.com/channels/", StringComparison.OrdinalIgnoreCase)
                || value.Contains("discordapp.com/channels/", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<(string? content, string? url)> TryFetchMessageContextAsync(SocketGuild guild, string messageLink)
        {
            if (!TryParseMessageLink(messageLink, out var guildId, out var channelId, out var messageId) || guildId != guild.Id)
            {
                return (null, messageLink);
            }

            var channel = guild.GetTextChannel(channelId);
            if (channel == null)
            {
                return (null, messageLink);
            }

            var message = await channel.GetMessageAsync(messageId);
            if (message == null)
            {
                return (null, messageLink);
            }

            var content = message.Content;
            if (string.IsNullOrWhiteSpace(content) && message.Attachments.Count > 0)
            {
                content = $"Message contained {message.Attachments.Count} attachment(s).";
            }

            return (content, messageLink);
        }

        private async Task SendModerationDmAsync(IGuildUser targetUser, string actionTitle, string reason, string? linkedMessageText, string? linkedMessageUrl, int? timeoutMinutes)
        {
            var moderatorAvatar = Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl();
            var targetAvatar = targetUser.GetAvatarUrl() ?? targetUser.GetDefaultAvatarUrl();

            var dmEmbed = new EmbedBuilder()
                .WithTitle(actionTitle)
                .WithColor(actionTitle == "BAN" ? new Color(0xED4245) : actionTitle == "TIMEOUT" ? new Color(0xFEE75C) : new Color(0xF28B82))
                .WithAuthor(targetUser.Username, targetAvatar)
                .WithDescription($"**What happened:** {GetActionText(actionTitle)}\n**Reason:** {reason}")
                .AddField("Issued By", $"{Context.User.Username}\n`{Context.User.Id}`", true)
                .AddField("Date & Time", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:F>", true)
                .WithFooter($"Action by {Context.User.Username}", moderatorAvatar)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (timeoutMinutes.HasValue)
            {
                dmEmbed.AddField("Duration", $"{timeoutMinutes.Value} minute{(timeoutMinutes.Value == 1 ? string.Empty : "s")}", true);
            }

            if (!string.IsNullOrWhiteSpace(linkedMessageText))
            {
                dmEmbed.AddField("Linked Message", BuildLinkedMessageField(linkedMessageText, linkedMessageUrl), false);
            }

            await targetUser.SendMessageAsync(embed: dmEmbed.Build());
        }

        private static string GetActionText(string actionTitle)
        {
            return actionTitle switch
            {
                "BAN" => "You have been banned from the server.",
                "KICK" => "You have been kicked from the server.",
                "TIMEOUT" => "You have been placed in timeout.",
                _ => "A moderation action has been applied."
            };
        }

        private static string BuildLinkedMessageField(string messageText, string? messageUrl)
        {
            var snippet = SafeSnippet(messageText, 800);
            if (string.IsNullOrWhiteSpace(messageUrl))
            {
                return $"```text\n{snippet}\n```";
            }

            return $"[Open linked message]({messageUrl})\n```text\n{snippet}\n```";
        }

        private static string SafeSnippet(string input, int max)
        {
            if (string.IsNullOrWhiteSpace(input)) return "—";
            input = input.Replace("`", "'");
            return input.Length <= max ? input : input.Substring(0, max) + "...";
        }

        private static bool TryParseMessageLink(string messageLink, out ulong guildId, out ulong channelId, out ulong messageId)
        {
            guildId = 0;
            channelId = 0;
            messageId = 0;

            var normalized = messageLink.Trim().Trim('<', '>');
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                return false;
            }

            return ulong.TryParse(segments[^3], out guildId)
                && ulong.TryParse(segments[^2], out channelId)
                && ulong.TryParse(segments[^1], out messageId);
        }

        // -------------------------
        // Helper: NextMessageAsync
        // -------------------------

        private async Task<SocketMessage?> NextMessageAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessage>();
            var expectedUserId = Context.User.Id;
            var expectedChannelId = Context.Channel.Id;
            var client = Context.Client;

            Task Handler(SocketMessage message)
            {
                if (message.Channel.Id == expectedChannelId &&
                    message.Author.Id == expectedUserId &&
                    !message.Author.IsBot)
                {
                    tcs.TrySetResult(message);
                }
                return Task.CompletedTask;
            }

            client.MessageReceived += Handler;
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            client.MessageReceived -= Handler;

            return completed == tcs.Task ? await tcs.Task : null;
        }
    }
}


