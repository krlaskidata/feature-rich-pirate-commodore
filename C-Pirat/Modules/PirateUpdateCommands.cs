using System;
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
    public class PirateUpdateCommands : ModuleBase<SocketCommandContext>
    {
        private static readonly string ConfigDirectory = "Configs";
        private static readonly string TicketConfigFile = "pirate_ticket_config.json";

        // ─── ?ticket-update ──────────────────────────────────────────────────────

        [Command("ticket-update")]
        [RequirePirateAdmin]
        [Summary("Refresh the ticket embed message to the latest version. (Admin only)")]
        public async Task TicketUpdateAsync()
        {
            var guildId = Context.Guild.Id;
            var filePath = Path.Combine(ConfigDirectory, $"{guildId}_{TicketConfigFile}");

            PirateTicketConfig? config = null;
            try
            {
                if (File.Exists(filePath))
                    config = JsonSerializer.Deserialize<PirateTicketConfig>(File.ReadAllText(filePath));
            }
            catch { }

            if (config == null || config.TicketMessageChannelId == null || !config.TicketMessageId.HasValue)
            {
                await ReplyAsync("❌ No ticket message found in the config. Run `?pirate-ticket-setup` first.");
                return;
            }

            var channel = Context.Guild.GetTextChannel(config.TicketMessageChannelId.Value);
            if (channel == null)
            {
                await ReplyAsync("❌ The configured ticket channel no longer exists.");
                return;
            }

            IUserMessage? existing = null;
            try { existing = await channel.GetMessageAsync(config.TicketMessageId.Value) as IUserMessage; }
            catch { }

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Piraten-Support Ticket")
                .WithDescription("**Benötigst du Hilfe, Matrose?**\n\n" +
                                 "Klicke auf den Button unten, um ein Support-Ticket zu erstellen.\n" +
                                 "Cpt. Harper und der Schiffrat wird sich so schnell wie möglich um dein Anliegen kümmern!\n\n" +
                                 "**Was passiert als nächstes:**\n" +
                                 "• Du wählst eine Kategorie für dein Problem\n" +
                                 "• Ein privater Kanal wird für dich erstellt\n" +
                                 "• Das Support-Team wird benachrichtigt\n\n" +
                                 "*Ahoy und fair winds! ⚓*")
                .WithColor(0xFFFFFF)
                .WithFooter("Piraten-Support-System")
                .WithCurrentTimestamp()
                .Build();

            var button = new ComponentBuilder()
                .WithButton("🎟️ Ticket erstellen", "create_pirate_ticket", ButtonStyle.Primary)
                .Build();

            if (existing != null)
            {
                await existing.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = button;
                });
                await ReplyAsync("✅ Ticket embed updated successfully.");
            }
            else
            {
                var newMsg = await channel.SendMessageAsync(embed: embed, components: button);
                config.TicketMessageId = newMsg.Id;
                File.WriteAllText(filePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                await ReplyAsync($"✅ Old message not found — sent a new ticket embed in {channel.Mention}.");
            }
        }

        // ─── ?voice-update ───────────────────────────────────────────────────────

        [Command("voice-update")]
        [RequirePirateAdmin]
        [Summary("Refresh voice channel permissions and settings from saved config. (Admin only)")]
        public async Task VoiceUpdateAsync()
        {
            var cfg = PirateVoiceService.GetConfig(Context.Guild.Id);

            if (cfg.JoinToCreateChannel == null && !cfg.JoinToCreateChannels.Any())
            {
                await ReplyAsync("❌ No voice setup found. Run `?pirate-voice-setup` first.");
                return;
            }

            var updated = new List<string>();
            var missing = new List<string>();

            // Refresh all Join-to-Create channels
            var allJtcIds = cfg.JoinToCreateChannels.ToList();
            if (cfg.JoinToCreateChannel.HasValue && !allJtcIds.Contains(cfg.JoinToCreateChannel.Value))
                allJtcIds.Add(cfg.JoinToCreateChannel.Value);

            foreach (var channelId in allJtcIds)
            {
                var jtcChannel = Context.Guild.GetVoiceChannel(channelId);
                if (jtcChannel == null) { missing.Add($"`{channelId}`"); continue; }

                // Ensure @everyone can join (view + connect)
                await jtcChannel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                    new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow));

                // If AllowedRole is set, ensure it also has access
                if (cfg.AllowedRole.HasValue)
                {
                    var role = Context.Guild.GetRole(cfg.AllowedRole.Value);
                    if (role != null)
                        await jtcChannel.AddPermissionOverwriteAsync(role,
                            new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow));
                }

                updated.Add(jtcChannel.Mention);
            }

            // Refresh log channel permissions if set
            if (cfg.VoiceLogChannel.HasValue)
            {
                var logCh = Context.Guild.GetTextChannel(cfg.VoiceLogChannel.Value);
                if (logCh != null)
                {
                    await logCh.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                        new OverwritePermissions(viewChannel: PermValue.Deny));
                    updated.Add($"{logCh.Mention} (log — hidden from @everyone)");
                }
            }

            var desc = updated.Any() ? $"Updated:\n{string.Join("\n", updated)}" : "Nothing to update.";
            if (missing.Any()) desc += $"\n\n⚠️ Channels no longer exist: {string.Join(", ", missing)}";

            var embed = new EmbedBuilder()
                .WithTitle("✅ Voice Setup Refreshed")
                .WithDescription(desc)
                .WithColor(Color.Green)
                .WithFooter("Barbossa")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // ─── ?security-update ────────────────────────────────────────────────────

        [Command("security-update")]
        [RequirePirateAdmin]
        [Summary("Refresh security channel permissions from saved config. (Admin only)")]
        public async Task SecurityUpdateAsync()
        {
            var cfg = SecurityService.GetConfig(Context.Guild.Id);

            if (cfg == null || !cfg.Enabled)
            {
                await ReplyAsync("❌ Security system not configured. Run `?setsecuritymod` first.");
                return;
            }

            var updated = new List<string>();

            // Refresh 18+ voice channel permissions
            if (cfg.Age18VoiceChannelId.HasValue && cfg.Age18RoleId.HasValue)
            {
                var voiceCh = Context.Guild.GetVoiceChannel(cfg.Age18VoiceChannelId.Value);
                var age18Role = Context.Guild.GetRole(cfg.Age18RoleId.Value);

                if (voiceCh != null && age18Role != null)
                {
                    await voiceCh.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                        new OverwritePermissions(viewChannel: PermValue.Deny, connect: PermValue.Deny));
                    await voiceCh.AddPermissionOverwriteAsync(age18Role,
                        new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow));

                    if (cfg.AdminRoleId.HasValue)
                    {
                        var adminRole = Context.Guild.GetRole(cfg.AdminRoleId.Value);
                        if (adminRole != null)
                            await voiceCh.AddPermissionOverwriteAsync(adminRole,
                                new OverwritePermissions(viewChannel: PermValue.Allow, connect: PermValue.Allow));
                    }

                    updated.Add($"{voiceCh.Mention} (18+ permissions)");
                }
            }

            // Refresh log channel permissions
            if (cfg.LogChannelId.HasValue)
            {
                var logCh = Context.Guild.GetTextChannel(cfg.LogChannelId.Value);
                if (logCh != null)
                {
                    await logCh.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
                        new OverwritePermissions(viewChannel: PermValue.Deny));

                    if (cfg.AdminRoleId.HasValue)
                    {
                        var adminRole = Context.Guild.GetRole(cfg.AdminRoleId.Value);
                        if (adminRole != null)
                            await logCh.AddPermissionOverwriteAsync(adminRole,
                                new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
                    }

                    updated.Add($"{logCh.Mention} (log channel permissions)");
                }
            }

            var desc = updated.Any()
                ? $"Permissions refreshed for:\n{string.Join("\n", updated)}"
                : "No channels needed updating (log and 18+ channel not fully configured).";

            var embed = new EmbedBuilder()
                .WithTitle("✅ Security Setup Refreshed")
                .WithDescription(desc)
                .WithColor(Color.Green)
                .WithFooter("Barbossa")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // ─── ?verify-update ──────────────────────────────────────────────────────

        [Command("verify-update")]
        [RequirePirateAdmin]
        [Summary("Refresh the verify embed message to the latest version. (Admin only)")]
        public async Task VerifyUpdateAsync()
        {
            var config = PirateVerifyService.GetConfig(Context.Guild.Id);

            if (config == null)
            {
                await ReplyAsync("❌ Verification system not configured. Run `?verify-setup @role` first.");
                return;
            }

            var channel = Context.Guild.GetTextChannel(config.ChannelId);
            if (channel == null)
            {
                await ReplyAsync("❌ The configured verify channel no longer exists.");
                return;
            }

            IUserMessage? existing = null;
            if (config.MessageId.HasValue)
            {
                try { existing = await channel.GetMessageAsync(config.MessageId.Value) as IUserMessage; }
                catch { }
            }

            var role = Context.Guild.GetRole(config.RoleId);

            var embed = new EmbedBuilder()
                .WithTitle("✅ Server Verification")
                .WithDescription("**Welcome to the server!**\n\n" +
                                 "To access all channels and features, please verify yourself by clicking the button below.\n\n" +
                                 "**Verification Requirements:**\n" +
                                 "• Your Discord account must be at least 7 days old\n" +
                                 (config.RequireCaptcha ? "• Complete a simple captcha\n" : "") +
                                 "• Click the verification button\n\n" +
                                 "If you have any issues, contact a staff member.")
                .WithColor(0xFFFFFF)
                .WithFooter("Barbossa – Anti-bot protection enabled")
                .Build();

            var button = new ComponentBuilder()
                .WithButton("✅ Verify Me!", "pirate_verify_button", ButtonStyle.Success)
                .Build();

            if (existing != null)
            {
                await existing.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = button;
                });
                await ReplyAsync("✅ Verify embed updated successfully.");
            }
            else
            {
                var newMsg = await channel.SendMessageAsync(embed: embed, components: button);
                config.MessageId = newMsg.Id;
                PirateVerifyService.SetConfig(Context.Guild.Id, config);
                await ReplyAsync($"✅ Old verify message not found — sent a new one in {channel.Mention}.");
            }
        }
    }
}
