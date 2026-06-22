using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Microsoft.Extensions.DependencyInjection;
using PiratBotCSharp.Modules;

namespace PiratBotCSharp
{
    public class Bot
    {
        private const string BOT_INVITE_LINK = "https://discord.com/oauth2/authorize?client_id=1435258176120229910&permissions=8&integration_type=0&scope=bot";
        
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public Bot()
        {
            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildVoiceStates | GatewayIntents.GuildMembers
            };
            _client = new DiscordSocketClient(config);
            
            var commandConfig = new CommandServiceConfig
            {
                DefaultRunMode = RunMode.Async
            };
            _commands = new CommandService(commandConfig);

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton<PiratbotCSharp.Modules.XPService>()
                .BuildServiceProvider();

            _client.Log += Client_Log;
            _commands.Log += Commands_Log;
            _client.Ready += Client_Ready;
            _client.MessageReceived += HandleMessageAsync;
            _client.InteractionCreated += InteractionCreatedAsync;
            _client.UserVoiceStateUpdated += PiratBotCSharp.Modules.PirateVoiceService.HandleVoiceStateUpdatedAsync;
            _client.UserVoiceStateUpdated += async (user, before, after) => 
            {
                var xpService = _services?.GetService<PiratbotCSharp.Modules.XPService>();
                if (xpService != null)
                {
                    await xpService.HandleVoiceUpdate(user, before, after);
                }
            };
            _client.UserJoined += async (user) =>
            {
                try
                {
                    await PiratBotCSharp.Modules.SecurityService.HandleUserJoinedAsync(user);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🏴‍☠️ Security HandleUserJoined error: {ex.Message}");
                }
            };
            _client.JoinedGuild += OnJoinedGuild;
        }

