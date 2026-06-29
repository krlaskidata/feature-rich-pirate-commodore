using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public class SotCommands : ModuleBase<SocketCommandContext>
    {
        private readonly DiscordSocketClient _client;

        public SotCommands(DiscordSocketClient client)
        {
            _client = client;
        }

        // ─── ?sot-set-profile ────────────────────────────────────────────────────

        [Command("sot-set-profile")]
        [Alias("sot-profile")]
        [Summary("Link your Xbox account to use Sea of Thieves stats commands.")]
        public async Task SetProfileAsync()
        {
            var askEmbed = new EmbedBuilder()
                .WithTitle("⚓ Sea of Thieves – Profile Setup")
                .WithDescription("Ahoy! Please send me your **Xbox Gamertag** (including the # and the numbers if you have one).\n\n" +
                                 "Example: `CaptainJack#1234`\n\n" +
                                 "*Type `cancel` at any time to abort.*")
                .WithColor(0x1DA1F2)
                .WithFooter("🏴‍☠️ Barbossa")
                .Build();
            await ReplyAsync(embed: askEmbed);

            var gamertagMsg = await NextMessageAsync(TimeSpan.FromMinutes(2));
            if (gamertagMsg == null || gamertagMsg.Content.Trim().ToLower() == "cancel")
            {
                await ReplyAsync("❌ Setup cancelled. Come back when ye're ready, matey!");
                return;
            }

            var gamertag = gamertagMsg.Content.Trim();
            if (!IsValidGamertag(gamertag))
            {
                await ReplyAsync("❌ That does not look like a valid Xbox Gamertag. Use 3-16 characters (letters, numbers, spaces, #, -, _), then run `?sot-set-profile` again.");
                return;
            }

            var searchingEmbed = new EmbedBuilder()
                .WithTitle("🔍 Searching...")
                .WithDescription($"Looking for Xbox account **`{gamertag}`** on the high seas…")
                .WithColor(Color.Orange)
                .Build();
            await ReplyAsync(embed: searchingEmbed);

            await Task.Delay(1200);

            var confirmEmbed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Is this your Xbox account?")
                .WithDescription($"**`{gamertag}`**\n\n" +
                                 "Reply with **Y** to confirm, or **N** to enter a different gamertag.")
                .WithColor(0x1DA1F2)
                .WithFooter("🏴‍☠️ Type Y or N")
                .Build();
            await ReplyAsync(embed: confirmEmbed);

            var confirmMsg = await NextMessageAsync(TimeSpan.FromMinutes(2));
            if (confirmMsg == null)
            {
                await ReplyAsync("❌ Confirmation timed out. Run `?sot-set-profile` again.");
                return;
            }

            var confirmInput = confirmMsg.Content.Trim().ToLowerInvariant();
            if (confirmInput == "n" || confirmInput == "no" || confirmInput == "cancel")
            {
                await ReplyAsync("❌ Profile not confirmed. Run `?sot-set-profile` again to try a different gamertag.");
                return;
            }

            if (confirmInput != "y" && confirmInput != "yes")
            {
                await ReplyAsync("❌ Invalid confirmation. Please answer with `Y`/`Yes` or `N`/`No`, then run `?sot-set-profile` again.");
                return;
            }

            SotProfileService.LinkProfile(Context.User.Id, gamertag);

            var successEmbed = new EmbedBuilder()
                .WithTitle("✅ Xbox Gamertag Linked!")
                .WithDescription($"Yer Xbox Gamertag **`{gamertag}`** has been linked to yer Discord account!\n\n" +
                                 "Use `?sot-show-ranks` to see yer profile link and linked account info.")
                .WithColor(Color.Green)
                .WithFooter("🏴‍☠️ Barbossa")
                .WithCurrentTimestamp()
                .Build();
            await ReplyAsync(embed: successEmbed);
        }

        // ─── ?sot-show-ranks ─────────────────────────────────────────────────────

        [Command("sot-show-ranks")]
        [Alias("sot-ranks")]
        [Summary("Show your linked Sea of Thieves profile.")]
        public async Task ShowRanksAsync()
        {
            var linked = SotProfileService.GetProfile(Context.User.Id);
            if (linked == null)
            {
                await ReplyAsync("\u274c Ye haven't linked yer Xbox account yet! Run `?sot-set-profile` first, matey!");
                return;
            }

            var guild = Context.Guild as SocketGuild;
            var guildUser = Context.User as SocketGuildUser;
            var linkedAt = new DateTimeOffset(linked.LinkedAt).ToUnixTimeSeconds();

            // Verify check (from server's security config)
            string verifyStatus = "Not configured";
            if (guild != null && guildUser != null)
            {
                var cfg = SecurityService.GetConfig(guild.Id);
                if (cfg?.VerifyRoleId.HasValue == true)
                {
                    verifyStatus = guildUser.Roles.Any(r => r.Id == cfg.VerifyRoleId.Value) ? "Verified" : "Not Verified";
                }
            }

            // Special role
            string specialRoleDisplay = "—";
            if (linked.SpecialRoleId.HasValue)
            {
                var role = guild?.GetRole(linked.SpecialRoleId.Value);
                if (role != null) specialRoleDisplay = role.Mention;
            }

            // XP
            string xpDisplay;
            if (linked.ReportedXp.HasValue && linked.LastUpdatedAt.HasValue)
            {
                var updatedAt = new DateTimeOffset(linked.LastUpdatedAt.Value).ToUnixTimeSeconds();
                xpDisplay = $"`{linked.ReportedXp.Value:N0}` *(updated <t:{updatedAt}:R>)*";
            }
            else
            {
                xpDisplay = "Not set yet";
            }

            var embed = new EmbedBuilder()
                .WithTitle($"Sea of Thieves \u2013 {linked.XboxGamertag}")
                .WithDescription($"Ahoy {Context.User.Mention}! Here's yer pirate profile.\n\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015\u2015")
                .WithColor(0x8B4513)
                .WithThumbnailUrl(Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl())
                .AddField("\u272b XBOX Gamertag", $"`{linked.XboxGamertag}`", false)
                .AddField("\u272b Special Role", specialRoleDisplay, false)
                .AddField("\u272b Verify Status", verifyStatus, false)
                .AddField("\u272b Achievement XPs", xpDisplay, false)
                .AddField("\u272b Linked on", $"<t:{linkedAt}:D>", false)
                .WithFooter("Barbossa")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        // ─── ?sot-update ─────────────────────────────────────────────────────────

        [Command("sot-update")]
        [RequirePirateAdmin]
        [Summary("Update a user's Sea of Thieves XP manually. (Admin only)")]
        public async Task UpdateStatsAsync([Remainder] string input = "")
        {
            var linked = SotProfileService.GetProfile(Context.User.Id);
            if (linked == null)
            {
                await ReplyAsync("❌ Ye haven't linked yer Xbox account yet! Run `?sot-set-profile` first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(input) || !long.TryParse(input.Trim().Replace(".", "").Replace(",", ""), out var xp) || xp < 0)
            {
                await ReplyAsync("❌ Please provide a valid XP amount.\nExample: `?sot-update 125000`");
                return;
            }

            SotProfileService.UpdateStats(Context.User.Id, xp);

            var embed = new EmbedBuilder()
                .WithTitle("✅ Stats Updated!")
                .WithDescription($"Yer Sea of Thieves stats have been recorded, **{Context.User.Mention}**!")
                .AddField("⚔️ Reported XP", $"`{xp:N0}`", true)
                .AddField("🕐 Recorded at", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f>", true)
                .WithColor(Color.Green)
                .WithFooter("🏴‍☠️ Barbossa • Use ?sot-show-ranks to see yer full profile")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // ─── ?sot-srole ──────────────────────────────────────────────────────────

        [Command("sot-srole")]
        [RequirePirateAdmin]
        [Summary("Set a special role to display on a Sea of Thieves profile. (Admin only)")]
        public async Task SetSpecialRoleAsync(ulong roleId)
        {
            var linked = SotProfileService.GetProfile(Context.User.Id);
            if (linked == null)
            {
                await ReplyAsync("❌ Ye haven't linked yer Xbox account yet! Run `?sot-set-profile` first.");
                return;
            }

            var guild = (Context.Guild as SocketGuild);
            var role = guild?.GetRole(roleId);
            if (role == null)
            {
                await ReplyAsync("❌ That role ID doesn't exist on this server. Right-click the role → Copy ID.");
                return;
            }

            SotProfileService.SetSpecialRole(Context.User.Id, roleId);

            var embed = new EmbedBuilder()
                .WithTitle("✅ Special Role Set!")
                .WithDescription($"Yer special role has been set to **{role.Mention}** and will now appear on yer profile.")
                .WithColor(role.Color)
                .WithFooter("🏴‍☠️ Barbossa • Use ?sot-show-ranks to see yer full profile")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // ─── ?sot-unlink ─────────────────────────────────────────────────────────

        [Command("sot-unlink")]
        [Summary("Remove your linked Xbox / SoT profile.")]
        public async Task UnlinkAsync()
        {
            var linked = SotProfileService.GetProfile(Context.User.Id);
            if (linked == null)
            {
                await ReplyAsync("⚠️ Ye don't have a linked profile, matey!");
                return;
            }

            SotProfileService.RemoveProfile(Context.User.Id);
            await ReplyAsync($"✅ Yer Xbox account **`{linked.XboxGamertag}`** has been unlinked from yer Discord. " +
                             "Run `?sot-set-profile` to link a new one.");
        }

        // ─── Helper ──────────────────────────────────────────────────────────────

        private async Task<SocketMessage?> NextMessageAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<SocketMessage?>();
            var expectedUserId = Context.User.Id;
            var expectedChannelId = Context.Channel.Id;

            Task OnMessageReceived(SocketMessage msg)
            {
                if (msg.Author.Id == expectedUserId && msg.Channel.Id == expectedChannelId)
                    tcs.TrySetResult(msg);
                return Task.CompletedTask;
            }

            _client.MessageReceived += OnMessageReceived;
            var timeoutTask = Task.Delay(timeout);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            _client.MessageReceived -= OnMessageReceived;

            if (completed == timeoutTask)
                tcs.TrySetResult(null);

            return await tcs.Task;
        }

        private static bool IsValidGamertag(string gamertag)
        {
            if (string.IsNullOrWhiteSpace(gamertag))
            {
                return false;
            }

            var trimmed = gamertag.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 24)
            {
                return false;
            }

            return Regex.IsMatch(trimmed, @"^[A-Za-z0-9 #_\-]+$");
        }
    }
}
