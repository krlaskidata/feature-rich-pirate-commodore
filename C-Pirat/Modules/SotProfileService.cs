using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;

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
        public long? ReportedXp { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public ulong? SpecialRoleId { get; set; }
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
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.seaofthieves.com/profile/overview");
            return client;
        }

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

        public static bool UpdateStats(ulong discordUserId, long xp)
        {
            if (!_profiles.TryGetValue(discordUserId, out var profile))
                return false;

            profile.ReportedXp = xp;
            profile.LastUpdatedAt = DateTime.UtcNow;
            SaveProfiles();
            return true;
        }

        public static bool SetSpecialRole(ulong discordUserId, ulong roleId)
        {
            if (!_profiles.TryGetValue(discordUserId, out var profile))
                return false;

            profile.SpecialRoleId = roleId;
            SaveProfiles();
            return true;
        }

        // ─── SoT API fetch ──────────────────────────────────────────────────────

        public static async Task<(SotPublicProfile? profile, string reason)> FetchPublicProfileWithReasonAsync(string xboxGamertag)
        {
            var candidates = BuildGamertagCandidates(xboxGamertag);
            var failures = new List<string>();

            foreach (var candidate in candidates)
            {
                var encoded = Uri.EscapeDataString(candidate);
                var url = $"https://www.seaofthieves.com/api/profilev2/public?gamertag={encoded}";

                try
                {
                    var response = await _httpClient.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        failures.Add($"{candidate}: HTTP {(int)response.StatusCode}");
                        continue;
                    }

                    var api = JsonSerializer.Deserialize<SotApiProfile>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (api?.PiratePublicProfile != null)
                    {
                        return (api.PiratePublicProfile, $"OK via '{candidate}'");
                    }

                    failures.Add($"{candidate}: empty profile payload");
                }
                catch (TaskCanceledException)
                {
                    failures.Add($"{candidate}: timeout");
                }
                catch (HttpRequestException ex)
                {
                    failures.Add($"{candidate}: network error ({ex.Message})");
                }
                catch (JsonException)
                {
                    failures.Add($"{candidate}: invalid JSON response");
                }
                catch (Exception ex)
                {
                    failures.Add($"{candidate}: unexpected error ({ex.Message})");
                }
            }

            return (null, failures.Count == 0
                ? "No candidates generated for this gamertag"
                : string.Join(" | ", failures));
        }

        public static async Task<SotPublicProfile?> FetchPublicProfileAsync(string xboxGamertag)
        {
            var result = await FetchPublicProfileWithReasonAsync(xboxGamertag);
            return result.profile;
        }

        private static List<string> BuildGamertagCandidates(string rawGamertag)
        {
            var value = rawGamertag.Trim();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value);
            }

            var hashMatch = Regex.Match(value, @"^(?<name>.+?)#(?<suffix>\d+)$");
            if (hashMatch.Success)
            {
                var baseName = hashMatch.Groups["name"].Value.Trim();
                var suffix = hashMatch.Groups["suffix"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(baseName))
                {
                    set.Add(baseName);
                    set.Add(baseName + suffix);
                }
            }

            return set.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
    }
}
