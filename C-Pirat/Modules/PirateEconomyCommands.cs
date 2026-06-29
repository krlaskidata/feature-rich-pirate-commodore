using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public class PirateEconomyCommands : ModuleBase<SocketCommandContext>
    {
        // Economy Role System - 10 Rollen mit progressiven Gold-Requirements
        private readonly Dictionary<string, (int minGold, int maxGold, Color color)> _economyRoles = new()
        {
            { "Cabin Boy 🪣", (0, 1499, Color.Green) },
            { "Deckhand ⚓", (1500, 3499, Color.Green) },
            { "Sailor ⛵", (3500, 6999, Color.Blue) },
            { "Corsair 🗡️", (7000, 12999, Color.Blue) },
            { "Veteran Pirate 💀", (13000, 22999, Color.Purple) },
            { "Quartermaster 📜", (23000, 37999, Color.Purple) },
            { "First Mate 🧭", (38000, 59999, Color.Gold) },
            { "Captain 🏴‍☠️", (60000, 94999, Color.Gold) },
            { "Admiral 🌊", (95000, 149999, 0x2C2C34) }, // Dunkelgrau/Schwarz
            { "Pirate King 👑", (150000, int.MaxValue, 0x2C2C34) } // Dunkelgrau/Schwarz
        };

        [Command("economy-setup")]
        [RequirePirateAdmin]
        public async Task EconomySetup()
        {
            await ReplyAsync("🏴‍☠️ **Piraten-Economy-System wird eingerichtet...**\nErstelle alle Economy-Rollen im Server, Arr! 💰");
            
            var success = await SetupEconomyRoles(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Piraten-Economy-System erfolgreich eingerichtet!")
                    .WithDescription("**Das Piraten-Economy-System ist jetzt aktiv, Arr!** 💰\n\n" +
                        "**Alle Economy-Rollen wurden im Server erstellt:**\n" +
                        "• 10 Piraten-Rang-Rollen (Cabin Boy → Pirate King)\n" +
                        "• Automatische Rollen-Vergabe bei Rang-Aufstieg\n" +
                        "• Progressive Gold-Requirements für echte Piraten\n\n" +
                        "**Berechtigungen:**\n" +
                        "• Alle Rollen haben Standard-Berechtigungen\n" +
                        "• Kapitäne können Berechtigungen selbst anpassen\n\n" +
                        "**Economy Features:**\n" +
                        "• Sammle Gold durch `?pdaily` & `?pwork`\n" +
                        "• Automatische Rollen-Updates bei Rang-Aufstieg\n" +
                        "• `?pprofile` Command zum Status checken\n\n" +
                        "**System über Bord werfen:** `?remove-economy-setup`")
                    .WithColor(Color.Gold)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("❌ **Fehler beim Einrichten des Economy-Systems!**\nBot-Berechtigungen (Rollen verwalten) erforderlich.");
            }
        }

        [Command("remove-economy-setup")]
        [RequirePirateAdmin]
        public async Task RemoveEconomySetup()
        {
            await ReplyAsync("🗑️ **Economy-System wird über Bord geworfen...**");
            
            var success = await RemoveEconomyRoles(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("Economy-System über Bord geworfen!")
                    .WithDescription("**Das Economy-System wurde komplett entfernt, Arr!** 🏴‍☠️\n\n" +
                        "**Gelöschte Rollen:**\n" +
                        "• Alle Economy-Rollen entfernt (10 Stück)\n" +
                        "• Rollen-Automatik deaktiviert\n\n" +
                        "**Gold-Schätze:**\n" +
                        "• Alle gespeicherten Gold-Daten bleiben erhalten\n" +
                        "• Bei erneutem `?economy-setup` werden Rollen wiederhergestellt\n\n" +
                        "**System reaktivieren:** `?economy-setup`")
                    .WithColor(Color.Orange)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("💀 **Fehler beim Entfernen des Economy-Systems!**\nEinige Rollen sind zu hartnäckig, Arr!");
            }
        }
        [Command("pdaily")]
        public async Task DailyAsync()
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            var player = PirateService.GetPlayer(Context.Guild.Id, Context.User.Id);
            var now = DateTime.UtcNow;

            // Check if already claimed today
            if (player.LastDaily.Date == now.Date)
            {
                var nextDaily = player.LastDaily.AddDays(1);
                var timeUntil = nextDaily - now;
                
                await ReplyAsync($"⏰ Ye've already claimed your daily treasure today, matey! " +
                               $"Come back in **{timeUntil.Hours}h {timeUntil.Minutes}m**");
                return;
            }

            // Calculate streak bonus
            var isStreak = (now.Date - player.LastDaily.Date).TotalDays == 1;
            if (!isStreak) player.DailyStreak = 0;
            
            player.DailyStreak++;
            var baseReward = 100;
            var streakBonus = Math.Min(player.DailyStreak * 10, 200); // Max 200 bonus
            var totalReward = baseReward + streakBonus;

            player.Gold += totalReward;
            player.LastDaily = now;
            PirateService.SavePlayer(player);

            // Update economy roles if system is set up
            if (IsEconomySetupDone())
            {
                await UpdatePlayerRoles(Context.Guild, Context.User, player.Gold);
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("💰 Daily Treasure Claimed!")
                .WithDescription($"**Ahoy {Context.User.Mention}!**\n\n" +
                               $"🏴‍☠️ Ye've claimed your daily treasure!")
                .AddField("Base Reward", $"{baseReward} gold", true)
                .AddField("Streak Bonus", $"+{streakBonus} gold", true)
                .AddField("Total Earned", $"**{totalReward} gold**", true)
                .AddField("Current Balance", $"{player.Gold:N0} gold", true)
                .AddField("Streak", $"{player.DailyStreak} days", true)
                .WithFooter("Come back tomorrow for more treasure!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pwork")]
        public async Task WorkAsync()
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            var player = PirateService.GetPlayer(Context.Guild.Id, Context.User.Id);
            var now = DateTime.UtcNow;

            // 2 hour cooldown
            if ((now - player.LastWork).TotalHours < 2)
            {
                var nextWork = player.LastWork.AddHours(2);
                var timeUntil = nextWork - now;
                
                await ReplyAsync($"⏰ Ye need to rest before working again, matey! " +
                               $"Try again in **{timeUntil.Hours}h {timeUntil.Minutes}m**");
                return;
            }

            var jobs = new[]
            {
                ("⚓ Cleaned the ship's deck", 80, 120),
                ("🗺️ Navigated through dangerous waters", 100, 150),
                ("⚔️ Defended the ship from sea monsters", 120, 180),
                ("💎 Found treasure in a hidden cave", 90, 140),
                ("🏴‍☠️ Raided a merchant vessel", 110, 160),
                ("🐟 Caught fish for the crew", 70, 110),
                ("🔧 Repaired the ship's sails", 85, 130),
                ("📜 Deciphered an ancient map", 95, 145)
            };

            var random = new Random();
            var job = jobs[random.Next(jobs.Length)];
            var earnings = random.Next(job.Item2, job.Item3 + 1);

            player.Gold += earnings;
            player.LastWork = now;
            PirateService.SavePlayer(player);

            // Update economy roles if system is set up
            if (IsEconomySetupDone())
            {
                await UpdatePlayerRoles(Context.Guild, Context.User, player.Gold);
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("⚔️ Pirate Work Complete!")
                .WithDescription($"**{Context.User.Mention}**\n\n" +
                               $"🏴‍☠️ {job.Item1}")
                .AddField("💰 Earnings", $"{earnings} gold", true)
                .AddField("💎 New Balance", $"{player.Gold:N0} gold", true)
                .WithFooter("Work again in 2 hours!")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pbalance")]
        [Alias("pbal")]
        public async Task BalanceAsync(SocketUser? user = null)
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            user ??= Context.User;
            var player = PirateService.GetPlayer(Context.Guild.Id, user.Id);

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle($"💰 {(user.Id == Context.User.Id ? "Your" : $"{user.Username}'s")} Pirate Treasury")
                .WithDescription($"**🏴‍☠️ {user.Mention}'s wealth:**")
                .AddField("💎 Gold", $"{player.Gold:N0}", true)
                .AddField("👥 Crew Size", $"{player.CrewSize}", true)
                .AddField("📦 Inventory Items", $"{player.Inventory.Count}", true)
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pprofile")]
        public async Task ProfileAsync(SocketUser? user = null)
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            user ??= Context.User;
            var player = PirateService.GetPlayer(Context.Guild.Id, user.Id);

            var rankData = GetPirateRank(player.Gold);
            var nextRankGold = GetNextRankGold(player.Gold);
            var progressPercent = nextRankGold > 0 ? (double)player.Gold / nextRankGold * 100 : 100;

            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle($"🏴‍☠️ {user.Username}'s Pirate Profile")
                .WithDescription($"**Captain {user.Username}** - *{rankData.rank}*")
                .AddField("💰 Gold", $"{player.Gold:N0}", true)
                .AddField("👥 Crew Size", $"{player.CrewSize}", true)
                .AddField("🏆 Rank", rankData.rank, true)
                .AddField("📈 Progress to Next Rank", 
                    nextRankGold > 0 ? $"{progressPercent:F1}% ({player.Gold:N0}/{nextRankGold:N0})" : "Max Rank!", false)
                .AddField("📦 Inventory", 
                    player.Inventory.Any() ? string.Join(", ", player.Inventory.Take(5).Select(kvp => $"{kvp.Key} x{kvp.Value}")) + 
                    (player.Inventory.Count > 5 ? $" (+{player.Inventory.Count - 5} more)" : "") : "Empty", false)
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("prank")]
        public async Task RankAsync(SocketUser? user = null)
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            user ??= Context.User;
            var player = PirateService.GetPlayer(Context.Guild.Id, user.Id);
            
            // Get guild ranking
            var allPlayers = PirateService.GetAllPlayers(Context.Guild.Id)
                .Select(p => new KeyValuePair<ulong, PiratePlayer>(p.UserId, p))
                .Where(kvp => Context.Guild.GetUser(kvp.Key) != null)
                .OrderByDescending(kvp => kvp.Value.Gold)
                .ToList();

            var userRank = allPlayers.FindIndex(p => p.Key == user.Id) + 1;
            var rankData = GetPirateRank(player.Gold);

            var embed = new EmbedBuilder()
                .WithColor(Color.Purple)
                .WithTitle($"🏆 {user.Username}'s Pirate Ranking")
                .AddField("🏴‍☠️ Pirate Rank", rankData.rank, true)
                .AddField("📊 Server Position", $"#{userRank} of {allPlayers.Count}", true)
                .AddField("💰 Gold", $"{player.Gold:N0}", true)
                .WithDescription($"**{rankData.description}**")
                .WithThumbnailUrl(user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("pleaderboard")]
        [Alias("plb")]
        public async Task LeaderboardAsync(int page = 1)
        {
            // Check if economy setup is done
            if (!IsEconomySetupDone())
            {
                await ReplyAsync("❌ **Economy-System nicht eingerichtet!**\n\n" +
                    "Ein Admin muss zuerst `?economy-setup` ausführen, Arr!\n" +
                    "Dann könnt ihr Gold sammeln wie echte Piraten! 🏴‍☠️💰");
                return;
            }
            
            var allPlayers = PirateService.GetAllPlayers(Context.Guild.Id)
                .Select((p, index) => new { Player = p, UserId = p.UserId, Index = index })
                .Where(item => Context.Guild.GetUser(item.UserId) != null)
                .OrderByDescending(item => item.Player.Gold)
                .ToList();

            if (!allPlayers.Any())
            {
                await ReplyAsync("❌ No pirates found on this ship!");
                return;
            }

            const int playersPerPage = 10;
            var totalPages = (int)Math.Ceiling((double)allPlayers.Count / playersPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var startIndex = (page - 1) * playersPerPage;
            var pageData = allPlayers.Skip(startIndex).Take(playersPerPage).ToList();

            var description = new StringBuilder();
            description.AppendLine("**🏴‍☠️ Richest Pirates on the Ship:**\n");

            for (int i = 0; i < pageData.Count; i++)
            {
                var item = pageData[i];
                var user = Context.Guild.GetUser(item.UserId);
                var position = startIndex + i + 1;
                var medal = position switch
                {
                    1 => "🥇",
                    2 => "🥈", 
                    3 => "🥉",
                    _ => $"**#{position}**"
                };

                var rank = GetPirateRank(item.Player.Gold).rank;
                description.AppendLine($"{medal} **{user?.Username ?? "Unknown"}** - *{rank}*");
                description.AppendLine($"    💰 {item.Player.Gold:N0} gold");
                description.AppendLine();
            }

            var userRank = allPlayers.FindIndex(item => item.UserId == Context.User.Id) + 1;
            if (userRank > 0 && (userRank < startIndex + 1 || userRank > startIndex + playersPerPage))
            {
                description.AppendLine($"📍 **Your position: #{userRank}**");
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithTitle("🏆 Pirate Treasure Leaderboard")
                .WithDescription(description.ToString())
                .WithFooter($"Page {page}/{totalPages} • Total Pirates: {allPlayers.Count}")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        // Helper methods
        private async Task<bool> SetupEconomyRoles(SocketGuild guild)
        {
            try
            {
                Console.WriteLine("🏴‍☠️ Setting up Economy Role System...");
                
                // Erstelle alle 10 Economy-Rollen im Discord-Server
                Console.WriteLine($"💰 Creating {_economyRoles.Count} economy roles...");
                foreach (var role in _economyRoles)
                {
                    var roleName = role.Key;
                    if (guild.Roles.Any(r => r.Name == roleName)) continue;
                    
                    await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: role.Value.color, isMentionable: false);
                    Console.WriteLine($"✅ Created economy role: {roleName}");
                    await Task.Delay(500); // Rate limit protection
                }
                
                Console.WriteLine($"🏴‍☠️ Economy Role System setup complete! Created {_economyRoles.Count} roles in server.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Economy Setup error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RemoveEconomyRoles(SocketGuild guild)
        {
            try
            {
                Console.WriteLine("🗑️ Removing Economy Role System...");
                int deletedCount = 0;
                
                // Lösche alle Economy-Rollen aus dem Discord-Server
                foreach (var roleName in _economyRoles.Keys)
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == roleName);
                    if (role != null)
                    {
                        await role.DeleteAsync();
                        Console.WriteLine($"🗑️ Deleted economy role: {roleName}");
                        deletedCount++;
                        await Task.Delay(500); // Rate limit protection
                    }
                }
                
                Console.WriteLine($"🎉 Economy Role System removal complete! Deleted {deletedCount} roles from server.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Economy Removal error: {ex.Message}");
                return false;
            }
        }

        private async Task UpdatePlayerRoles(SocketGuild guild, SocketUser user, long gold)
        {
            if (user is not SocketGuildUser guildUser) return;

            try
            {
                // Finde die passende Rolle basierend auf Gold
                var targetRoleName = GetEconomyRoleName(gold);
                var targetRole = guild.Roles.FirstOrDefault(r => r.Name == targetRoleName);
                
                if (targetRole != null && !guildUser.Roles.Contains(targetRole))
                {
                    // Remove old economy roles
                    var oldEconomyRoles = guildUser.Roles.Where(r => _economyRoles.ContainsKey(r.Name));
                    foreach (var oldRole in oldEconomyRoles)
                    {
                        await guildUser.RemoveRoleAsync(oldRole);
                    }
                    
                    await guildUser.AddRoleAsync(targetRole);
                    Console.WriteLine($"💰 {guildUser.DisplayName} erreichte Economy-Rang: {targetRoleName}!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error updating economy roles for {guildUser?.DisplayName}: {ex.Message}");
            }
        }

        private string GetEconomyRoleName(long gold)
        {
            return _economyRoles.FirstOrDefault(kvp => gold >= kvp.Value.minGold && gold <= kvp.Value.maxGold).Key 
                   ?? "Cabin Boy 🪣"; // Fallback
        }
        
        private static (string rank, string description) GetPirateRank(long gold)
        {
            return gold switch
            {
                < 1500 => ("Cabin Boy 🪣", "Just starting your pirate journey!"),
                < 3500 => ("Deckhand ⚓", "Learning the ropes of piracy!"),
                < 7000 => ("Sailor ⛵", "A competent member of the crew!"),
                < 13000 => ("Corsair 🗡️", "A skilled pirate warrior!"),
                < 23000 => ("Veteran Pirate 💀", "Experienced in the ways of piracy!"),
                < 38000 => ("Quartermaster 📜", "Keeper of the ship's treasures!"),
                < 60000 => ("First Mate 🧭", "Second in command of the ship!"),
                < 95000 => ("Captain 🎩", "Commander of your own vessel!"),
                < 150000 => ("Admiral 🌊", "Ruler of the seven seas!"),
                _ => ("Pirate King 👑", "The ultimate pirate legend!")
            };
        }

        private static long GetNextRankGold(long currentGold)
        {
            var thresholds = new[] { 1500L, 3500L, 7000L, 13000L, 23000L, 38000L, 60000L, 95000L, 150000L };
            return thresholds.FirstOrDefault(t => t > currentGold);
        }

        // Check if economy system is set up (at least half of the roles exist)
        private bool IsEconomySetupDone()
        {
            var existingRoles = Context.Guild.Roles.Count(r => _economyRoles.ContainsKey(r.Name));
            return existingRoles >= 5; // At least half of the 10 roles should exist
        }
    }
}