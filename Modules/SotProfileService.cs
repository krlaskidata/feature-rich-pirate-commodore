using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PiratBotCSharp.Modules
{
    /// <summary>
    /// Stores the link between a Discord user and their Xbox gamertag / Sea of Thieves profile.
    /// </summary>
    public class SotUserProfile
    {
        public ulong DiscordUserId { get; set; }
        public string XboxGamertag { get; set; } = "";
        public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── Raw SoT API response models ────────────────────────────────────────────

    public class SotApiProfile
    {
        [JsonPropertyName("piratePublicProfile")]
        public SotPublicProfile? PiratePublicProfile { get; set; }
    }

    public class SotPublicProfile
    {
        [JsonPropertyName("gamertag")]
        public string? Gamertag { get; set; }

        [JsonPropertyName("reputations")]
        public List<SotReputation>? Reputations { get; set; }

        [JsonPropertyName("gold")]
        public long Gold { get; set; }

        [JsonPropertyName("pirateLevel")]
        public int PirateLevel { get; set; }

        [JsonPropertyName("isPirateLegend")]
        public bool IsPirateLegend { get; set; }

        [JsonPropertyName("titles")]
        public List<SotTitle>? Titles { get; set; }
    }

    public class SotReputation
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("rank")]
        public string? Rank { get; set; }
    }

    public class SotTitle
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("isUnlocked")]
        public bool IsUnlocked { get; set; }
    }

    /// <summary>
    /// Responsible for loading/saving linked profiles and fetching SoT stats.
    /// </summary>
    public static class SotProfileService
    {
        private const string PROFILES_FILE = "sot_profiles.json";
        private static Dictionary<ulong, SotUserProfile> _profiles = LoadProfiles();

        // ─── Persist helpers ────────────────────────────────────────────────────

        private static Dictionary<ulong, SotUserProfile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(PROFILES_FILE)) return new Dictionary<ulong, SotUserProfile>();
                var json = File.ReadAllText(PROFILES_FILE);
                return JsonSerializer.Deserialize<Dictionary<ulong, SotUserProfile>>(json)
                       ?? new Dictionary<ulong, SotUserProfile>();
            }
            catch { return new Dictionary<ulong, SotUserProfile>(); }
        }

        private static void SaveProfiles()
        {
            try
            {
                var json = JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PROFILES_FILE, json);
            }
            catch { /* non-critical */ }
        }

        // ─── Profile management ─────────────────────────────────────────────────

        public static void LinkProfile(ulong discordUserId, string xboxGamertag)
        {
            _profiles[discordUserId] = new SotUserProfile
            {
                DiscordUserId = discordUserId,
                XboxGamertag = xboxGamertag.Trim(),
                LinkedAt = DateTime.UtcNow
            };
            SaveProfiles();
        }

        public static SotUserProfile? GetProfile(ulong discordUserId)
        {
            return _profiles.TryGetValue(discordUserId, out var p) ? p : null;
        }

        public static void RemoveProfile(ulong discordUserId)
        {
            _profiles.Remove(discordUserId);
            SaveProfiles();
        }

        // ─── SoT API fetch ──────────────────────────────────────────────────────

        /// <summary>
        /// Fetches public Sea of Thieves stats for a gamertag using the
        /// unofficial seaofthieves.com public profile endpoint.
        /// No authentication required – only publicly visible data.
        /// Returns null if the profile was not found or the request failed.
        /// </summary>
        public static async Task<SotPublicProfile?> FetchPublicProfileAsync(string xboxGamertag)
        {
            // The public endpoint on seaofthieves.com – this is an unofficial,
            // community-discovered endpoint. It only returns data that is
            // publicly visible on the in-game profile (no bank gold, private data).
            // URL-encode the gamertag (spaces → %20, # kept or encoded as %23).
            var encoded = Uri.EscapeDataString(xboxGamertag);
            var url = $"https://www.seaofthieves.com/api/profilev2/public?gamertag={encoded}";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (compatible; PiratBot/1.0)");
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                var response = await httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var api = JsonSerializer.Deserialize<SotApiProfile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return api?.PiratePublicProfile;
            }
            catch
            {
                return null;
            }
        }
    }
}
