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
    public class PirateVerifyConfigEntry
    {
        public ulong ChannelId { get; set; }
        public ulong RoleId { get; set; }
        public ulong? MessageId { get; set; }
        public ulong? LogChannelId { get; set; }
        public bool RequireCaptcha { get; set; } = false;
        public Dictionary<ulong, bool?> Snapshot { get; set; } = new Dictionary<ulong, bool?>();
        public Dictionary<ulong, DateTime> PendingVerifications { get; set; } = new Dictionary<ulong, DateTime>();
    }

    public class PirateVerifyAttempt
    {
        public ulong UserId { get; set; }
        public string? CaptchaCode { get; set; }
        public DateTime CreatedAt { get; set; }
        public int AttemptCount { get; set; } = 0;
    }

    public static class PirateVerifyService
    {
        private const string VERIFY_FILE = "pirate_verify_config.json";
        private static Dictionary<ulong, PirateVerifyConfigEntry> _cfg = LoadConfig();
        private static ConcurrentDictionary<ulong, PirateVerifyAttempt> _pendingCaptchas = new();
        private static readonly Random _random = new Random();

        private static DateTime _lastCleanup = DateTime.UtcNow;
        private const int MAX_PENDING_CAPTCHAS = 100;
        private const int CAPTCHA_EXPIRY_MINUTES = 30;

        private static Dictionary<ulong, PirateVerifyConfigEntry> LoadConfig()
        {
            try
            {
                if (!File.Exists(VERIFY_FILE)) return new Dictionary<ulong, PirateVerifyConfigEntry>();
                var txt = File.ReadAllText(VERIFY_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, PirateVerifyConfigEntry>>(txt)
                       ?? new Dictionary<ulong, PirateVerifyConfigEntry>();
            }
            catch { return new Dictionary<ulong, PirateVerifyConfigEntry>(); }
        }

        private static void SaveConfig()
        {
            try
            {
                var txt = JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(VERIFY_FILE, txt);
            }
            catch { }
        }

        public static PirateVerifyConfigEntry? GetConfig(ulong guildId)
        {
            return _cfg.TryGetValue(guildId, out var e) ? e : null;
        }

        public static void SetConfig(ulong guildId, PirateVerifyConfigEntry config)
        {
            _cfg[guildId] = config;
            SaveConfig();
        }

        public static void RemoveConfig(ulong guildId)
        {
            if (_cfg.ContainsKey(guildId)) { _cfg.Remove(guildId); SaveConfig(); }
        }

        public static string GenerateCaptcha()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }

        public static async Task HandleVerifyButtonAsync(SocketMessageComponent component)
        {
            try
            {
                if (component.Data.CustomId != "pirate_verify_button") return;

                if (!component.GuildId.HasValue)
                {
                    await component.RespondAsync("❌ Server not found.", ephemeral: true);
                    return;
                }

                var config = GetConfig(component.GuildId.Value);
                if (config == null)
                {
                    await component.RespondAsync("❌ Verification system not configured.", ephemeral: true);
                    return;
                }

                var guild = (component.User as SocketGuildUser)?.Guild;
                var user = component.User as SocketGuildUser;

                if (guild == null || user == null)
                {
                    await component.RespondAsync("❌ Unable to access server information.", ephemeral: true);
                    return;
                }

                var role = guild.GetRole(config.RoleId);
                if (role == null)
                {
                    await component.RespondAsync("❌ Verification role not found.", ephemeral: true);
                    return;
                }

                if (user.Roles.Contains(role))
                {
                    await component.RespondAsync("✅ You are already verified!", ephemeral: true);
                    return;
                }

                if ((DateTime.UtcNow - user.CreatedAt.UtcDateTime).TotalDays < 7)
                {
                    await component.RespondAsync("❌ Your account must be at least 7 days old to verify.", ephemeral: true);
                    await LogVerificationAttempt(guild, user, "Account too new", false);
                    return;
                }

                if (config.RequireCaptcha)
                {
                    await HandleCaptchaVerification(component, config);
                    return;
                }

                await VerifyUser(component, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pirate verify button error: {ex.Message}");
                await component.RespondAsync("❌ Verification failed. Please try again.", ephemeral: true);
            }
        }

        private static async Task HandleCaptchaVerification(SocketMessageComponent component, PirateVerifyConfigEntry config)
        {
            CleanupExpiredCaptchas();

            var captchaCode = GenerateCaptcha();
            _pendingCaptchas[component.User.Id] = new PirateVerifyAttempt
            {
                UserId = component.User.Id,
                CaptchaCode = captchaCode,
                CreatedAt = DateTime.UtcNow,
                AttemptCount = 0
            };

            var embed = new EmbedBuilder()
                .WithTitle("🔐 Verification Required")
                .WithDescription($"Please solve this captcha to verify:\n\n**Enter this code:** `{captchaCode}`\n\nReply with just the code in this channel.")
                .WithColor(Color.Orange)
                .WithFooter("Expires in 5 minutes");

            await component.RespondAsync(embed: embed.Build(), ephemeral: true);

            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                _pendingCaptchas.TryRemove(component.User.Id, out var _);
            });
        }

        public static async Task HandleCaptchaResponse(SocketMessage message)
        {
            try
            {
                if (message.Author.IsBot) return;
                if (!_pendingCaptchas.TryGetValue(message.Author.Id, out var attempt)) return;

                var guild = (message.Channel as SocketTextChannel)?.Guild;
                if (guild == null) return;

                var config = GetConfig(guild.Id);
                if (config == null) return;

                attempt.AttemptCount++;

                if (message.Content.Trim().ToUpper() == attempt.CaptchaCode)
                {
                    _pendingCaptchas.TryRemove(message.Author.Id, out _);

                    var role = guild.GetRole(config.RoleId);
                    var user = guild.GetUser(message.Author.Id);

                    if (role != null && user != null)
                    {
                        await user.AddRoleAsync(role);
                        config.Snapshot[user.Id] = true;
                        SaveConfig();

                        if (message is IUserMessage userMessage)
                            await userMessage.ReplyAsync($"✅ **Verification successful!** You now have access to {guild.Name}!");
                        await LogVerificationAttempt(guild, user, "Captcha verification", true);
                    }
                }
                else
                {
                    if (attempt.AttemptCount >= 3)
                    {
                        _pendingCaptchas.TryRemove(message.Author.Id, out _);
                        if (message is IUserMessage userMessage2)
                            await userMessage2.ReplyAsync("❌ **Too many failed attempts.** Please try the verification process again.");
                        await LogVerificationAttempt(guild, message.Author, "Failed captcha (3 attempts)", false);
                    }
                    else
                    {
                        if (message is IUserMessage userMessage3)
                            await userMessage3.ReplyAsync($"❌ **Incorrect code.** Try again. ({attempt.AttemptCount}/3 attempts)");
                    }
                }

                try { await message.DeleteAsync(); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pirate captcha response error: {ex.Message}");
            }
        }

        private static async Task VerifyUser(SocketMessageComponent component, PirateVerifyConfigEntry config)
        {
            try
            {
                var guild = (component.User as SocketGuildUser)?.Guild;
                var user = component.User as SocketGuildUser;

                if (guild == null || user == null) return;

                var role = guild.GetRole(config.RoleId);

                await user.AddRoleAsync(role);
                config.Snapshot[user.Id] = true;
                config.PendingVerifications[user.Id] = DateTime.UtcNow;
                SaveConfig();

                await component.RespondAsync($"✅ **Welcome to {guild.Name}!** You have been verified successfully!", ephemeral: true);
                await LogVerificationAttempt(guild, user, "Button verification", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pirate user verification error: {ex.Message}");
                await component.RespondAsync("❌ Failed to verify. Please contact an administrator.", ephemeral: true);
            }
        }

        private static async Task LogVerificationAttempt(SocketGuild guild, SocketUser user, string method, bool success)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config?.LogChannelId == null) return;

                var logChannel = guild.GetTextChannel(config.LogChannelId.Value);
                if (logChannel == null) return;

                var embed = new EmbedBuilder()
                    .WithTitle(success ? "✅ Verification Successful" : "❌ Verification Failed")
                    .WithColor(success ? Color.Green : Color.Red)
                    .AddField("User", $"{user.Mention} (`{user.Id}`)", true)
                    .AddField("Method", method, true)
                    .AddField("Account Age", $"{(DateTime.UtcNow - user.CreatedAt.UtcDateTime).TotalDays:F1} days", true)
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl());

                await logChannel.SendMessageAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pirate verification log error: {ex.Message}");
            }
        }

        public static async Task<bool> ManualVerifyUser(SocketGuild guild, SocketUser user, SocketUser moderator)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config == null) return false;

                var role = guild.GetRole(config.RoleId);
                if (role == null) return false;

                var guildUser = guild.GetUser(user.Id);
                if (guildUser == null) return false;

                await guildUser.AddRoleAsync(role);
                config.Snapshot[user.Id] = true;
                SaveConfig();

                await LogVerificationAttempt(guild, user, $"Manual verification by {moderator.Username}", true);
                return true;
            }
            catch { return false; }
        }

        public static async Task<bool> UnverifyUser(SocketGuild guild, SocketUser user, SocketUser moderator)
        {
            try
            {
                var config = GetConfig(guild.Id);
                if (config == null) return false;

                var role = guild.GetRole(config.RoleId);
                if (role == null) return false;

                var guildUser = guild.GetUser(user.Id);
                if (guildUser == null) return false;

                await guildUser.RemoveRoleAsync(role);
                config.Snapshot[user.Id] = false;
                SaveConfig();

                await LogVerificationAttempt(guild, user, $"Manual unverification by {moderator.Username}", false);
                return true;
            }
            catch { return false; }
        }

        private static void CleanupExpiredCaptchas()
        {
            try
            {
                if (DateTime.UtcNow - _lastCleanup < TimeSpan.FromMinutes(10)) return;
                _lastCleanup = DateTime.UtcNow;

                var expiredTime = DateTime.UtcNow.AddMinutes(-CAPTCHA_EXPIRY_MINUTES);
                var expiredUsers = _pendingCaptchas
                    .Where(kvp => kvp.Value.CreatedAt < expiredTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var userId in expiredUsers)
                    _pendingCaptchas.TryRemove(userId, out _);

                if (_pendingCaptchas.Count > MAX_PENDING_CAPTCHAS)
                {
                    var oldest = _pendingCaptchas
                        .OrderBy(kvp => kvp.Value.CreatedAt)
                        .Take(_pendingCaptchas.Count - MAX_PENDING_CAPTCHAS)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var userId in oldest)
                        _pendingCaptchas.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pirate captcha cleanup error: {ex.Message}");
            }
        }
    }

    public class PirateVerifyCommands : ModuleBase<SocketCommandContext>
    {
        [Command("verify-setup")]
        [Summary("Setup verification system (Admin only)")]
        [RequirePirateAdmin]
        public async Task SetupVerifyAsync(IRole role, ITextChannel? logChannel = null, bool requireCaptcha = false)
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithTitle("✅ Server Verification")
                    .WithDescription("**Welcome to the server!**\n\n" +
                                     "To access all channels and features, please verify yourself by clicking the button below.\n\n" +
                                     "**Verification Requirements:**\n" +
                                     "• Your Discord account must be at least 7 days old\n" +
                                     (requireCaptcha ? "• Complete a simple captcha\n" : "") +
                                     "• Click the verification button\n\n" +
                                     "If you have any issues, contact a staff member.")
                    .WithColor(0xFFFFFF)
                    .WithFooter("🏴‍☠️ Barbossa – Anti-bot protection enabled");

                var button = new ComponentBuilder()
                    .WithButton("✅ Verify Me!", "pirate_verify_button", ButtonStyle.Success);

                var message = await Context.Channel.SendMessageAsync(embed: embed.Build(), components: button.Build());

                var config = new PirateVerifyConfigEntry
                {
                    ChannelId = Context.Channel.Id,
                    RoleId = role.Id,
                    MessageId = message.Id,
                    LogChannelId = logChannel?.Id,
                    RequireCaptcha = requireCaptcha
                };

                PirateVerifyService.SetConfig(Context.Guild.Id, config);

                // Also update SecurityConfigEntry so ?sot-show-ranks can check the verify role
                var secCfg = SecurityService.GetConfig(Context.Guild.Id);
                if (secCfg == null) secCfg = new SecurityConfigEntry { Enabled = true };
                secCfg.VerifyRoleId = role.Id;
                SecurityService.SetConfig(Context.Guild.Id, secCfg);

                var setupEmbed = new EmbedBuilder()
                    .WithTitle("✅ Verification System Configured")
                    .WithColor(Color.Green)
                    .AddField("Verified Role", role.Mention, true)
                    .AddField("Log Channel", logChannel?.Mention ?? "None", true)
                    .AddField("Captcha Required", requireCaptcha ? "Yes" : "No", true)
                    .AddField("Anti-bot Protection", "7+ day account age", false)
                    .WithDescription("Users can now verify using the button above!")
                    .WithFooter("Barbossa")
                    .Build();

                await ReplyAsync(embed: setupEmbed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Setup failed: {ex.Message}");
            }
        }

        [Command("verify")]
        [Summary("Manually verify a user (Admin only)")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task ManualVerifyAsync(SocketGuildUser user)
        {
            try
            {
                var config = PirateVerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured. Run `?verify-setup @role` first.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                if (user.Roles.Contains(role))
                {
                    await ReplyAsync("❌ User is already verified.");
                    return;
                }

                var success = await PirateVerifyService.ManualVerifyUser(Context.Guild, user, Context.User);
                if (success)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("✅ User Verified")
                        .WithColor(Color.Green)
                        .AddField("User", user.Mention, true)
                        .AddField("Verified By", Context.User.Mention, true)
                        .AddField("Method", "Manual verification", true)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await ReplyAsync(embed: embed);
                }
                else
                {
                    await ReplyAsync("❌ Failed to verify user.");
                }
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error: {ex.Message}");
            }
        }

        [Command("unverify")]
        [Summary("Remove verification from a user (Admin only)")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task UnverifyAsync(SocketGuildUser user)
        {
            try
            {
                var config = PirateVerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                if (!user.Roles.Contains(role))
                {
                    await ReplyAsync("❌ User is not verified.");
                    return;
                }

                var success = await PirateVerifyService.UnverifyUser(Context.Guild, user, Context.User);
                if (success)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("❌ User Unverified")
                        .WithColor(Color.Orange)
                        .AddField("User", user.Mention, true)
                        .AddField("Unverified By", Context.User.Mention, true)
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    await ReplyAsync(embed: embed);
                }
                else
                {
                    await ReplyAsync("❌ Failed to unverify user.");
                }
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error: {ex.Message}");
            }
        }

        [Command("verify-status")]
        [Summary("Check verification system status (Admin only)")]
        [RequirePirateAdmin]
        public async Task VerifyStatusAsync()
        {
            try
            {
                var config = PirateVerifyService.GetConfig(Context.Guild.Id);
                if (config == null)
                {
                    await ReplyAsync("❌ Verification system not configured. Run `?verify-setup @role` first.");
                    return;
                }

                var role = Context.Guild.GetRole(config.RoleId);
                var channel = Context.Guild.GetTextChannel(config.ChannelId);
                var logChannel = config.LogChannelId.HasValue ? Context.Guild.GetTextChannel(config.LogChannelId.Value) : null;
                var verifiedCount = config.Snapshot.Count(s => s.Value == true);

                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Verification System Status")
                    .WithColor(0x8B4513)
                    .AddField("Status", "✅ Active", true)
                    .AddField("Verified Role", role?.Mention ?? "Not found", true)
                    .AddField("Verify Channel", channel?.Mention ?? "Not found", true)
                    .AddField("Log Channel", logChannel?.Mention ?? "None", true)
                    .AddField("Captcha", config.RequireCaptcha ? "Enabled" : "Disabled", true)
                    .AddField("Verified Members", verifiedCount.ToString(), true)
                    .WithFooter("Barbossa")
                    .WithCurrentTimestamp()
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error: {ex.Message}");
            }
        }

        [Command("verify-remove")]
        [Summary("Remove the verification system (Admin only)")]
        [RequirePirateAdmin]
        public async Task RemoveVerifyAsync()
        {
            PirateVerifyService.RemoveConfig(Context.Guild.Id);
            await ReplyAsync("✅ Verification system has been removed.");
        }
    }
}
