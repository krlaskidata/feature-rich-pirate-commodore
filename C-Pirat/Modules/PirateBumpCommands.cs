using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public class PirateBumpCommands : ModuleBase<SocketCommandContext>
    {
        [Command("pirate-bumpreminder")]
        [RequirePirateAdmin]
        public async Task SetBumpReminderAsync(SocketTextChannel channel)
        {
            PirateService.SetBumpReminderChannel(Context.Guild.Id, channel.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🏴‍☠️ Bump Reminder Set!")
                .WithDescription($"**Arrr! Bump reminders will now be posted in {channel.Mention}!**\n\n" +
                               "🔔 The crew will remind ye every 2 hours to bump the ship!")
                .WithFooter("Use ?pirate-bumpstatus to check the status!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-bumpstatus")]
        public async Task BumpStatusAsync()
        {
            var channelId = PirateService.GetBumpReminderChannel(Context.Guild.Id);
            if (!channelId.HasValue)
            {
                await ReplyAsync("❌ No bump reminder channel set, matey!");
                return;
            }

            var channel = Context.Guild.GetTextChannel(channelId.Value);
            var lastBump = PirateService.GetLastBumpTime(Context.Guild.Id);
            var nextBump = lastBump.AddHours(2);
            var timeUntil = nextBump - DateTime.UtcNow;

            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle("📊 Pirate Bump Status")
                .AddField("📢 Bump Channel", channel?.Mention ?? "Channel not found", true)
                .AddField("⏰ Last Bump", lastBump.ToString("HH:mm dd.MM.yyyy"), true)
                .AddField("🔜 Next Reminder", 
                    timeUntil.TotalMinutes > 0 ? 
                    $"in {timeUntil.Hours}h {timeUntil.Minutes}m" : 
                    "Ready to bump!", true)
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-bump")]
        public async Task BumpAsync()
        {
            var channelId = PirateService.GetBumpReminderChannel(Context.Guild.Id);
            if (!channelId.HasValue)
            {
                await ReplyAsync("❌ No bump reminder channel set! Use `?pirate-bumpreminder` first.");
                return;
            }

            PirateService.SetLastBumpTime(Context.Guild.Id, DateTime.UtcNow);

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("🚢 Ship Bumped!")
                .WithDescription("**Arrr! The ship has been bumped!**\n\n" +
                               "🏴‍☠️ The crew will remind ye in 2 hours to bump again!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // Static method for timer integration
        public static async Task SendBumpReminder(DiscordSocketClient client, ulong guildId)
        {
            try
            {
                var guild = client.GetGuild(guildId);
                if (guild == null) return;

                var channelId = PirateService.GetBumpReminderChannel(guildId);
                if (!channelId.HasValue) return;

                var channel = guild.GetTextChannel(channelId.Value);
                if (channel == null) return;

                var lastBump = PirateService.GetLastBumpTime(guildId);
                var timeSince = DateTime.UtcNow - lastBump;

                if (timeSince.TotalHours >= 2)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Orange)
                        .WithTitle("🔔 Bump Reminder!")
                        .WithDescription("**Ahoy crew!**\n\n" +
                                       "🏴‍☠️ It's time to bump our ship!\n" +
                                       "⚓ Use `/bump` to keep us sailing high!\n\n" +
                                       "*Remember: Every bump helps more pirates find our crew!*")
                        .WithFooter("Use ?pirate-bump to update the timer")
                        .WithCurrentTimestamp()
                        .Build();

                    await channel.SendMessageAsync(embed: embed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error sending bump reminder: {ex.Message}");
            }
        }
    }
}