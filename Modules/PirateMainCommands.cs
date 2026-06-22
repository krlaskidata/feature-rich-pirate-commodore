using System.Threading.Tasks;
using Discord.Commands;
using Discord;
using Discord.WebSocket;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.IO;

namespace PiratBotCSharp.Modules
{
    public class PirateMainCommands : ModuleBase<SocketCommandContext>
    {
        [Command("ping")]
        [Summary("Check bot latency")]
        public async Task PingAsync()
        {
            var latency = Context.Client.Latency;
            await ReplyAsync($"🏴‍☠️ Ahoy! Latency: {latency}ms");
        }

        [Command("help")]
        [Summary("Shows all available pirate commands")]
        public async Task HelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("**Mary the Red - Pirate Bot**")
                .WithColor(0x8B4513)
                .WithDescription("*Ahoy matey! Welcome aboard the most fearsome pirate ship on Discord!*")
                
                .AddField("**ECONOMY SYSTEM**",
                    "`?pirate-daily` - Claim your daily gold treasure\n" +
                    "`?pirate-work` - Work for gold coins\n" +
                    "`?pirate-balance` - Check your treasure chest\n" +
                    "`?pirate-profile` - View your pirate profile\n" +
                    "`?pirate-rank` - Check your rank among pirates\n" +
                    "`?pirate-leaderboard` - See the richest pirates", false)

                .AddField("**BIRTHDAY SYSTEM**",
                    "`?pirate-birthdayset [DD.MM.YYYY]` - Set your birthday\n" +
                    "`?pirate-birthdayremove` - Remove someone's birthday (Admin)\n" +
                    "`?pirate-mybirthdayremove` - Remove your own birthday\n" +
                    "`?pirate-mybirthdayinfo` - Check your birthday info\n" +
                    "`?pirate-birthdaylist` - List all crew birthdays\n" +
                    "`?pirate-todaysbirthdays` - See today's birthdays\n" +
                    "`?pirate-birthdaychannel <#channel>` - Set birthday channel", false)

                .AddField("**BUMP REMINDER SYSTEM**",
                    "`?pirate-bumpreminder <#channel>` - Setup bump reminders\n" +
                    "`?pirate-bumpstatus` - Check reminder status\n" +
                    "`?pirate-bump` - Manual bump reminder", false)

                .AddField("**Security & Moderation**",
                    "`?setsecuritymod` - Setup security system for your ship\n" +
                    "`?disable` - Disable security system\n" +
                    "`?status` - Check security system status\n" +
                    "`?security-filters` - List all security categories and consequences\n" +
                    "`?warn [userid] [reason]` - Warn a crew member\n" +
                    "`?unban [userid]` - Unban a pirate\n" +
                    "`?timeoutdel @user` - Remove timeout from crew member\n" +
                    "`?close-sticket` - Close an open security appeal ticket\n" +
                    "`?cleanup [number]` - Clean the deck (delete messages)\n" +
                    "`?cleanup-now` - Deep clean the entire deck\n" +
                    "`?warnhelp` / `?unbanhelp` - Command usage help", false)

                .AddField("**Ticket System**",
                    "`?pirate-ticket-setup` - Setup ticket system for your crew\n" +
                    "`?pirate-set-support [ROLE_ID]` - Set support roles\n" +
                    "`?pirate-ticket-close` - Close the current ticket\n" +
                    "`?pirate-ticket-status` - Show ticket system status\n" +
                    "`?pirate-del-ticket-system` - Remove ticket system", false)

                .AddField("**Voice Features**",
                    "`?pirate-voice-setup` - Auto setup pirate voice system\n" +
                    "`?voicesetup [log-id] [category-id] [role-id]` - Manual setup\n" +
                    "`?create [category-id]` - Create Join-to-Create cabin\n" +
                    "`?voice-cleanup` - Clean up empty cabins (Admin)\n" +
                    "`?remove-voice-setup` - Disable voice system\n" +
                    "`?voicename [name]` - Rename your pirate cabin\n" +
                    "`?voicelimit [0-99]` - Set cabin crew limit (0=unlimited)\n" +
                    "`?voicelock` / `?voiceunlock` - Make cabin private/public\n" +
                    "`?voicehelp` - Voice system help", false)

