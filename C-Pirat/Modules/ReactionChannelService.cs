using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public static class ReactionChannelService
    {
        private const string DATA_FILE = "pirate_reaction_channels.json";
        private static Dictionary<ulong, string> _channels = Load();

        private static Dictionary<ulong, string> Load()
        {
            try
            {
                if (!File.Exists(DATA_FILE)) return new Dictionary<ulong, string>();
                var json = File.ReadAllText(DATA_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, string>>(json)
                       ?? new Dictionary<ulong, string>();
            }
            catch { return new Dictionary<ulong, string>(); }
        }

        private static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_channels, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DATA_FILE, json);
            }
            catch { }
        }

        public static void Set(ulong channelId, string emojiRaw)
        {
            _channels[channelId] = emojiRaw;
            Save();
        }

        public static bool Remove(ulong channelId)
        {
            if (!_channels.ContainsKey(channelId)) return false;
            _channels.Remove(channelId);
            Save();
            return true;
        }

        public static string? Get(ulong channelId)
        {
            return _channels.TryGetValue(channelId, out var e) ? e : null;
        }

        public static IReadOnlyDictionary<ulong, string> GetAll() => _channels;

        public static async Task HandleReactionAsync(SocketUserMessage message)
        {
            var emojiRaw = Get(message.Channel.Id);
            if (emojiRaw == null) return;

            try
            {
                IEmote emote;
                if (Emote.TryParse(emojiRaw, out var customEmote))
                    emote = customEmote;
                else
                    emote = new Emoji(emojiRaw);

                await message.AddReactionAsync(emote);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ReactionChannel react error in channel {message.Channel.Id}: {ex.Message}");
            }
        }
    }

    public class ReactionChannelCommands : ModuleBase<SocketCommandContext>
    {
        [Command("reaction-channel")]
        [RequirePirateAdmin]
        [Summary("Set a channel to auto-react to every message with an emoji. (Admin only)")]
        public async Task SetReactionChannelAsync(ulong channelId, [Remainder] string emoji)
        {
            var channel = Context.Guild.GetTextChannel(channelId);
            if (channel == null)
            {
                await ReplyAsync("❌ Channel not found. Make sure the ID is correct.");
                return;
            }

            emoji = emoji.Trim();

            ReactionChannelService.Set(channelId, emoji);

            var embed = new EmbedBuilder()
                .WithTitle("✅ Reaction Channel Set")
                .WithDescription($"I will now react to every message in {channel.Mention} with {emoji}")
                .WithColor(0x8B4513)
                .WithFooter("Barbossa")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("reaction-channel-remove")]
        [RequirePirateAdmin]
        [Summary("Remove auto-reaction from a channel. (Admin only)")]
        public async Task RemoveReactionChannelAsync(ulong channelId)
        {
            var removed = ReactionChannelService.Remove(channelId);
            if (!removed)
            {
                await ReplyAsync("❌ No reaction channel configured for that ID.");
                return;
            }

            var channel = Context.Guild.GetTextChannel(channelId);
            await ReplyAsync($"✅ Auto-reaction removed from {channel?.Mention ?? $"`{channelId}`"}.");
        }

        [Command("reaction-channel-list")]
        [RequirePirateAdmin]
        [Summary("List all active reaction channels. (Admin only)")]
        public async Task ListReactionChannelsAsync()
        {
            var all = ReactionChannelService.GetAll();
            if (all.Count == 0)
            {
                await ReplyAsync("No reaction channels configured.");
                return;
            }

            var lines = new System.Text.StringBuilder();
            foreach (var entry in all)
            {
                var ch = Context.Guild.GetTextChannel(entry.Key);
                lines.AppendLine($"{ch?.Mention ?? $"`{entry.Key}`"} → {entry.Value}");
            }

            var embed = new EmbedBuilder()
                .WithTitle("🏴‍☠️ Active Reaction Channels")
                .WithDescription(lines.ToString())
                .WithColor(0x8B4513)
                .WithFooter("Barbossa")
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: embed);
        }
    }
}
