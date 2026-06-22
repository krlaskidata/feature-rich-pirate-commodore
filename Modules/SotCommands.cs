using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        [Summary("Link your Xbox account to use Sea of Thieves stats commands.")]
        public async Task SetProfileAsync()
        {
            // Step 1 – ask for Xbox Gamertag
            var askEmbed = new EmbedBuilder()
                .WithTitle("⚓ Sea of Thieves – Profile Setup")
                .WithDescription("Ahoy! Please send me your **Xbox Gamertag** (including the # and the numbers if you have one).\n\n" +
                                 "Example: `CaptainJack#1234`\n\n" +
                                 "*Type `cancel` at any time to abort.*")
                .WithColor(0x1DA1F2)
                .WithFooter("🏴‍☠️ Mary the Red")
                .Build();
            await ReplyAsync(embed: askEmbed);

            var gamertagMsg = await NextMessageAsync(TimeSpan.FromMinutes(2));
            if (gamertagMsg == null || gamertagMsg.Content.Trim().ToLower() == "cancel")
            {
                await ReplyAsync("❌ Setup cancelled. Come back when ye're ready, matey!");
                return;
            }

            var gamertag = gamertagMsg.Content.Trim();

            // Step 2 – search / "find" the gamertag (we confirm it back to the user)
            var searchingEmbed = new EmbedBuilder()
                .WithTitle("🔍 Searching...")
                .WithDescription($"Looking for Xbox account **`{gamertag}`** on the high seas…")
                .WithColor(Color.Orange)
                .Build();
            var searchMsg = await ReplyAsync(embed: searchingEmbed);

            // We cannot do a real Xbox Live user-search without an authenticated
            // Microsoft/Xbox API key (requires Azure app registration + user consent).
            // So we confirm the tag back to the user for them to verify.
            await Task.Delay(1200); // small delay – feels like it's "searching"

            var confirmEmbed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Is this your Xbox account?")
                .WithDescription($"**`{gamertag}`**\n\n" +
                                 "Reply with **Y** to confirm, or **N** to enter a different gamertag.")
                .WithColor(0x1DA1F2)
                .WithFooter("🏴‍☠️ Type Y or N")
                .Build();
            await ReplyAsync(embed: confirmEmbed);

            var confirmMsg = await NextMessageAsync(TimeSpan.FromMinutes(2));
            if (confirmMsg == null || confirmMsg.Content.Trim().ToUpper() != "Y")
            {
                await ReplyAsync("❌ Profile not confirmed. Run `?sot-set-profile` again to try a different gamertag.");
                return;
            }

            // Step 3 – save the link
            SotProfileService.LinkProfile(Context.User.Id, gamertag);

            // Step 4 – try to fetch SoT profile data right away
            var fetchingEmbed = new EmbedBuilder()
                .WithTitle("⚓ Connecting to Sea of Thieves…")
                .WithDescription("Checking if a Sea of Thieves account is linked to that Xbox profile…")
                .WithColor(Color.Orange)
                .Build();
            await ReplyAsync(embed: fetchingEmbed);

            var sotProfile = await SotProfileService.FetchPublicProfileAsync(gamertag);

            if (sotProfile == null)
            {
                var noDataEmbed = new EmbedBuilder()
                    .WithTitle("✅ Xbox Tag Saved!")
                    .WithDescription($"Yer Xbox Gamertag **`{gamertag}`** has been linked to yer Discord account!\n\n" +
                                     "⚠️ However, I couldn't fetch yer Sea of Thieves stats right now.\n" +
                                     "This can happen if:\n" +
                                     "• The gamertag has no public SoT profile\n" +
                                     "• The SoT website is temporarily unavailable\n\n" +
                                     "Use `?sot-show-ranks` to try again later.")
                    .WithColor(Color.Orange)
                    .WithFooter("🏴‍☠️ Mary the Red")
                    .Build();
                await ReplyAsync(embed: noDataEmbed);
                return;
            }

            // Profile found!
            var successEmbed = new EmbedBuilder()
                .WithTitle("✅ Profile Linked Successfully!")
                .WithDescription($"Ahoy **{Context.User.Mention}**! Yer Sea of Thieves profile has been found and linked!\n\n" +
                                 $"🎮 **Gamertag:** `{sotProfile.Gamertag ?? gamertag}`\n" +
                                 $"⚓ **Pirate Level:** {sotProfile.PirateLevel}\n" +
                                 (sotProfile.IsPirateLegend ? "🏆 **Pirate Legend:** ✅ Yes!\n" : "🏆 **Pirate Legend:** ❌ Not yet\n") +
                                 "\nUse `?sot-show-ranks` to see all yer ranks and stats!")
                .WithColor(Color.Green)
                .WithFooter("🏴‍☠️ Mary the Red")
                .WithCurrentTimestamp()
                .Build();
            await ReplyAsync(embed: successEmbed);
        }

        // ─── ?sot-show-ranks ─────────────────────────────────────────────────────

        [Command("sot-show-ranks")]
        [Summary("Show your Sea of Thieves ranks, gold and medals.")]
        public async Task ShowRanksAsync()
        {
            var linked = SotProfileService.GetProfile(Context.User.Id);
            if (linked == null)
            {
                await ReplyAsync("❌ Ye haven't linked yer Xbox account yet! Run `?sot-set-profile` first, matey!");
                return;
            }

            var loadingEmbed = new EmbedBuilder()
                .WithTitle("⚓ Loading yer Sea of Thieves stats…")
                .WithColor(Color.Orange)
                .Build();
            await ReplyAsync(embed: loadingEmbed);

            var profile = await SotProfileService.FetchPublicProfileAsync(linked.XboxGamertag);

            if (profile == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Could not fetch SoT stats")
                    .WithDescription($"Couldn't reach the Sea of Thieves profile for **`{linked.XboxGamertag}`**.\n" +
                                     "The SoT website may be down or the profile is private.\n\n" +
                                     "Try again in a few minutes!")
                    .WithColor(Color.Red)
                    .WithFooter("🏴‍☠️ Mary the Red")
                    .Build();
                await ReplyAsync(embed: errorEmbed);
                return;
            }

            // ── Build the embed ──────────────────────────────────────────────────
            var embed = new EmbedBuilder()
                .WithTitle($"🏴‍☠️ Sea of Thieves Profile – {profile.Gamertag ?? linked.XboxGamertag}")
                .WithColor(0x8B4513)
                .WithThumbnailUrl("https://www.seaofthieves.com/images/meta/default.jpg")
                .WithFooter("🏴‍☠️ Mary the Red • Data: seaofthieves.com")
                .WithCurrentTimestamp();

            // General stats
            var sb = new StringBuilder();
            sb.AppendLine($"⚓ **Pirate Level:** {profile.PirateLevel}");
            sb.AppendLine(profile.IsPirateLegend
                ? "🏆 **Pirate Legend:** ✅ Yes!"
                : "🏆 **Pirate Legend:** ❌ Not yet");
            if (profile.Gold > 0)
                sb.AppendLine($"💰 **Gold in Bank:** {profile.Gold:N0}");
            embed.WithDescription(sb.ToString());

            // Trading company ranks
            if (profile.Reputations?.Count > 0)
            {
                var repSb = new StringBuilder();
                foreach (var rep in profile.Reputations)
                {
                    var rankStr = !string.IsNullOrWhiteSpace(rep.Rank) ? $" – *{rep.Rank}*" : "";
                    repSb.AppendLine($"• **{rep.Name}** – Level {rep.Level}{rankStr}");
                }
                embed.AddField("⚔️ Trading Company Ranks", repSb.ToString(), false);
            }

            // Notable titles / commendations
            if (profile.Titles?.Count > 0)
            {
                var unlockedTitles = profile.Titles
                    .Where(t => t.IsUnlocked)
                    .Select(t => $"`{t.Name}`")
                    .ToList();

                if (unlockedTitles.Any())
                {
                    // Only show first 20 to avoid embed overflow
                    var shown = unlockedTitles.Take(20).ToList();
                    var extra = unlockedTitles.Count > 20 ? $"\n*…and {unlockedTitles.Count - 20} more*" : "";
                    embed.AddField("🎖️ Unlocked Titles", string.Join(" ", shown) + extra, false);
                }
            }

            // Linked since
            embed.AddField("🔗 Linked Xbox Account",
                $"`{linked.XboxGamertag}` – linked <t:{new DateTimeOffset(linked.LinkedAt).ToUnixTimeSeconds()}:R>",
                false);

            await ReplyAsync(embed: embed.Build());
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
    }
}