                .AddField("**XP System**",
                    "`?run-xp-setup` - Setup pirate XP system with roles\n" +
                    "`?remove-xp-setup` - Remove XP system\n" +
                    "`?xp [user]` - Check pirate XP status\n" +
                    "`?xp-debug` - Debug XP system (Admin)\n" +
                    "`?create-rolemanagement-embed <#channel>` - Create role info", false)

                .AddField("**Utilities**",
                    "`?ping` - Check bot latency\n" +
                    "`?sendit MESSAGE_ID to CHANNEL_ID` - Send message to another channel\n" +
                    "`?info` - Bot information & stats\n" +
                    "`?gm` / `?gn` / `?hi` - Friendly pirate greetings\n" +
                    "`?piratehelp` - Interactive help menu", false)

                .AddField("**PIRATE CASINO & GAMES (Coming Soon)**",
                    "**In Development:**\n" +
                    "`?coinflip`, `?dice`, `?slots`, `?treasure`, `?raid`\n" +
                    "Advanced gambling features coming soon!", false)

                .WithFooter("Mary the Red - The Most Feared Pirate Bot! • heute um " + DateTime.Now.ToString("HH:ss") + " Uhr")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        // Command correction system - catches common command mistakes
        [Command("help-correct")]
        public async Task CorrectCommandAsync([Remainder] string? wrongCommand = null)
        {
            if (string.IsNullOrWhiteSpace(wrongCommand))
            {
                await ReplyAsync("⚔️ **Ahoy matey!** Use `?help` to see all available commands!");
                return;
            }

            var corrections = new Dictionary<string, string>
            {
                { "ticket", "ticket-setup" },
                { "setup-ticket", "ticket-setup" },
                { "voice", "pirate-voice-setup" },
                { "setup-voice", "pirate-voice-setup" },
                { "economy", "pirate-balance" },
                { "daily", "pirate-daily" },
                { "balance", "pirate-balance" },
                { "work", "pirate-work" },
                { "xp", "xp" },
                { "level", "xp" },
                { "security", "setsecuritymod" },
                { "warn", "warn [userid] [reason]" },
                { "ban", "Use Discord's built-in ban feature" },
                { "kick", "Use Discord's built-in kick feature" },
                { "mute", "Use Discord's timeout feature" },
                { "birthday", "pirate-birthdayset" }
            };

            var lowerCommand = wrongCommand.ToLower();
            var suggestion = corrections.FirstOrDefault(x => lowerCommand.Contains(x.Key));

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Command Correction")
                .WithColor(0xFF6B35)
                .WithDescription($"**Ahoy! Did ye mean:**\n`?{suggestion.Value ?? "help"}`")
                .AddField("💡 Tip", "Use `?help` to see all available commands, matey!")
                .WithFooter("Mary the Red • Command Assistant")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("info")]
        [Summary("Shows pirate bot information")]
        public async Task InfoAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Pirate Bot Information")
                .WithColor(0x8B4513)
                .AddField("Captain", Context.Client.CurrentUser.Username, true)
                .AddField("Server", "[Join the Crew! <3](https://discord.gg/hQmvHTs9vz)", true)
                .AddField("Sailing since", Context.Client.CurrentUser.CreatedAt.ToString("dd.MM.yyyy HH:mm"), true)
                .AddField("Latency", $"{Context.Client.Latency}ms", true)
                .AddField("Framework", "Discord.NET", true)
                .AddField("Language", "C#", true)
                .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl())
                .WithFooter("Made with ❤️ by the pirate crew")
                .WithCurrentTimestamp();

