using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public class PirateBirthdayCommands : ModuleBase<SocketCommandContext>
    {
        [Command("pirate-birthdaychannel")]
        [RequirePirateAdmin]
        public async Task SetBirthdayChannelAsync(SocketTextChannel channel)
        {
            PirateService.SetBirthdayChannel(Context.Guild.Id, channel.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🎂 Birthday Channel Set!")
                .WithDescription($"**Arrr! Birthday announcements will now be posted in {channel.Mention}!**\n\n" +
                               "🏴‍☠️ The crew will celebrate all birthdays here!")
                .WithFooter("Use !pirate-birthdayset to add your birthday!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-birthdayset")]
        public async Task SetBirthdayAsync([Remainder] string dateInput)
        {
            // Parse different date formats
            DateTime birthday;
            var formats = new[] { "dd.MM.yyyy", "dd/MM/yyyy", "dd-MM-yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };
            
            if (!DateTime.TryParseExact(dateInput, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out birthday))
            {
                await ReplyAsync("❌ Invalid date format, matey! Please use: DD.MM.YYYY (e.g., 15.03.1995)");
                return;
            }

            // Check if date is reasonable
            if (birthday.Year < 1900 || birthday > DateTime.Now.AddDays(-365)) // At least 1 year old
            {
                await ReplyAsync("❌ That date doesn't seem right, matey! Please enter a valid birth date.");
                return;
            }

            PirateService.SetBirthday(Context.User.Id, birthday, Context.User.Username);

            var age = CalculateAge(birthday);
            var nextBirthday = GetNextBirthday(birthday);
            var daysUntil = (nextBirthday - DateTime.Now).Days;

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🎂 Birthday Registered!")
                .WithDescription($"**Ahoy {Context.User.Mention}!**\n\n" +
                               $"🏴‍☠️ Your birthday has been registered: **{birthday:dd.MM.yyyy}**")
                .AddField("🎯 Current Age", $"{age} years", true)
                .AddField("⏰ Next Birthday", $"{nextBirthday:dd.MM.yyyy} ({daysUntil} days)", true)
                .WithFooter("The crew will celebrate with ye!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-birthdayremove")]
        [RequirePirateAdmin]
        public async Task RemoveBirthdayAsync(SocketGuildUser user)
        {
            var birthdayInfo = PirateService.GetBirthday(user.Id);
            if (birthdayInfo == null)
            {
                await ReplyAsync($"❌ {user.Mention} doesn't have a birthday registered, matey!");
                return;
            }

            PirateService.RemoveBirthday(user.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("🗑️ Birthday Removed")
                .WithDescription($"**{user.Mention}'s birthday has been removed from the ship's records!**")
                .WithFooter("They can register again with !pirate-birthdayset")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-mybirthdayremove")]
        public async Task RemoveMyBirthdayAsync()
        {
            var birthday = PirateService.GetBirthday(Context.User.Id);
            if (birthday == null)
            {
                await ReplyAsync("❌ Ye don't have a birthday registered, matey!");
                return;
            }

            PirateService.RemoveBirthday(Context.User.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("🗑️ Your Birthday Removed")
                .WithDescription("**Your birthday has been removed from the ship's records!**\n\n" +
                               "Ye can register again anytime with `!pirate-birthdayset`")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-mybirthdayinfo")]
        public async Task MyBirthdayInfoAsync()
        {
            var birthdayInfo = PirateService.GetBirthday(Context.User.Id);
            if (birthdayInfo == null)
            {
                await ReplyAsync("❌ Ye don't have a birthday registered! Use `!pirate-birthdayset DD.MM.YYYY` to register.");
                return;
            }

            var age = CalculateAge(birthdayInfo.BirthdayDate);
            var nextBirthday = GetNextBirthday(birthdayInfo.BirthdayDate);
            var daysUntil = (nextBirthday - DateTime.Now).Days;

            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle("🎂 Your Birthday Info")
                .WithDescription($"**Ahoy {Context.User.Mention}!**")
                .AddField("📅 Birthday", birthdayInfo.BirthdayDate.ToString("dd.MM.yyyy"), true)
                .AddField("🎯 Current Age", $"{age} years", true)
                .AddField("⏰ Next Birthday", $"{nextBirthday:dd.MM.yyyy}", true)
                .AddField("⏳ Days Until", $"{daysUntil} days", true)
                .WithFooter("The crew is ready to celebrate!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-birthdaylist")]
        public async Task BirthdayListAsync(int page = 1)
        {
            var allBirthdays = PirateService.GetAllBirthdays();
            var guildMembers = Context.Guild.Users.ToDictionary(u => u.Id, u => u);
            
            // Filter to only include current guild members
            var guildBirthdays = allBirthdays
                .Where(kvp => guildMembers.ContainsKey(kvp.Key))
                .Select(kvp => new
                {
                    UserId = kvp.Key,
                    Birthday = kvp.Value,
                    Member = guildMembers[kvp.Key],
                    NextBirthday = GetNextBirthday(kvp.Value.BirthdayDate),
                    Age = CalculateAge(kvp.Value.BirthdayDate)
                })
                .OrderBy(x => x.NextBirthday.DayOfYear)
                .ToList();

            if (!guildBirthdays.Any())
            {
                await ReplyAsync("❌ No birthdays registered on this ship yet, matey!");
                return;
            }

            const int itemsPerPage = 10;
            var totalPages = (int)Math.Ceiling((double)guildBirthdays.Count / itemsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var startIndex = (page - 1) * itemsPerPage;
            var pageItems = guildBirthdays.Skip(startIndex).Take(itemsPerPage).ToList();

            var description = new StringBuilder();
            description.AppendLine("**🏴‍☠️ Upcoming Crew Birthdays:**\n");

            foreach (var item in pageItems)
            {
                var daysUntil = (item.NextBirthday - DateTime.Now).Days;
                var ageText = item.Age > 0 ? $"(turning {item.Age + 1})" : "";
                
                description.AppendLine($"🎂 **{item.Member.DisplayName}** - {item.NextBirthday:dd.MM} {ageText}");
                description.AppendLine($"    ⏰ {(daysUntil == 0 ? "**TODAY!** 🎉" : daysUntil == 1 ? "**Tomorrow!**" : $"in {daysUntil} days")}");
                description.AppendLine();
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🎂 Pirate Crew Birthdays")
                .WithDescription(description.ToString())
                .WithFooter($"Page {page}/{totalPages} • Total: {guildBirthdays.Count} birthdays")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pirate-todaysbirthdays")]
        public async Task TodaysBirthdaysAsync()
        {
            var allBirthdays = PirateService.GetAllBirthdays();
            var guildMembers = Context.Guild.Users.ToDictionary(u => u.Id, u => u);
            var today = DateTime.Now;

            var todaysBirthdays = allBirthdays
                .Where(kvp => guildMembers.ContainsKey(kvp.Key))
                .Where(kvp => kvp.Value.BirthdayDate.Month == today.Month && kvp.Value.BirthdayDate.Day == today.Day)
                .Select(kvp => new
                {
                    Member = guildMembers[kvp.Key],
                    Birthday = kvp.Value.BirthdayDate,
                    Age = CalculateAge(kvp.Value.BirthdayDate)
                })
                .ToList();

            if (!todaysBirthdays.Any())
            {
                await ReplyAsync("🎂 No birthdays today on this ship, matey!");
                return;
            }

            var description = new StringBuilder();
            description.AppendLine("**🎉 Today's Birthday Celebrations! 🎉**\n");

            foreach (var birthday in todaysBirthdays)
            {
                description.AppendLine($"🏴‍☠️ **Happy Birthday {birthday.Member.DisplayName}!** 🎂");
                description.AppendLine($"🎯 Turning **{birthday.Age}** years old!");
                description.AppendLine();
            }

            description.AppendLine("**🎊 The entire crew wishes ye a fantastic day! 🎊**");

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🎂 Today's Birthdays!")
                .WithDescription(description.ToString())
                .WithThumbnailUrl("https://cdn.discordapp.com/emojis/🎂.png")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // Helper methods
        private static int CalculateAge(DateTime birthday)
        {
            var today = DateTime.Today;
            var age = today.Year - birthday.Year;
            if (birthday.Date > today.AddYears(-age)) age--;
            return Math.Max(0, age);
        }

        private static DateTime GetNextBirthday(DateTime birthday)
        {
            var today = DateTime.Today;
            var thisYear = new DateTime(today.Year, birthday.Month, birthday.Day);
            
            if (thisYear >= today)
                return thisYear;
            else
                return thisYear.AddYears(1);
        }

        // Static method for birthday service integration
        public static async Task CheckAndAnnounceBirthdays(DiscordSocketClient client)
        {
            try
            {
                var allBirthdays = PirateService.GetAllBirthdays();
                var today = DateTime.Now;

                foreach (var guild in client.Guilds)
                {
                    var birthdayChannelId = PirateService.GetBirthdayChannel(guild.Id);
                    if (!birthdayChannelId.HasValue) continue;

                    var channel = guild.GetTextChannel(birthdayChannelId.Value);
                    if (channel == null) continue;

                    var todaysBirthdays = allBirthdays
                        .Where(kvp => kvp.Value.BirthdayDate.Month == today.Month && kvp.Value.BirthdayDate.Day == today.Day)
                        .Select(kvp => guild.GetUser(kvp.Key))
                        .Where(user => user != null)
                        .ToList();

                    foreach (var user in todaysBirthdays)
                    {
                        var age = CalculateAge(allBirthdays[user.Id].BirthdayDate);
                        
                        var embed = new EmbedBuilder()
                            .WithColor(Color.Gold)
                            .WithTitle("🎉 Happy Birthday! 🎉")
                            .WithDescription($"**🏴‍☠️ Ahoy {user.Mention}!**\n\n" +
                                           $"🎂 Happy **{age}th** Birthday, ye magnificent pirate!\n" +
                                           $"🎊 The entire crew wishes ye fair winds and following seas!\n\n" +
                                           $"**May your treasures be plenty and your adventures endless! ⚔️**")
                            .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                            .WithCurrentTimestamp()
                            .Build();

                        await channel.SendMessageAsync($"🎂 {user.Mention}", embed: embed);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error checking birthdays: {ex.Message}");
            }
        }
    }
}