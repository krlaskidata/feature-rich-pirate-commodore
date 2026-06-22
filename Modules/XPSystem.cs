using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PiratBotCSharp.Modules;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratbotCSharp.Modules
{
    public class XPSystemCommands : ModuleBase<SocketCommandContext>
    {
        private readonly XPService _xpService;
        
        public XPSystemCommands(XPService xpService)
        {
            _xpService = xpService;
        }

        [Command("run-xp-setup")]
        [RequirePirateAdmin]
        public async Task RunXPSetup()
        {
            await ReplyAsync("🏴‍☠️ **Piraten-XP-System wird eingerichtet...**\nErstelle alle Piraten-Rollen im Server, Arr! ⚔️");
            
            var success = await _xpService.SetupXPSystem(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Piraten-XP-System erfolgreich eingerichtet!")
                    .WithDescription("**Das Piraten-XP-System ist jetzt aktiv, Arr!** ⚔️\n\n" +
                        "🏴‍☠️ **Alle Piraten-Rollen wurden im Server erstellt:**\n" +
                        "• 12 Piraten-Level-Rollen (Schiffsjunge -> Herrscher der Weltmeere)\n" +
                        "• 5 Tavernen-Chat-Rollen\n" +
                        "• 5 Deck-Voice-Rollen\n\n" +
                        "⚓ **Berechtigungen:**\n" +
                        "• Alle Rollen haben Standard-Berechtigungen\n" +
                        "• Kapitane konnen Berechtigungen selbst anpassen\n\n" +
                        "💰 **Piraten Features:**\n" +
                        "• Sammle XP durch Chat & Voice wie ein echter Pirat\n" +
                        "• Automatische Rollen-Vergabe bei Rang-Aufstieg\n" +
                        "• `?xp` Command zum Piraten-Status checken\n\n" +
                        "🗑️ **System uber Bord werfen:** `?remove-xp-setup`\n\n" +
                        "🏴‍☠️ **Mochten Sie ein Piraten-Rolemanagement-Embed erstellen?**\n" +
                        "Verwenden Sie: `?create-rolemanagement-embed <#channel>`")
                    .WithColor(0x8B0000) // Dunkelrot fur Piraten
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("💀 **Fehler beim Einrichten des Piraten-XP-Systems!**\nEinige Rollen konnten nicht erstellt werden, Arr!");
            }
        }

        [Command("remove-xp-setup")]
        [RequirePirateAdmin]
        public async Task RemoveXPSetup()
        {
            await ReplyAsync("💀 **Piraten-XP-System wird uber Bord geworfen...**");
            
            var success = await _xpService.RemoveXPSystem(Context.Guild);
            
            if (success)
            {
                var embed = new EmbedBuilder()
                    .WithTitle("💀 Piraten-XP-System uber Bord geworfen!")
                    .WithDescription("**Das Piraten-XP-System wurde komplett entfernt, Arr!** 🏴‍☠️\n\n" +
                        "💀 **Geloschte Rollen:**\n" +
                        "• Alle Piraten-Level-Rollen entfernt\n" +
                        "• Alle Tavernen-Chat-Rollen entfernt\n" +
                        "• Alle Deck-Voice-Rollen entfernt\n\n" +
                        "💰 **XP-Schatz:**\n" +
                        "• Alle gespeicherten XP-Schatze bleiben erhalten\n" +
                        "• Bei erneutem `?run-xp-setup` werden Schatze wiederhergestellt\n\n" +
                        "🏴‍☠️ **System reaktivieren:** `?run-xp-setup`")
                    .WithColor(Color.Orange)
                    .WithCurrentTimestamp();
                    
                await ReplyAsync(embed: embed.Build());
            }
            else
            {
                await ReplyAsync("💀 **Fehler beim Entfernen des Piraten-XP-Systems!**\nEinige Rollen sind zu hartnackig, Arr!");
            }
        }

        [Command("xp")]
        public async Task CheckXP(SocketUser? user = null)
        {
            user = user ?? Context.User;
            var userData = await _xpService.GetUserData(Context.Guild.Id, user.Id);
            
            var guildUser = user as SocketGuildUser ?? Context.Guild.GetUser(user.Id);
            var displayName = guildUser?.DisplayName ?? user.Username;
            
            var embed = new EmbedBuilder()
                .WithTitle("📊 Your XP-Stats")
                .AddField("XP/ CHAT", userData.MessageXP, true)
                .AddField("XP/ VOICE", userData.VoiceXP, true)
                .AddField("YOUR XP", userData.TotalXP, true)
                .AddField("SENDET", userData.MessageCount, true)
                .AddField("VOICE CALL H", $"{userData.VoiceMinutes / 60:F1}h", true)
                .AddField("YOUR LEVEL", userData.Level, true)
                .WithDescription("Chat and come in voice calls to get more xp's to become a Mgb Legend!\nGo to the corrent rolemanagement channel for more information.")
                .WithColor(0x228B22)
                .WithThumbnailUrl(user.GetAvatarUrl())
                .WithCurrentTimestamp();
                
            await ReplyAsync(embed: embed.Build());
        }

        [Command("xp-debug")]
        [RequirePirateAdmin]
        public async Task DebugXPRoles()
        {
            var existingRoles = Context.Guild.Roles.Where(r => 
                _xpService.IsXPRole(r.Name)).ToList();
            
            // Check bot permissions
            var botUser = Context.Guild.GetUser(Context.Client.CurrentUser.Id);
            var hasManageRoles = botUser.GuildPermissions.ManageRoles;
            var botRole = Context.Guild.Roles.Where(r => r.Members.Contains(botUser))
                .OrderByDescending(r => r.Position).FirstOrDefault();
                
            var embed = new EmbedBuilder()
                .WithTitle("🔍 Piraten-XP System Debug")
                .WithDescription($"**Bot Permissions Check:**\n" +
                    $"• Manage Roles: {(hasManageRoles ? "✅ YES" : "❌ NO")}\n" +
                    $"• Bot Role: {botRole?.Name ?? "NONE"} (Position: {botRole?.Position ?? 0})\n\n" +
                    $"**Gefundene XP-Rollen:** {existingRoles.Count}\n\n" +
                    string.Join("\n", existingRoles.Select(r => $"• {r.Name} (ID: {r.Id}, Pos: {r.Position})")))
                .WithColor(hasManageRoles ? Color.Green : Color.Red)
                .WithFooter("Wenn 'Manage Roles' = NO ist, kann der Bot keine Rollen zuweisen!");
                
            await ReplyAsync(embed: embed.Build());
        }

        [Command("create-rolemanagement-embed")]
        [RequirePirateAdmin]
        public async Task CreateRolemanagementEmbed(ITextChannel channel)
        {
            var success = await _xpService.SendRolemanagementEmbed(channel);
            if (success)
            {
                await ReplyAsync($"🏴‍☠️ **Piraten-Rolemanagement-Embed erfolgreich gesendet!**\nChannel: {channel.Mention}");
            }
            else
            {
                await ReplyAsync("💀 **Fehler beim Senden des Rolemanagement-Embeds!**");
            }
        }
    }

    public class XPService
    {
        private const string XP_DATA_FILE = "Modules/pirate_xp_data.json";
        private Dictionary<string, UserXPData> _userData = new();
        
        // 🏴‍☠️ PIRATEN XP-SYSTEM KONFIGURATION - MEMORY OPTIMIZED!
        // Reduced from 10,000 levels to 100 levels (Max: 10,000 XP instead of 1,000,000)
        // Each level = 100 XP for much better memory efficiency
        private readonly Dictionary<string, (int minLevel, int maxLevel, int minXP, int maxXP)> _levelRoles = new()
        {
            { "⚓ Schiffsjunge", (1, 10, 100, 1000) },          // Levels 1-10
            { "🗡️ Matrose", (11, 20, 1100, 2000) },           // Levels 11-20
            { "🏴‍☠️ Seeräuber", (21, 35, 2100, 3500) },        // Levels 21-35
            { "💀 Freibeuter", (36, 50, 3600, 5000) },         // Levels 36-50
            { "⚔️ Korsar", (51, 65, 5100, 6500) },             // Levels 51-65
            { "🌊 Seewolf", (66, 80, 6600, 8000) },            // Levels 66-80
            { "👑 Kapitän", (81, 95, 8100, 9500) },            // Levels 81-95
            { "🌟 Herrscher der Weltmeere", (96, 100, 9600, 10000) } // Levels 96-100 (MAX)
        };
        
        private readonly Dictionary<string, int> _messageRoles = new()
        {
            { "🦜 Tavernen-Plauderer", 300 },
            { "📜 Geschichtenerzähler", 1000 },
            { "🗣️ Hafenlegende", 2000 },
            { "⚔️ Wortführer", 3000 },
            { "🏴‍☠️ Stimme der Meere", 5000 }
        };
        
        private readonly Dictionary<string, int> _voiceRoles = new()
        {
            { "🎙️ Deckwächter", 50 * 60 }, // 50 Stunden in Minuten
            { "🍻 Rudelführer", 150 * 60 },
            { "🌊 Meeresnomade", 300 * 60 },
            { "💀 Kapitän der Stimmen", 600 * 60 },
            { "🔱 Admiral der Worte", 1000 * 60 }
        };

        public XPService()
        {
            LoadXPData();
        }

        public async Task<bool> SetupXPSystem(SocketGuild guild)
        {
            try
            {
                Console.WriteLine("🏴‍☠️ Setting up Pirate XP System...");
                
                // Erstelle alle 12 Piraten-Level-Rollen im Discord-Server
                Console.WriteLine($"⚔️ Creating {_levelRoles.Count} pirate level roles...");
                foreach (var role in _levelRoles)
                {
                    var roleName = role.Key; // Der Rollen-Name ist jetzt der Key
                    if (guild.Roles.Any(r => r.Name == roleName)) continue;
                    
                    await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: GetRoleColor(role.Value.maxXP), isMentionable: false);
                    Console.WriteLine($"✅ Created pirate level role: {roleName}");
                    await Task.Delay(500); // Rate limit protection
                }
                
                // Erstelle Chat-Rollen im Discord-Server
                Console.WriteLine($"📝 Creating {_messageRoles.Count} message roles...");
                foreach (var roleData in _messageRoles)
                {
                    var roleName = roleData.Key;
                    Console.WriteLine($"🔍 Checking message role: {roleName}");
                    
                    if (guild.Roles.Any(r => r.Name == roleName)) 
                    {
                        Console.WriteLine($"⏭️ Message role already exists: {roleName}");
                        continue;
                    }
                    
                    var createdRole = await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: Color.Blue, isMentionable: false);
                    Console.WriteLine($"✅ Created message role: {roleName} (ID: {createdRole.Id})");
                    await Task.Delay(500);
                }
                
                // Erstelle Voice-Rollen im Discord-Server
                Console.WriteLine($"🎤 Creating {_voiceRoles.Count} voice roles...");
                foreach (var roleData in _voiceRoles)
                {
                    var roleName = roleData.Key;
                    Console.WriteLine($"🔍 Checking voice role: {roleName}");
                    
                    if (guild.Roles.Any(r => r.Name == roleName)) 
                    {
                        Console.WriteLine($"⏭️ Voice role already exists: {roleName}");
                        continue;
                    }
                    
                    var createdRole = await guild.CreateRoleAsync(roleName, permissions: GuildPermissions.None, 
                        color: Color.Purple, isMentionable: false);
                    Console.WriteLine($"✅ Created voice role: {roleName} (ID: {createdRole.Id})");
                    await Task.Delay(500);
                }
                
                Console.WriteLine($"🏴‍☠️ Pirate XP-System setup complete! Created {_levelRoles.Count + _messageRoles.Count + _voiceRoles.Count} roles in server.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Pirate XP Setup error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveXPSystem(SocketGuild guild)
        {
            try
            {
                // Entferne alle XP-Rollen
                foreach (var role in guild.Roles.ToList())
                {
                    if (IsXPRole(role.Name))
                    {
                        try
                        {
                            await role.DeleteAsync();
                            Console.WriteLine($"💀 Pirate role deleted: {role.Name}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Could not delete role {role.Name}: {ex.Message}");
                        }
                    }
                }
                
                Console.WriteLine("💀 Pirate XP-System removed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error removing Pirate XP-System: {ex.Message}");
                return false;
            }
        }

        private async Task CreateRoleIfNotExists(SocketGuild guild, string roleName)
        {
            var existingRole = guild.Roles.FirstOrDefault(r => r.Name == roleName);
            if (existingRole == null)
            {
                try
                {
                    var newRole = await guild.CreateRoleAsync(roleName, null, Color.DarkRed, false, false);
                    Console.WriteLine($"🏴‍☠️ Pirate role created: {roleName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💀 Error creating pirate role {roleName}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"⚓ Pirate role already exists: {roleName}");
            }
        }

        public bool IsXPRole(string roleName)
        {
            return _levelRoles.ContainsKey(roleName) ||
                   _messageRoles.ContainsKey(roleName) ||
                   _voiceRoles.ContainsKey(roleName);
        }

        public async Task<UserXPData> GetUserData(ulong guildId, ulong userId)
        {
            var key = $"{guildId}_{userId}";
            if (!_userData.ContainsKey(key))
            {
                _userData[key] = new UserXPData { GuildId = guildId, UserId = userId };
            }
            
            return _userData[key];
        }

        public async Task<bool> SendRolemanagementEmbed(ITextChannel channel)
        {
            try
            {
                var guild = channel.Guild as SocketGuild;
                if (guild == null) return false;
                
                // Level-Rollen Text (sortiert nach XP) - Neues System mit XP-Bereichen
                var levelRolesText = "";
                foreach (var levelRole in _levelRoles.OrderBy(lr => lr.Value.minXP))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == levelRole.Key);
                    var roleMention = role != null ? role.Mention : levelRole.Key;
                    levelRolesText += $"• {levelRole.Value.minXP:N0} - {levelRole.Value.maxXP:N0} XP → {roleMention}\n";
                }
                
                // Message-Rollen Text (sortiert nach Anzahl)
                var messageRolesText = "";
                foreach (var messageRole in _messageRoles.OrderBy(mr => mr.Value))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == messageRole.Key);
                    var roleMention = role != null ? role.Mention : messageRole.Key;
                    messageRolesText += $"• {messageRole.Value} Nachrichten → {roleMention}\n";
                }
                
                // Voice-Rollen Text (sortiert nach Stunden)
                var voiceRolesText = "";
                foreach (var voiceRole in _voiceRoles.OrderBy(vr => vr.Value))
                {
                    var role = guild.Roles.FirstOrDefault(r => r.Name == voiceRole.Key);
                    var roleMention = role != null ? role.Mention : voiceRole.Key;
                    var hours = voiceRole.Value / 60;
                    voiceRolesText += $"• {hours} Stunden → {roleMention}\n";
                }
                
                // Erstelle das saubere Piraten Rolemanagement Embed
                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ ROLLENMANAGEMENT - PIRATENEDITION")
                    .WithDescription("Nicht alle Schätze glänzen. Manche tragen Titel.\n" +
                        "─────────────────────────────────────\n" +
                        "**⚡ NEUES SYSTEM: Bis zu 1.000.000 XP möglich! ⚡**")
                    .AddField("⚔️ Allgemeine Rollen (XP-System)", 
                        "Diese Rollen erhältst du durch gesammelte XP (Level-Aufstieg):\n\n" + levelRolesText)
                    .AddField("💬 Nachrichten-Rollen (Chat-Aktivität)", 
                        "Diese Rollen bekommst du durch geschriebene Nachrichten:\n\n" + messageRolesText)
                    .AddField("🎧 Sprach-Rollen (Voicecalls)", 
                        "Diese Rollen erhältst du durch Zeit in Voice-Kanälen:\n\n" + voiceRolesText)
                    .WithFooter("─────────────────────────────────────\n" +
                        "⚓ Viel Ruhm, wenig Gesetz und noch weniger Gnade.\n" +
                        "Level auf, sammle Titel und werde zur Legende der Meere.\n\n" +
                        "Deinen Fortschritt siehst du jederzeit mit ?xp.\n" +
                        "Jeder Rang bringt dich näher an den Fluch... oder an den Schatz. 🏴‍☠️")
                    .WithColor(0x8B0000) // Pirate Red
                    .WithCurrentTimestamp();
                
                // Sende das Piraten Embed
                await channel.SendMessageAsync(embed: embed.Build());
                Console.WriteLine($"🏴‍☠️ Pirate rolemanagement embed sent to #{channel.Name}, Arr!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error sending pirate rolemanagement embed: {ex.Message}");
                return false;
            }
        }

        public async Task HandleMessage(SocketUserMessage message)
        {
            if (message.Author.IsBot) return;
            
            var userId = message.Author.Id;
            var guildId = (message.Channel as SocketGuildChannel)?.Guild?.Id ?? 0;
            if (guildId == 0) return;

            var userData = await GetUserData(guildId, userId);
            userData.MessageCount++;
            userData.MessageXP = CalculateMessageXP(userData.MessageCount); // Verwende neue Berechnung
            userData.UpdateTotalXP();
            
            // Level berechnen mit neuem System
            var newLevel = GetLevelFromXP(userData.TotalXP);
            var oldLevel = userData.Level;
            userData.Level = newLevel;
            
            var guildUser = message.Author as SocketGuildUser;
            if (guildUser != null)
            {
                // Update Message-Rollen
                await UpdateMessageRoles(guildUser, userData.MessageCount);
                
                // Update Level-Rollen mit neuem System
                await UpdateLevelRoles(guildUser, newLevel);
                
                // Level-up Nachricht nur bei echtem Level-Up
                if (newLevel > oldLevel && message.Channel is ITextChannel textChannel)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🏴‍☠️ PIRATEN-LEVEL AUFSTIEG!")
                        .WithDescription($"**{message.Author.Mention} hat Level {newLevel} erreicht!**\n" +
                            $"Du bist jetzt ein echter Pirat der Sieben Meere! ⚔️\n" +
                            $"💰 Gesamt-XP: {userData.TotalXP}")
                        .WithColor(0x8B0000)
                        .WithThumbnailUrl(message.Author.GetAvatarUrl())
                        .WithCurrentTimestamp();
                        
                    await textChannel.SendMessageAsync(embed: embed.Build());
                }
            }
            
            SaveXPData();
        }

        public async Task RestoreVoiceSessions(IReadOnlyCollection<SocketGuild> guilds)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var guild in guilds)
            {
                foreach (var channel in guild.VoiceChannels)
                {
                    foreach (var member in channel.Users)
                    {
                        if (member.IsBot) continue;
                        var userData = await GetUserData(guild.Id, member.Id);
                        if (!userData.LastVoiceJoin.HasValue)
                        {
                            userData.LastVoiceJoin = now;
                            Console.WriteLine($"🏴‍☠️ Restored voice session for {member.Username} in {channel.Name}");
                        }
                    }
                }
            }
            SaveXPData();
        }

        public async Task HandleVoiceUpdate(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user.IsBot) return;

            var guildId = before.VoiceChannel?.Guild?.Id ?? after.VoiceChannel?.Guild?.Id ?? 0;
            if (guildId == 0) return;

            var userData = await GetUserData(guildId, user.Id);

            bool joined = before.VoiceChannel == null && after.VoiceChannel != null;
            bool left = before.VoiceChannel != null && after.VoiceChannel == null;

            if (joined)
            {
                userData.LastVoiceJoin = DateTimeOffset.UtcNow;
            }
            else if (left && userData.LastVoiceJoin.HasValue)
            {
                var minutesSpent = (int)(DateTimeOffset.UtcNow - userData.LastVoiceJoin.Value).TotalMinutes;
                userData.LastVoiceJoin = null;

                if (minutesSpent > 0)
                {
                    userData.VoiceMinutes += minutesSpent;
                    userData.VoiceXP = CalculateVoiceXP(userData.VoiceMinutes);
                    userData.UpdateTotalXP();

                    var newLevel = GetLevelFromXP(userData.TotalXP);
                    userData.Level = newLevel;

                    if (user is SocketGuildUser guildUser)
                    {
                        await UpdateVoiceRoles(guildUser, userData.VoiceMinutes);
                        await UpdateLevelRoles(guildUser, newLevel);
                    }
                }
            }

            SaveXPData();
        }

        private async Task UpdateLevelRoles(SocketGuildUser user, int level)
        {
            if (user?.Guild?.Roles == null) return;
            
            // Check bot permissions
            var botGuildUser = user.Guild.GetUser(user.Guild.CurrentUser.Id);
            if (!botGuildUser.GuildPermissions.ManageRoles)
            {
                Console.WriteLine($"💀 Bot lacks ManageRoles permission in {user.Guild.Name}!");
                return;
            }
            
            try
            {
                var userData = await GetUserData(user.Guild.Id, user.Id);
                var userTotalXP = userData.TotalXP;
                
                // Finde die passende Rolle basierend auf XP (neues System)
                string? appropriateRoleName = null;
                foreach (var roleData in _levelRoles)
                {
                    if (userTotalXP >= roleData.Value.minXP && userTotalXP <= roleData.Value.maxXP)
                    {
                        appropriateRoleName = roleData.Key;
                        break;
                    }
                }
                
                // Falls keine Rolle gefunden wurde und User hat XP, nimm die höchste Rolle
                if (appropriateRoleName == null && userTotalXP > 1000000)
                {
                    appropriateRoleName = "🌟 Herrscher der Weltmeere"; // Endrolle
                }
                
                if (!string.IsNullOrEmpty(appropriateRoleName))
                {
                    var roleToAdd = user.Guild.Roles.FirstOrDefault(r => r.Name == appropriateRoleName);
                    if (roleToAdd != null)
                    {
                        // Check if bot role is high enough
                        var botRole = user.Guild.Roles.Where(r => r.Members.Contains(botGuildUser))
                            .OrderByDescending(r => r.Position).FirstOrDefault();
                        
                        if (botRole == null || botRole.Position <= roleToAdd.Position)
                        {
                            Console.WriteLine($"💀 Bot role position too low! Bot: {botRole?.Position ?? 0}, Target: {roleToAdd.Position}");
                            return;
                        }
                        
                        if (!user.Roles.Contains(roleToAdd))
                        {
                            await user.AddRoleAsync(roleToAdd);
                            Console.WriteLine($"🏴‍☠️ Added pirate level role {roleToAdd.Name} to {user.DisplayName} (XP: {userTotalXP})");
                        }
                    }
                }
                
                // Entferne alte Level-Rollen
                var currentLevelRoles = user.Roles.Where(r => _levelRoles.Keys.Contains(r.Name)).ToList();
                foreach (var role in currentLevelRoles)
                {
                    if (role.Name != appropriateRoleName)
                    {
                        await user.RemoveRoleAsync(role);
                        Console.WriteLine($"💀 Removed old pirate level role {role.Name} from {user.DisplayName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error updating pirate level roles for {user?.DisplayName}: {ex.Message}");
                Console.WriteLine($"💀 Stack trace: {ex.StackTrace}");
            }
        }

        private async Task UpdateMessageRoles(SocketGuildUser user, int messageCount)
        {
            if (user?.Guild?.Roles == null) return;
            
            try
            {
                foreach (var messageRole in _messageRoles.OrderByDescending(mr => mr.Value))
                {
                    if (messageCount >= messageRole.Value)
                    {
                        var role = user.Guild.Roles.FirstOrDefault(r => r.Name == messageRole.Key);
                        if (role != null && !user.Roles.Contains(role))
                        {
                            await user.AddRoleAsync(role);
                            Console.WriteLine($"🦜 Added pirate message role {role.Name} to {user.DisplayName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error updating pirate message roles for {user?.DisplayName}: {ex.Message}");
            }
        }

        private async Task UpdateVoiceRoles(SocketGuildUser user, int voiceMinutes)
        {
            if (user?.Guild?.Roles == null) return;
            
            try
            {
                foreach (var voiceRole in _voiceRoles.OrderByDescending(vr => vr.Value))
                {
                    if (voiceMinutes >= voiceRole.Value)
                    {
                        var role = user.Guild.Roles.FirstOrDefault(r => r.Name == voiceRole.Key);
                        if (role != null && !user.Roles.Contains(role))
                        {
                            await user.AddRoleAsync(role);
                            Console.WriteLine($"🎙️ Added pirate voice role {role.Name} to {user.DisplayName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error updating pirate voice roles for {user?.DisplayName}: {ex.Message}");
            }
        }

        private int CalculateLevel(int totalXP)
        {
            // Neues System: Jedes Level = 100 XP
            // Level 0 = 0 XP, Level 1 = 100 XP, Level 2 = 200 XP, etc.
            // Minimum ist Level 1 (nicht Level 0)
            return Math.Max(1, totalXP / 100);
        }

        public int CalculateMessageXP(int messageCount)
        {
            // 5 XP pro Nachricht, plus 20 XP Bonus alle 100 Nachrichten
            return (messageCount * 5) + (messageCount / 100) * 20;
        }

        public int CalculateVoiceXP(int voiceMinutes)
        {
            // 5 XP pro Stunde (60 Minuten), plus 20 XP Bonus alle 10 Stunden
            int hours = voiceMinutes / 60;
            return (hours * 5) + (hours / 10) * 20;
        }

        public int GetLevelFromXP(int xp)
        {
            // Neues System: Jedes Level = 100 XP
            // Level 0 = 0 XP, Level 1 = 100 XP, Level 2 = 200 XP, etc.
            // Minimum ist Level 1 (nicht Level 0)
            return Math.Max(1, xp / 100);
        }

        private Color GetRoleColor(int xp)
        {
            // Pirate-Farben basierend auf XP-Bereichen der 12 Rollen
            return xp switch
            {
                < 83300 => Color.LightGrey,        // Schiffsjunge
                < 166600 => Color.Green,           // Matrose
                < 249900 => Color.Blue,            // Seeräuber
                < 333200 => Color.Purple,          // Freibeuter
                < 416500 => Color.Orange,          // Korsar
                < 499800 => Color.Red,             // Seewolf
                < 583100 => Color.Magenta,         // Navigateur
                < 666400 => Color.Gold,            // Schatzjäger
                < 749700 => Color.DarkBlue,        // Kapitän
                < 833000 => Color.DarkRed,         // Piratenkönig
                < 916300 => Color.DarkMagenta,     // Legende der Sieben Meere
                _ => Color.Gold                    // Herrscher der Weltmeere (Endrolle)
            };
        }

        private int CalculateRequiredXP(int level)
        {
            int totalRequired = 0;
            for (int i = 1; i <= level; i++)
            {
                totalRequired += i * 100;
            }
            return totalRequired;
        }

        private int GetRandomXP(int min, int max)
        {
            var random = new Random();
            return random.Next(min, max + 1);
        }

        private void LoadXPData()
        {
            try
            {
                if (File.Exists(XP_DATA_FILE))
                {
                    var json = File.ReadAllText(XP_DATA_FILE);
                    _userData = JsonSerializer.Deserialize<Dictionary<string, UserXPData>>(json) ?? new();
                    Console.WriteLine($"🏴‍☠️ Loaded {_userData.Count} pirate XP records");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error loading pirate XP data: {ex.Message}");
                _userData = new Dictionary<string, UserXPData>();
            }
        }

        private void SaveXPData()
        {
            try
            {
                var directory = Path.GetDirectoryName(XP_DATA_FILE);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonSerializer.Serialize(_userData, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(XP_DATA_FILE, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💀 Error saving pirate XP data: {ex.Message}");
            }
        }
    }

    public class UserXPData
    {
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public int MessageXP { get; set; } = 0;
        public int VoiceXP { get; set; } = 0;
        public int TotalXP { get; set; } = 0;
        public int Level { get; set; } = 0;
        public int MessageCount { get; set; } = 0;
        public int VoiceMinutes { get; set; } = 0;
        public DateTimeOffset? LastVoiceJoin { get; set; }

        public void UpdateTotalXP()
        {
            TotalXP = MessageXP + VoiceXP;
        }
    }
}