            await ReplyAsync(embed: embed.Build());
        }

        [Command("gn")]
        [Summary("Good night pirate message")]
        public async Task GoodNightAsync()
        {
            var messages = new[]
            {
                "Good night, matey! 🌙 Sleep tight in your cabin!",
                "Sweet dreams of treasure! 😴💰",
                "Sleep well, ye brave pirate! 🌙⚓",
                "May your dreams be full of gold! 😌🏴‍☠️",
                "Dream of the seven seas! 🌙🌊",
                "Rest well, captain! The crew will watch the ship! 🛏️⚓",
                "Time to rest those sea legs! 💪🌙",
                "Anchor down for the night! See ye at dawn! 🔱✨",
                "Off to Davy Jones' dreams! Safe travels! 🌊💫",
                "May your hammock be cozy and dreams be golden! 😊🏴‍☠️",
                "Sleep tight, don't let the sea monsters bite! 🌙🦈",
                "Batten down for a good night's rest! 💡😴",
                "Pleasant dreams of distant shores! 🌌🏴‍☠️",
                "Good night! May ye wake up richer! ☀️💰",
                "Rest well, the treasure hunt continues tomorrow! 🎒⚓",
                "Night night! Don't sail in your sleep! ⏰🚢",
                "Time to hit the hammock! Good night! 🌊💤",
                "Sweet slumber on the high seas! 😌🌙",
                "Good night! May your dreams be as grand as your adventures! 🌟🏴‍☠️",
                "Off to bed! The sunrise awaits! 🌅⚓"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        [Command("hi")]
        [Summary("Say ahoy")]
        public async Task HelloAsync()
        {
            var messages = new[]
            {
                "Ahoy there, matey! 🏴‍☠️ How's the wind in your sails?",
                "Greetings, brave pirate! ⚓ Welcome aboard!",
                "Ahoy! 🎉 Ready for some adventure?",
                "Hey there, sea dog! 🏴‍☠️ What treasures seek ye?",
                "Ahoy, captain! 🤠 Nice to have ye aboard!",
                "Greetings, fellow pirate! 😄 How be ye today?",
                "Ahoy there! 🌟 Good to see a friendly face!",
                "Hey hey, matey! ⚓ What brings ye to these waters?",
                "Ahoy! 😊 Hope yer having a grand adventure!",
                "Greetings, sailor! 🎊 How can this crew help?",
                "Yo ho ho! 🤘 What's happening on deck?",
                "Ahoy, friend! ⚓ How be your voyage?",
                "Hey there, buccaneer! 😁 Long time no sea!",
                "Ahoy! ✨ Ready to sail the seven seas?",
                "Greetings! 🌈 Lovely to see ye on the ship!",
                "Hey there, sea wolf! 🎮 What adventures await?",
                "Ahoy! 🚀 Hope ye be doing swimmingly!",
                "Greetings! 💬 Feel free to chat with the crew!",
                "Hey, matey! 🎵 How be everything with ye?",
                "Ahoy there, admiral! 🌻 Have a wonderful day on the seas!"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        [Command("gm")]
        [Summary("Good morning pirate message")]
        public async Task GoodMorningAsync()
        {
            var messages = new[]
            {
                "Good morning, captain! ☀️ Ready to sail today?",
                "Morning, matey! 🌅 Did ye sleep well in yer cabin?",
                "Good morning! ☕ Ready for a day of adventure?",
                "Morning, ye sea dog! 🌞 Hope ye be feeling shipshape!",
                "Good morning! 🌻 Let's conquer the seven seas today!",
                "Rise and shine, pirate! ✨ Time to hunt for treasure!",
                "Good morning! 🌈 Make today legendary!",
                "Morning, captain! ☀️ Ready to set sail?",
                "Top of the morning, matey! 🎩 Let's plunder today!",
                "Good morning! 🚀 Today's seas are full of possibilities!",
                "Wake up and be awesome, ye pirate! 💪 Good morning!",
                "Morning, buccaneer! 🌄 Hope ye slept like a baby!",
                "Good morning! 🎊 Time to make some waves!",
                "Rise and grind, matey! ⚡ Good morning!",
                "Morning, sea eagle! 🦅 Soar high on the winds today!",
                "Good morning! 🌺 Wishing ye fair winds and following seas!",
                "Wakey wakey, ye scurvy dog! 🥞 Time for some grub!",
                "Good morning! 🎯 Let's chart a course for success!",
                "Morning, star navigator! 🌟 Shine bright today!",
                "Good morning! 🎮 Ready to level up yer pirate skills?"
            };

            var random = new Random();
            var message = messages[random.Next(messages.Length)];

            await ReplyAsync(message);
        }

        [Command("sendit")]
        [Summary("Forward a message to another channel")]
        [RequirePirateAdmin]
        public async Task SendItAsync([Remainder] string? args = null)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                var usageEmbed = new EmbedBuilder()
                    .WithColor(0xFFD700) // Gold/Yellow
                    .WithTitle("📝 ?sendit Command Usage")
                    .WithDescription("Forward a message from this channel to another channel.")
                    .AddField("Usage", "`?sendit MESSAGE_ID to CHANNEL_ID`", false)
                    .AddField("Example", "`?sendit 1234567890123456789 to 9876543210987654321`", false)
                    .WithFooter("You need Administrator permissions to use this command")
                    .Build();

                await ReplyAsync(embed: usageEmbed);
                return;
            }

            // Parse: MESSAGE_ID to CHANNEL_ID
            var parts = args.Split(new[] { " to " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                await ReplyAsync("❌ Invalid format! Use: `?sendit MESSAGE_ID to CHANNEL_ID`");
                return;
            }

            var messageIdStr = parts[0].Trim();
            var channelIdStr = parts[1].Trim();

            if (!ulong.TryParse(messageIdStr, out var messageId))
            {
                await ReplyAsync("❌ Invalid MESSAGE_ID! Must be a valid snowflake ID.");
                return;
            }

            if (!ulong.TryParse(channelIdStr, out var channelId))
            {
                await ReplyAsync("❌ Invalid CHANNEL_ID! Must be a valid snowflake ID.");
                return;
            }

            try
            {
                // Fetch the message from current channel
                var originalMessage = await Context.Channel.GetMessageAsync(messageId);
                if (originalMessage == null)
                {
                    var notFoundEmbed = new EmbedBuilder()
                        .WithTitle("❌ Message Not Found")
                        .WithDescription("Message not found in this channel!")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: notFoundEmbed);
                    return;
                }

                // Get target channel
                var targetChannel = Context.Guild.GetTextChannel(channelId);
                if (targetChannel == null)
                {
                    var chanNotFoundEmbed = new EmbedBuilder()
                        .WithTitle("❌ Channel Not Found")
                        .WithDescription("Target channel not found or not accessible!")
                        .WithColor(0x40E0D0)
                        .Build();
                    await ReplyAsync(embed: chanNotFoundEmbed);
                    return;
                }

                // Check bot permissions in target channel
                var botPerms = targetChannel.GetPermissionOverwrite(Context.Guild.CurrentUser);
                if (botPerms?.SendMessages == PermValue.Deny)
                {
                    await ReplyAsync($"❌ I don't have permission to send messages in {targetChannel.Mention}!");
                    return;
                }

                // Forward only the message content - no embeds, no author info
                var content = originalMessage.Content;
                
                // If message has content, send it
                if (!string.IsNullOrWhiteSpace(content))
                {
                    await targetChannel.SendMessageAsync(content);
                }
                
                // Forward attachments if any
                if (originalMessage.Attachments.Count > 0)
                {
                    foreach (var attachment in originalMessage.Attachments)
                    {
                        await targetChannel.SendMessageAsync(attachment.Url);
                    }
                }

                await ReplyAsync($"✅ Message forwarded to {targetChannel.Mention}!");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"❌ Error forwarding message: {ex.Message}");
            }
        }

        [Command("piratehelp")]
        [Alias("interactive-help")]
        [Summary("Interactive pirate help menu")]
        public async Task PirateHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Interactive Pirate Help")
                .WithDescription("What kind of help do ye need, matey?")
                .WithColor(0x8B4513)
                .AddField("📚 Categories Available:", 
                    "• Economy & Games\n" +
                    "• Voice System\n" +
                    "• Security & Moderation\n" +
                    "• Ticket System\n" +
                    "• Birthday System\n" +
                    "• Utilities", false)
                .WithFooter("Use ?help for a complete command list")
                .Build();

            await ReplyAsync(embed: embed);
        }
    }
}