        public async Task InitializeAsync()
        {
            var token = Environment.GetEnvironmentVariable("PIRATBOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("⚓ FATAL: Missing PIRATBOT_TOKEN - Can't set sail without yer token, captain!");
            }

            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private async Task Client_Ready()
        {
            Console.WriteLine($"🏴‍☠️ Mary the red ready. Logged in as {_client.CurrentUser}");
            await _client.SetActivityAsync(new Game("Sailin' the seven seas"));

            var xpService = _services?.GetService<PiratbotCSharp.Modules.XPService>();
            if (xpService != null)
                await xpService.RestoreVoiceSessions(_client.Guilds);

            Console.WriteLine("🏴‍☠️ Pirate bot fully operational and ready for adventure!");
        }

        private Task Client_Log(LogMessage log)
        {
            Console.WriteLine($"🏴‍☠️ {log}");
            return Task.CompletedTask;
        }

        private Task Commands_Log(LogMessage log)
        {
            Console.WriteLine($"🏴‍☠️ Command: {log}");
            return Task.CompletedTask;
        }

        private async Task HandleMessageAsync(SocketMessage messageParam)
        {
            if (!await MessageProtectionService.TryEnterMessagePipelineAsync())
            {
                Console.WriteLine("🏴‍☠️ Message queue full - anchor overloaded, captain! Dropping message...");
                return;
            }

            try
            {
                try
                {
                    await PiratBotCSharp.Modules.SecurityService.HandleMessageAsync(messageParam);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🏴‍☠️ Security HandleMessage error: {ex.Message}");
                    return;
                }

                if (!(messageParam is SocketUserMessage message) || message.Author.IsBot)
                    return;

                var userId = message.Author.Id;
                if (MessageProtectionService.IsCommandRateLimited(userId))
                {
                    Console.WriteLine($"🏴‍☠️ Rate limit: User {userId} exceeded command quota - throttled!");
                    return;
                }

                try
                {
                    var xpService = _services?.GetService<PiratbotCSharp.Modules.XPService>();
                    if (xpService != null)
                    {
                        await xpService.HandleMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🏴‍☠️ Pirate XPService HandleMessage error: {ex}");
                }

                int argPos = 0;

                if (!(message.HasCharPrefix('?', ref argPos) || 
                      message.HasMentionPrefix(_client.CurrentUser, ref argPos)))
                    return;

                var context = new SocketCommandContext(_client, message);

                var result = await _commands.ExecuteAsync(
                    context: context, 
                    argPos: argPos,
                    services: _services);


                if (!result.IsSuccess && result.Error == CommandError.UnknownCommand)
                {
                    await HandleUnknownCommand(context, message.Content.Substring(argPos));
                }
            }
            finally
            {
                MessageProtectionService.ExitMessagePipeline();
            }
        }

        private async Task HandleUnknownCommand(SocketCommandContext context, string command)
        {
            var commandParts = command.Split(' ');
            var firstWord = commandParts[0].ToLower();

            var corrections = new Dictionary<string, string>
            {
                { "ticket", "pirate-ticket-setup" },
                { "tickets", "pirate-ticket-setup" },
                { "setup-ticket", "pirate-ticket-setup" },
                { "ticketsetup", "pirate-ticket-setup" },
                { "ticket-setup", "pirate-ticket-setup" },
                { "voice", "pirate-voice-setup" },
                { "voicesetup", "pirate-voice-setup" },
                { "setup-voice", "pirate-voice-setup" },
                { "economy", "pirate-balance" },
                { "eco", "pirate-balance" },
                { "daily", "pirate-daily" },
                { "balance", "pirate-balance" },
                { "bal", "pirate-balance" },
                { "money", "pirate-balance" },
                { "work", "pirate-work" },
                { "xp", "xp" },
                { "level", "xp" },
                { "lvl", "xp" },
                { "security", "setsecuritymod" },
                { "sec", "setsecuritymod" },
                { "moderation", "setsecuritymod" },
                { "warn", "warn [userid] [reason]" },
                { "ban", "Use Discord's built-in ban feature" },
                { "kick", "Use Discord's built-in kick feature" },
                { "mute", "Use Discord's timeout feature" },
                { "timeout", "Use Discord's timeout feature" },
                { "birthday", "pirate-birthdayset" },
                { "bday", "pirate-birthdayset" },
                { "birthdayset", "pirate-birthdayset" },
                { "cleanup", "cleanup [number]" },
                { "clear", "cleanup [number]" },
                { "purge", "cleanup [number]" },
                { "delete", "cleanup [number]" },
                { "coinflip", "Coming Soon! Use ?help for available commands" },
                { "coin", "Coming Soon! Use ?help for available commands" },
                { "dice", "Coming Soon! Use ?help for available commands" },
                { "slots", "Coming Soon! Use ?help for available commands" },
                { "game", "Coming Soon! Use ?help for available commands" },
                { "games", "Coming Soon! Use ?help for available commands" }
            };

            if (corrections.ContainsKey(firstWord))
            {
                var suggestion = corrections[firstWord];
                var embed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Command Correction")
                    .WithColor(0xFF6B35)
                    .WithDescription($"**Ahoy matey! Did ye mean:**\n`?{suggestion}`")
                    .AddField("💡 Tip", "Use `?help` to see all available commands, ye scurvy dog!")
                    .AddField("🔍 What ye tried", $"`?{command}`", true)
                    .AddField("✅ Suggested command", $"`?{suggestion}`", true)
                    .WithFooter("Mary the Red • Command Assistant")
                    .WithCurrentTimestamp()
                    .Build();

                await context.Channel.SendMessageAsync(embed: embed);
            }
            else if (firstWord.Length > 2 && (firstWord.Contains("help") || firstWord.Contains("command")))
            {
                var helpEmbed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Need Help, Matey?")
                    .WithColor(0x8B4513)
                    .WithDescription("**Arrr! Use these commands to navigate me ship:**")
                    .AddField("📚 Main Help", "`?help` - Complete command list", false)
                    .AddField("🎯 Interactive Help", "`?piratehelp` - Category-based help", false)
                    .AddField("ℹ️ Bot Info", "`?info` - About Mary the Red", false)
                    .WithFooter("The most feared pirate bot on Discord!")
                    .WithCurrentTimestamp()
                    .Build();

                await context.Channel.SendMessageAsync(embed: helpEmbed);
            }
        }

        private async Task InteractionCreatedAsync(SocketInteraction interaction)
        {
            try
            {
                if (interaction is SocketMessageComponent component)
                {
                    var customId = component.Data.CustomId;
                    
                    // Ticket Setup Interactions
                    if (customId == "pirate_ticket_setup_step")
                    {
                        await HandleTicketSetupStepAsync(component);
                    }
                    // Ticket Creation
                    else if (customId.StartsWith("create_pirate_ticket"))
                    {
                        await HandleCreateTicketAsync(component);
                    }
                    // Ticket Category Selection
                    else if (customId == "pirate_ticket_category")
                    {
                        await HandleTicketCategoryAsync(component);
                    }
                    // Ticket Close Confirmation
                    else if (customId.StartsWith("confirm_close_pirate_ticket"))
                    {
                        await HandleTicketCloseAsync(component);
                    }
                    else if (customId.StartsWith("cancel_close_pirate_ticket"))
                    {
                        await component.RespondAsync("🏴‍☠️ Ticket-Schließung abgebrochen, Kapitän.", ephemeral: true);
                    }
                    // Setup Channel Selection
                    else if (customId == "pirate_setup_channel_select")
                    {
                        await HandleSetupChannelSelectAsync(component);
                    }
                    // Setup Category Selection
                    else if (customId == "pirate_setup_category_select")
                    {
                        await HandleSetupCategorySelectAsync(component);
                    }
                    // Setup Role Selection
                    else if (customId == "pirate_setup_role_select")
                    {
                        await HandleSetupRoleSelectAsync(component);
                    }
                    else if (customId.StartsWith("help_"))
                    {
                        
                        await component.RespondAsync("🏴‍☠️ Nutze `?help` für Hilfe, Matrose!", ephemeral: true);
                    }
                    else if (customId.StartsWith("security_appeal|", StringComparison.Ordinal))
                    {
                        await PiratBotCSharp.Modules.SecurityService.HandleSecurityAppealInteractionAsync(component);
                    }
                    else
                    {
                        
                        await component.RespondAsync("🏴‍☠️ Unbekannte Interaktion, Kapitän.", ephemeral: true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Fehler beim Behandeln der Interaktion: {ex.Message}");
                try
                {
                    if (interaction is SocketMessageComponent comp && !comp.HasResponded)
                    {
                        await comp.RespondAsync("💀 Ein Fehler ist aufgetreten, Matrose! Versuch es nochmal.", ephemeral: true);
                    }
                }
                catch { /* Ignore if we can't respond */ }
            }
        }

        private async Task OnJoinedGuild(SocketGuild guild)
        {
            Console.WriteLine($"🏴‍☠️ Ahoy! Joined new ship: {guild.Name} (ID: {guild.Id}) with {guild.MemberCount} crew members");
            
            try
            {
                var systemChannel = guild.SystemChannel ?? guild.DefaultChannel ?? guild.TextChannels.FirstOrDefault();
                if (systemChannel != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🏴‍☠️ Ahoy, Captain!")
                        .WithDescription("**Mary the Red has joined yer crew!**\\n\\n" +
                            "Thank ye fer invitin' me aboard yer fine vessel!\\n" +
                            "Use `?help` to see all the treasure I can help ye with!\\n\\n" +
                            "**Quick Start Commands:**\\n" +
                            "`?help` - Show all commands\\n" +
                            "`?premium` - Unlock Premium treasures\\n" +
                            "`?ahoy` - Proper pirate greeting\\n\\n" +
                            "*Fair winds and following seas, matey!* ⚓")
                        .WithColor(0x8B0000)
                        .WithThumbnailUrl("https://i.imgur.com/7mkVUuO.png")
                        .WithFooter("Commanded by Captain mungabee", "https://i.imgur.com/7mkVUuO.png")
                        .WithCurrentTimestamp()
                        .Build();
                    
                    await systemChannel.SendMessageAsync(embed: embed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Could not send welcome message: {ex.Message}");
            }
        }

        private static readonly ulong[] BotOwners = {
            1105877268775051316UL,
        };

        public static bool IsBotOwner(ulong userId)
        {
            return BotOwners.Contains(userId);
        }

        // Ticket System Handlers
        private async Task HandleTicketSetupStepAsync(SocketMessageComponent component)
        {
            var selectedValue = component.Data.Values.FirstOrDefault();
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);

            switch (selectedValue)
            {
                case "setup_log_channel":
                    await ShowChannelSelectionAsync(component, "log");
                    break;
                case "setup_category":
                    await ShowCategorySelectionAsync(component);
                    break;
                case "setup_support_roles":
                    await ShowRoleSelectionAsync(component);
                    break;
                case "setup_ticket_message":
                    await CreateTicketMessageAsync(component);
                    break;
                case "show_current_config":
                    await ShowCurrentConfigAsync(component);
                    break;
            }
        }

        private async Task ShowChannelSelectionAsync(SocketMessageComponent component, string type)
        {
            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var channels = guild.TextChannels.Take(20).ToList(); // Limit for select menu

            if (!channels.Any())
            {
                await component.RespondAsync("🏴‍☠️ Keine Textkanäle gefunden, Kapitän!", ephemeral: true);
                return;
            }

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("pirate_setup_channel_select")
                .WithPlaceholder($"🏴‍☠️ Wähle den {(type == "log" ? "Log-Kanal" : "Kanal")}...")
                .WithMaxValues(1);

            foreach (var channel in channels)
            {
                selectMenu.AddOption(
                    $"#{channel.Name}",
                    $"{type}_{channel.Id}",
                    channel.Topic?.Substring(0, Math.Min(channel.Topic.Length, 50)) ?? "Kein Topic"
                );
            }

            var embed = new EmbedBuilder()
                .WithTitle($"🏴‍☠️ {(type == "log" ? "Log-Kanal" : "Kanal")} auswählen")
                .WithDescription($"Wähle den Kanal für {(type == "log" ? "Ticket-Logs" : "das Setup")}")
                .WithColor(Color.Gold)
                .Build();

            await component.RespondAsync(embed: embed, components: new ComponentBuilder()
                .WithSelectMenu(selectMenu).Build(), ephemeral: true);
        }

        private async Task ShowCategorySelectionAsync(SocketMessageComponent component)
        {
            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var categories = guild.CategoryChannels.Take(20).ToList();

            if (!categories.Any())
            {
                await component.RespondAsync("🏴‍☠️ Keine Kategorien gefunden, Kapitän! Erstelle zuerst eine Kategorie.", ephemeral: true);
                return;
            }

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("pirate_setup_category_select")
                .WithPlaceholder("🏴‍☠️ Wähle die Ticket-Kategorie...")
                .WithMaxValues(1);

            foreach (var category in categories)
            {
                selectMenu.AddOption(
                    category.Name,
                    $"category_{category.Id}",
                    $"Kanäle: {category.Channels.Count}"
                );
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Ticket-Kategorie auswählen")
                .WithDescription("Wähle die Kategorie, in der neue Tickets erstellt werden sollen")
                .WithColor(Color.Gold)
                .Build();

            await component.RespondAsync(embed: embed, components: new ComponentBuilder()
                .WithSelectMenu(selectMenu).Build(), ephemeral: true);
        }

        private async Task ShowRoleSelectionAsync(SocketMessageComponent component)
        {
            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var roles = guild.Roles.Where(r => !r.IsEveryone && !r.IsManaged)
                              .OrderByDescending(r => r.Position)
                              .Take(20)
                              .ToList();

            if (!roles.Any())
            {
                await component.RespondAsync("🏴‍☠️ Keine Rollen gefunden, Kapitän!", ephemeral: true);
                return;
            }

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("pirate_setup_role_select")
                .WithPlaceholder("🏴‍☠️ Wähle Support-Rollen...")
                .WithMaxValues(Math.Min(roles.Count, 10));

            foreach (var role in roles)
            {
                selectMenu.AddOption(
                    role.Name,
                    $"role_{role.Id}",
                    $"Mitglieder: {guild.Users.Count(u => u.Roles.Contains(role))}"
                );
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Support-Rollen auswählen")
                .WithDescription("Wähle die Rollen, die Zugriff auf alle Tickets haben sollen")
                .WithColor(Color.Gold)
                .Build();

            await component.RespondAsync(embed: embed, components: new ComponentBuilder()
                .WithSelectMenu(selectMenu).Build(), ephemeral: true);
        }

        private async Task HandleSetupChannelSelectAsync(SocketMessageComponent component)
        {
            var selectedValue = component.Data.Values.FirstOrDefault();
            if (string.IsNullOrEmpty(selectedValue)) return;

            var parts = selectedValue.Split('_');
            if (parts.Length != 2) return;

            var type = parts[0];
            var channelId = ulong.Parse(parts[1]);

            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);

            if (type == "log")
            {
                config.LogChannelId = channelId;
                config.LastSetupAttempt = DateTime.UtcNow;
                SaveTicketConfig(guildId, config);

                await component.RespondAsync($"✅ Log-Kanal wurde auf <#{channelId}> gesetzt, Kapitän!", ephemeral: true);
            }
        }

        private async Task HandleSetupCategorySelectAsync(SocketMessageComponent component)
        {
            var selectedValue = component.Data.Values.FirstOrDefault();
            if (string.IsNullOrEmpty(selectedValue)) return;

            var parts = selectedValue.Split('_');
            if (parts.Length != 2) return;

            var categoryId = ulong.Parse(parts[1]);
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);

            config.TicketCategoryId = categoryId;
            config.LastSetupAttempt = DateTime.UtcNow;
            SaveTicketConfig(guildId, config);

            await component.RespondAsync($"✅ Ticket-Kategorie wurde gesetzt, Kapitän!", ephemeral: true);
        }

        private async Task HandleSetupRoleSelectAsync(SocketMessageComponent component)
        {
            var selectedValues = component.Data.Values;
            if (!selectedValues.Any()) return;

            var roleIds = selectedValues.Where(v => v.StartsWith("role_"))
                                      .Select(v => ulong.Parse(v.Substring(5)))
                                      .ToList();

            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);

            config.SupportRoleIds = roleIds;
            config.LastSetupAttempt = DateTime.UtcNow;
            SaveTicketConfig(guildId, config);

            await component.RespondAsync($"✅ {roleIds.Count} Support-Rolle(n) wurden gesetzt, Kapitän!", ephemeral: true);
        }

        private async Task CreateTicketMessageAsync(SocketMessageComponent component)
        {
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);

            if (config.LogChannelId == 0 || !config.TicketCategoryId.HasValue)
            {
                await component.RespondAsync("🏴‍☠️ Konfiguriere zuerst Log-Kanal und Kategorie, Kapitän!", ephemeral: true);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Piraten-Support Ticket")
                .WithDescription("**Benötigst du Hilfe, Matrose?**\n\n" +
                               "Klicke auf den Button unten, um ein Support-Ticket zu erstellen.\n" +
                               "Cpt. Harper und der Schiffrat wird sich so schnell wie möglich um dein Anliegen kümmern!\n\n" +
                               "**Was passiert als nächstes:**\n" +
                               "• Du wählst eine Kategorie für dein Problem\n" +
                               "• Ein privater Kanal wird für dich erstellt\n" +
                               "• Das Support-Team wird benachrichtigt\n\n" +
                               "*Ahoy und fair winds!* ⚓")            
                .WithColor(0x8B0000)
                .WithFooter("Piraten-Support-System")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            var button = new ComponentBuilder()
                .WithButton("🎫 Ticket erstellen", "create_pirate_ticket", ButtonStyle.Primary)
                .Build();

            var message = await component.Channel.SendMessageAsync(embed: embed, components: button);

            config.TicketMessageChannelId = component.Channel.Id;
            config.TicketMessageId = message.Id;
            SaveTicketConfig(guildId, config);

            await component.RespondAsync("✅ Ticket-Nachricht wurde erstellt, Kapitän!", ephemeral: true);
        }

        private async Task HandleCreateTicketAsync(SocketMessageComponent component)
        {
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);
            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var user = component.User as SocketGuildUser;

            // Check if user already has an open ticket
            var existingTicket = CheckForExistingTicket(guildId, user.Id);
            if (existingTicket.HasValue)
            {
                await component.RespondAsync($"🏴‍☠️ Du hast bereits ein offenes Ticket: <#{existingTicket.Value}>, Matrose!", ephemeral: true);
                return;
            }

            // Category selection
            var categoryMenu = new SelectMenuBuilder()
                .WithCustomId("pirate_ticket_category")
                .WithPlaceholder("🏴‍☠️ Wähle eine Kategorie für dein Ticket...")
                .AddOption("⚓ Allgemeine Fragen", "allgemein", "Allgemeine Fragen und Hilfe")
                .AddOption("🐛 Bug Report", "bug", "Fehler oder Probleme melden")
                .AddOption("💡 Vorschlag", "vorschlag", "Verbesserungsvorschläge")
                .AddOption("⚠️ Beschwerde", "beschwerde", "Beschwerden oder Probleme")
                .AddOption("🤝 Partnership", "partnership", "Partnership-Anfragen")
                .AddOption("❓ Sonstiges", "other", "Andere Anliegen");

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Ticket-Kategorie wählen")
                .WithDescription("Ahoy! Wähle die passende Kategorie für dein Anliegen:")
                .WithColor(Color.Gold)
                .Build();

            await component.RespondAsync(embed: embed, components: new ComponentBuilder()
                .WithSelectMenu(categoryMenu).Build(), ephemeral: true);
        }

        private async Task HandleTicketCategoryAsync(SocketMessageComponent component)
        {
            var category = component.Data.Values.FirstOrDefault();
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);
            var guild = ((SocketGuildChannel)component.Channel).Guild;
            var user = component.User as SocketGuildUser;

            if (!config.TicketCategoryId.HasValue)
            {
                await component.RespondAsync("🏴‍☠️ Ticket-System nicht konfiguriert, Kapitän!", ephemeral: true);
                return;
            }

            try
            {
                // Create ticket channel
                var ticketChannel = await guild.CreateTextChannelAsync($"ticket-{user.Username}", properties =>
                {
                    properties.CategoryId = config.TicketCategoryId;
                    properties.Topic = $"Support-Ticket für {user.Username} - Kategorie: {category}";
                });

                // Set permissions
                await ticketChannel.AddPermissionOverwriteAsync(guild.EveryoneRole, 
                    new OverwritePermissions(viewChannel: PermValue.Deny));
                await ticketChannel.AddPermissionOverwriteAsync(user, 
                    new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));

                // Add support roles
                foreach (var roleId in config.SupportRoleIds)
                {
                    var role = guild.GetRole(roleId);
                    if (role != null)
                    {
                        await ticketChannel.AddPermissionOverwriteAsync(role,
                            new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow));
                    }
                }

                // Save ticket metadata
                var ticketMeta = new PirateTicketMeta
                {
                    UserId = user.Id,
                    Category = category,
                    GuildId = guildId,
                    CreatedAt = DateTime.UtcNow,
                    Username = user.Username
                };
                SaveTicketMeta(guildId, ticketChannel.Id, ticketMeta);

                // Send welcome message
                var categoryTranslations = new Dictionary<string, string>
                {
                    { "allgemein", "⚓ Allgemeine Fragen" },
                    { "bug", "🐛 Bug Report" },
                    { "vorschlag", "💡 Vorschlag" },
                    { "beschwerde", "⚠️ Beschwerde" },
                    { "partnership", "🤝 Partnership" },
                    { "other", "❓ Sonstiges" }
                };

                var welcomeEmbed = new EmbedBuilder()
                    .WithTitle("🏴‍☠️ Ticket erstellt!")
                    .WithDescription($"Ahoy {user.Mention}! Dein Support-Ticket wurde erstellt.\n\n" +
                                   $"**Kategorie:** {categoryTranslations.GetValueOrDefault(category, category)}\n" +
                                   $"**Ticket-ID:** {ticketChannel.Id}\n\n" +
                                   "Beschreibe dein Anliegen und unser Support-Team wird sich bald melden!\n\n" +
                                   "*Um das Ticket zu schließen, verwende `?pirate-ticket-close`*")
                    .WithColor(Color.Green)
                    .WithThumbnailUrl(user.GetAvatarUrl())
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                await ticketChannel.SendMessageAsync(embed: welcomeEmbed);

                // Log to log channel
                if (config.LogChannelId != 0)
                {
                    var logChannel = guild.GetTextChannel(config.LogChannelId);
                    if (logChannel != null)
                    {
                        var logEmbed = new EmbedBuilder()
                            .WithTitle("🎫 Neues Ticket erstellt")
                            .AddField("User", user.Mention, true)
                            .AddField("Kanal", ticketChannel.Mention, true)
                            .AddField("Kategorie", categoryTranslations.GetValueOrDefault(category, category), true)
                            .WithColor(Color.Blue)
                            .WithTimestamp(DateTimeOffset.Now)
                            .Build();

                        await logChannel.SendMessageAsync(embed: logEmbed);
                    }
                }

                await component.RespondAsync($"✅ Ticket erstellt! Gehe zu {ticketChannel.Mention}", ephemeral: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ticket: {ex.Message}");
                await component.RespondAsync("💀 Fehler beim Erstellen des Tickets, Kapitän!", ephemeral: true);
            }
        }

        private async Task HandleTicketCloseAsync(SocketMessageComponent component)
        {
            var channelId = ulong.Parse(component.Data.CustomId.Split('_').Last());
            var channel = ((SocketGuildChannel)component.Channel).Guild.GetTextChannel(channelId);
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;

            if (channel == null)
            {
                await component.RespondAsync("🏴‍☠️ Kanal nicht gefunden, Kapitän!", ephemeral: true);
                return;
            }

            var ticketMeta = LoadTicketMeta(guildId, channelId);
            if (ticketMeta == null)
            {
                await component.RespondAsync("🏴‍☠️ Ticket-Daten nicht gefunden, Kapitän!", ephemeral: true);
                return;
            }

            try
            {
                // Log to log channel before deletion
                var config = LoadTicketConfig(guildId);
                if (config.LogChannelId != 0)
                {
                    var logChannel = ((SocketGuildChannel)component.Channel).Guild.GetTextChannel(config.LogChannelId);
                    if (logChannel != null)
                    {
                        var logEmbed = new EmbedBuilder()
                            .WithTitle("🎫 Ticket geschlossen")
                            .AddField("User", $"<@{ticketMeta.UserId}>", true)
                            .AddField("Geschlossen von", component.User.Mention, true)
                            .AddField("Kategorie", ticketMeta.Category, true)
                            .AddField("Dauer", (DateTime.UtcNow - ticketMeta.CreatedAt).ToString(@"dd\.hh\:mm\:ss"), true)
                            .WithColor(Color.Red)
                            .WithTimestamp(DateTimeOffset.Now)
                            .Build();

                        await logChannel.SendMessageAsync(embed: logEmbed);
                    }
                }

                // Delete ticket metadata
                DeleteTicketMeta(guildId, channelId);

                // Delete channel
                await channel.DeleteAsync();

                await component.RespondAsync("✅ Ticket wurde geschlossen, Kapitän!", ephemeral: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing ticket: {ex.Message}");
                await component.RespondAsync("💀 Fehler beim Schließen des Tickets, Kapitän!", ephemeral: true);
            }
        }

        private async Task ShowCurrentConfigAsync(SocketMessageComponent component)
        {
            var guildId = ((SocketGuildChannel)component.Channel).Guild.Id;
            var config = LoadTicketConfig(guildId);
            var guild = ((SocketGuildChannel)component.Channel).Guild;

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Aktuelle Ticket-Konfiguration")
                .WithColor(Color.Blue);

            var configText = "";
            configText += config.LogChannelId != 0 ? $"✅ Log-Kanal: <#{config.LogChannelId}>\n" : "❌ Log-Kanal: Nicht gesetzt\n";
            configText += config.TicketCategoryId.HasValue ? $"✅ Kategorie: {guild.GetCategoryChannel(config.TicketCategoryId.Value)?.Name ?? "Unbekannt"}\n" : "❌ Kategorie: Nicht gesetzt\n";
            configText += config.SupportRoleIds.Any() ? $"✅ Support-Rollen: {config.SupportRoleIds.Count}\n" : "❌ Support-Rollen: Keine\n";
            configText += config.TicketMessageId.HasValue ? "✅ Ticket-Nachricht: Aktiv\n" : "❌ Ticket-Nachricht: Nicht erstellt\n";

            embed.WithDescription(configText);

            if (config.SupportRoleIds.Any())
            {
                var roleNames = config.SupportRoleIds.Take(5)
                    .Select(id => guild.GetRole(id)?.Name ?? "Gelöschte Rolle");
                embed.AddField("Support-Rollen", string.Join("\n", roleNames.Select(name => $"• {name}")), false);
            }

            await component.RespondAsync(embed: embed.Build(), ephemeral: true);
        }

        // Helper Methods
        private PirateTicketConfig LoadTicketConfig(ulong guildId)
        {
            var configDirectory = "Configs";
            var ticketConfigFile = "pirate_ticket_config.json";
            var filePath = Path.Combine(configDirectory, $"{guildId}_{ticketConfigFile}");
            
            if (!File.Exists(filePath))
                return new PirateTicketConfig();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<PirateTicketConfig>(json) ?? new PirateTicketConfig();
            }
            catch
            {
                return new PirateTicketConfig();
            }
        }

        private void SaveTicketConfig(ulong guildId, PirateTicketConfig config)
        {
            try
            {
                var configDirectory = "Configs";
                var ticketConfigFile = "pirate_ticket_config.json";
                Directory.CreateDirectory(configDirectory);
                var filePath = Path.Combine(configDirectory, $"{guildId}_{ticketConfigFile}");
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ticket config: {ex.Message}");
            }
        }

        private PirateTicketMeta? LoadTicketMeta(ulong guildId, ulong channelId)
        {
            var configDirectory = "Configs";
            var ticketMetaFile = "pirate_ticket_metadata.json";
            var filePath = Path.Combine(configDirectory, $"{guildId}_{channelId}_{ticketMetaFile}");
            
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<PirateTicketMeta>(json);
            }
            catch
            {
                return null;
            }
        }

        private void SaveTicketMeta(ulong guildId, ulong channelId, PirateTicketMeta meta)
        {
            try
            {
                var configDirectory = "Configs";
                var ticketMetaFile = "pirate_ticket_metadata.json";
                Directory.CreateDirectory(configDirectory);
                var filePath = Path.Combine(configDirectory, $"{guildId}_{channelId}_{ticketMetaFile}");
                var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ticket meta: {ex.Message}");
            }
        }

        private void DeleteTicketMeta(ulong guildId, ulong channelId)
        {
            try
            {
                var configDirectory = "Configs";
                var ticketMetaFile = "pirate_ticket_metadata.json";
                var filePath = Path.Combine(configDirectory, $"{guildId}_{channelId}_{ticketMetaFile}");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting ticket meta: {ex.Message}");
            }
        }

        private ulong? CheckForExistingTicket(ulong guildId, ulong userId)
        {
            try
            {
                var configDirectory = "Configs";
                var ticketMetaFile = "pirate_ticket_metadata.json";
                var configDir = new DirectoryInfo(configDirectory);
                
                if (!configDir.Exists) return null;

                foreach (var file in configDir.GetFiles($"{guildId}_*_{ticketMetaFile}"))
                {
                    try
                    {
                        var json = File.ReadAllText(file.FullName);
                        var meta = JsonSerializer.Deserialize<PirateTicketMeta>(json);
                        if (meta?.UserId == userId)
                        {
                            // Extract channel ID from filename
                            var parts = Path.GetFileNameWithoutExtension(file.Name).Split('_');
                            if (parts.Length >= 2 && ulong.TryParse(parts[1], out var channelId))
                            {
                                return channelId;
                            }
                        }
                    }
                    catch { continue; }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}