using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PiratBotCSharp.Modules
{
    public class PirateTicketCommands : ModuleBase<SocketCommandContext>
    {
        private static readonly string ConfigDirectory = "Configs";
        private static readonly string TicketConfigFile = "pirate_ticket_config.json";
        private static readonly string TicketMetaFile = "pirate_ticket_metadata.json";

        private readonly Dictionary<string, string> CategoryTranslations = new Dictionary<string, string>
        {
            { "allgemein", "⚓ Allgemeine Fragen" },
            { "bug", "🐛 Bug Report" },
            { "vorschlag", "💡 Vorschlag" },
            { "beschwerde", "⚠️ Beschwerde" },
            { "partnership", "🤝 Partnership" },
            { "other", "❓ Sonstiges" }
        };

        [Command("pirate-ticket-setup")]
        [RequirePirateAdmin]
        public async Task PirateTicketSetupAsync()
        {
            var guildId = Context.Guild.Id;
            var config = LoadTicketConfig(guildId);
            
            // Embed für Setup-Start
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Piraten-Ticket-System Setup")
                .WithDescription("Ahoy Kapitän! Lass uns dein Ticket-System konfigurieren.\n\nWähle den ersten Schritt:")
                .WithColor(Color.Gold)
                .WithThumbnailUrl("https://cdn.discordapp.com/emojis/1234567890.png")
                .AddField("📋 Was wird konfiguriert:", 
                    "• Log-Kanal für Ticket-Aktionen\n" +
                    "• Kategorie für neue Tickets\n" +
                    "• Support-Rollen\n" +
                    "• Ticket-Erstellungs-Nachricht", true)
                .WithFooter("Piraten-Ticket-System")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("pirate_ticket_setup_step")
                .WithPlaceholder("🏴‍☠️ Wähle einen Setup-Schritt...")
                .AddOption("Log-Kanal konfigurieren", "setup_log_channel", "📝 Kanal für Ticket-Logs festlegen")
                .AddOption("Ticket-Kategorie konfigurieren", "setup_category", "📂 Kategorie für neue Tickets festlegen")
                .AddOption("Support-Rollen konfigurieren", "setup_support_roles", "👥 Rollen mit Ticket-Zugriff festlegen")
                .AddOption("Ticket-Nachricht erstellen", "setup_ticket_message", "📨 Nachricht zum Erstellen von Tickets")
                .AddOption("Aktuelle Konfiguration anzeigen", "show_current_config", "📊 Zeige aktuelle Einstellungen");

            var components = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .Build();

            await ReplyAsync(embed: embed, components: components);
        }

        [Command("pirate-ticket-close")]
        public async Task PirateTicketCloseAsync()
        {
            var channel = Context.Channel as SocketTextChannel;
            var guildId = Context.Guild.Id;

            // Prüfen ob es ein Ticket-Kanal ist
            var ticketMeta = LoadTicketMeta(guildId, channel.Id);
            if (ticketMeta == null)
            {
                await ReplyAsync("🏴‍☠️ Arrr! Das ist kein Ticket-Kanal, Matrose!");
                return;
            }

            // Prüfen ob User das Ticket schließen darf
            var user = Context.User as SocketGuildUser;
            var config = LoadTicketConfig(guildId);
            
            bool canClose = ticketMeta.UserId == user.Id || // Ticket-Ersteller
                           user.GuildPermissions.ManageChannels || // Berechtigung
                           config.SupportRoleIds.Any(roleId => user.Roles.Any(r => r.Id == roleId)); // Support-Rolle

            if (!canClose)
            {
                await ReplyAsync("🏴‍☠️ Du hast keine Berechtigung, dieses Ticket zu schließen, Matrose!");
                return;
            }

            // Bestätigungsnachricht
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Ticket schließen bestätigen")
                .WithDescription($"Arrr! Willst du dieses Ticket wirklich schließen?\n\n" +
                               $"**Ticket von:** <@{ticketMeta.UserId}>\n" +
                               $"**Kategorie:** {ticketMeta.Category}\n" +
                               $"**Erstellt am:** {ticketMeta.CreatedAt:dd.MM.yyyy HH:mm}")
                .WithColor(Color.Orange)
                .Build();

            var buttons = new ComponentBuilder()
                .WithButton("✅ Ja, schließen", $"confirm_close_pirate_ticket_{channel.Id}", ButtonStyle.Danger)
                .WithButton("❌ Abbrechen", $"cancel_close_pirate_ticket_{channel.Id}", ButtonStyle.Secondary)
                .Build();

            await ReplyAsync(embed: embed, components: buttons);
        }

        [Command("pirate-ticket-status")]
        public async Task PirateTicketStatusAsync()
        {
            var guildId = Context.Guild.Id;
            var config = LoadTicketConfig(guildId);
            
            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Piraten-Ticket-System Status")
                .WithColor(Color.Blue)
                .WithThumbnailUrl(Context.Guild.IconUrl);

            // Konfigurationsstatus
            var configStatus = "```diff\n";
            configStatus += config.LogChannelId != 0 ? "+ Log-Kanal konfiguriert\n" : "- Log-Kanal fehlt\n";
            configStatus += config.TicketCategoryId.HasValue ? "+ Ticket-Kategorie konfiguriert\n" : "- Ticket-Kategorie fehlt\n";
            configStatus += config.SupportRoleIds.Any() ? $"+ {config.SupportRoleIds.Count} Support-Rolle(n) konfiguriert\n" : "- Keine Support-Rollen konfiguriert\n";
            configStatus += config.TicketMessageId.HasValue ? "+ Ticket-Nachricht aktiv\n" : "- Keine Ticket-Nachricht\n";
            configStatus += "```";

            embed.AddField("⚙️ Konfigurationsstatus", configStatus, false);

            // Details wenn konfiguriert
            if (config.LogChannelId != 0)
            {
                embed.AddField("📝 Log-Kanal", $"<#{config.LogChannelId}>", true);
            }

            if (config.TicketCategoryId.HasValue)
            {
                var category = Context.Guild.GetCategoryChannel(config.TicketCategoryId.Value);
                embed.AddField("📂 Ticket-Kategorie", category?.Name ?? "Nicht gefunden", true);
            }

            if (config.SupportRoleIds.Any())
            {
                var roleNames = config.SupportRoleIds
                    .Select(id => Context.Guild.GetRole(id)?.Name ?? "Gelöschte Rolle")
                    .Take(5);
                var roleText = string.Join("\n", roleNames.Select(name => $"• {name}"));
                if (config.SupportRoleIds.Count > 5)
                    roleText += $"\n• ... und {config.SupportRoleIds.Count - 5} weitere";
                
                embed.AddField("👥 Support-Rollen", roleText, false);
            }

            // Aktive Tickets zählen
            var activeTickets = CountActiveTickets(guildId);
            embed.AddField("📊 Statistiken", 
                $"🎫 Aktive Tickets: **{activeTickets}**\n" +
                $"📅 Letztes Setup: {(config.LastSetupAttempt?.ToString("dd.MM.yyyy HH:mm") ?? "Nie")}", false);

            // Setup-Tipp wenn nicht vollständig konfiguriert
            bool isFullyConfigured = config.LogChannelId != 0 && 
                                   config.TicketCategoryId.HasValue && 
                                   config.SupportRoleIds.Any();

            if (!isFullyConfigured)
            {
                embed.AddField("💡 Tipp", 
                    "Das System ist noch nicht vollständig konfiguriert!\n" +
                    "Verwende `?pirate-ticket-setup` um die Konfiguration abzuschließen.", false);
            }

            embed.WithFooter("Piraten-Ticket-System", Context.Client.CurrentUser.GetAvatarUrl())
                 .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: embed.Build());
        }

        // Helper Methods
        private PirateTicketConfig LoadTicketConfig(ulong guildId)
        {
            var filePath = Path.Combine(ConfigDirectory, $"{guildId}_{TicketConfigFile}");
            
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
                Directory.CreateDirectory(ConfigDirectory);
                var filePath = Path.Combine(ConfigDirectory, $"{guildId}_{TicketConfigFile}");
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error saving ticket config: {ex.Message}");
            }
        }

        private PirateTicketMeta? LoadTicketMeta(ulong guildId, ulong channelId)
        {
            var filePath = Path.Combine(ConfigDirectory, $"{guildId}_{channelId}_{TicketMetaFile}");
            
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
                Directory.CreateDirectory(ConfigDirectory);
                var filePath = Path.Combine(ConfigDirectory, $"{guildId}_{channelId}_{TicketMetaFile}");
                var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                AtomicFileStore.WriteAllTextAtomic(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🏴‍☠️ Error saving ticket meta: {ex.Message}");
            }
        }

        private int CountActiveTickets(ulong guildId)
        {
            try
            {
                var configDir = new DirectoryInfo(ConfigDirectory);
                if (!configDir.Exists) return 0;

                return configDir.GetFiles($"{guildId}_*_{TicketMetaFile}")
                               .Count();
            }
            catch
            {
                return 0;
            }
        }
    }
}