using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Core.Attributes;


namespace ProConnectionLogsPlugin
{
    [MinimumApiVersion(247)]
    public class ProConnectionLogsPlugin : BasePlugin
    {
        public override string ModuleAuthor => "ZIRA";
        public override string ModuleName => "[Discord] Pro Connection Logs";
        public override string ModuleVersion => "v3.0";
        
        private Config _config = null!;
        private Phrases _phrases = null!;

        private readonly Dictionary<int, PlayerInfo> _playerInfos = new();
        public override void Load(bool hotReload)
        {
            _config = LoadConfig();
            _phrases = LoadPhrases();
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        private struct PlayerInfo
        {
            public string PlayerName;
            public long SteamId64;
            public string AdminStatus;
            public string VipStatus;
            public DateTime ConnectTime;
        }
        [GameEventHandler]
        public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            if (@event.Userid != null && @event.Userid.IsValid)
            {
                try
                {
                    var playerName = @event.Userid.PlayerName;
                    var playerSteamId32 = (int)@event.Userid.SteamID;
                    var playerSteamId64 = (long)@event.Userid.SteamID;
                    var adminStatus = CheckAdminStatus(SteamId32To64(playerSteamId32));
                    var vipStatus = CheckVipStatus(playerSteamId32);

                    Logger.LogInformation($"NAME = {playerName}");
                    Logger.LogInformation($"ID32 = {playerSteamId32}");
                    Logger.LogInformation($"ID64 = {playerSteamId64}");

                    _playerInfos[playerSteamId32] = new PlayerInfo
                    {
                        PlayerName = playerName,
                        SteamId64 = playerSteamId64,
                        AdminStatus = adminStatus,
                        VipStatus = vipStatus,
                        ConnectTime = DateTime.Now
                    };

                    SendToDiscord(_config.ConnectWebhookUrl, _phrases.PlayerConnect, playerName, playerSteamId32, adminStatus, vipStatus);
                }
                catch (Exception ex)
                {
                    Logger.LogInformation($"Error while sending message about player connect {ex}");
                }
            }
            else
            {
                Logger.LogError("Error while player connection");
            }
            return HookResult.Continue;
        }
        public void OnClientDisconnect(int playerSlot)
        {
            var player = Utilities.GetPlayerFromSlot(playerSlot);

            if (player != null)
            {
                var playerSteamId32 = (int)player.SteamID;

                if (_playerInfos.TryGetValue(playerSteamId32, out var playerInfo))
                {
                    try
                    {
                        var playTime = DateTime.Now - playerInfo.ConnectTime;
                        _playerInfos.Remove(playerSteamId32);

                        SendToDiscord(_config.DisconnectWebhookUrl, _phrases.PlayerDisConnect,playerInfo.PlayerName, playerSteamId32, playerInfo.AdminStatus, playerInfo.VipStatus, playTime);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogInformation($"Error while sending message about player disconnect {ex}");
                    }
                }
                else
                {
                    Logger.LogInformation("Error while tring to get information about player connect");
                }
            }
            else
            {
                Logger.LogInformation("Error while player disconnect");
            }
        }
        private string CheckAdminStatus(long sid)
        {
            Logger.LogInformation($"Player id from db - {sid}");
            var builder = new MySqlConnectionStringBuilder
            {
                Server = _config.LKSAdminHost,
                UserID = _config.LKSAdminUser,
                Password = _config.LKSAdminPassword,
                Database = _config.LKSAdmindbName,
            };

            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT g.name
                    FROM iks_admins a
                    JOIN iks_groups g ON a.group_id = g.id
                    WHERE a.sid = @Sid;";
                command.Parameters.AddWithValue("@Sid", sid);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var groupname = reader.GetString("name");
                    Logger.LogInformation("db answer - " + groupname);
                    return groupname;
                }
                else
                {
                    Logger.LogInformation("db answer - No results found");
                    return _phrases.NoAdmin;
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation("db answer " + ex.ToString());
                return $"{ex}";
            }
        }
        private string CheckVipStatus(long sid)
        {
            Logger.LogInformation($"Player id to db- {sid}");
            var builder = new MySqlConnectionStringBuilder
            {
                Server = _config.LKSAdminHost,
                UserID = _config.LKSAdminUser,
                Password = _config.LKSAdminPassword,
                Database = _config.LKSAdmindbName,
            };

            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT `group` FROM `vip_users` WHERE `account_id` = @Sid";
                command.Parameters.AddWithValue("@Sid", sid);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var groupname = reader.GetString("group");
                    Logger.LogInformation("db answer - " + groupname);
                    return groupname;
                }
                else
                {
                    Logger.LogInformation("db answer - No results found");
                    return _phrases.NoVip;
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation("db answer - " + ex.ToString());
                return $"{ex}";
            }
        }
        private async void SendToDiscord(string webhookUrl, string title, string playerName, int playerId, string adminStatus, string vipStatus, TimeSpan? playTime = null)
        {
            Logger.LogInformation("Trying to send message to discord");
            try
            {
                using var httpClient = new HttpClient();
                var playTimeString = playTime.HasValue ? $"**{_phrases.PlayerTime}** {playTime.Value:hh\\:mm\\:ss}\n" : "";
                var payload = JsonSerializer.Serialize(new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = title,
                            description = "Discord Pro Connection Logs",
                            color = 255,
                            fields = new[]
                            {
                                new
                                {
                                    name = _phrases.Player,
                                    value = $"**{_phrases.Name}** {playerName}\n" +
                                            $"**{_phrases.SteamID}** {SteamId32To64(playerId)}\n" +
                                            $"**{_phrases.Admin}** {adminStatus}\n" +
                                            $"**{_phrases.Vip}** {vipStatus}\n" +
                                            playTimeString +
                                            $"**{_phrases.Url}** [{_phrases.UrlClick}](https://steamcommunity.com/profiles/{SteamId32To64(playerId)})\n\n" +

                                            $"**{_phrases.Server}**\n"+
                                            $"**IP:** {_phrases.ServerIp}\n" +
                                            $"**Name:** {_phrases.ServerName}\n",
                                    inline = false,
                                }
                            }
                        }
                    }
                });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                await httpClient.PostAsync(webhookUrl, content);
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString());
                throw;
            }
        }
        private static long SteamId32To64(int steamId)
        {
            return steamId | 76561197960265728L;
        }
        private static int SteamId64To32(long steamId)
        {
            return (int)((steamId << 32) >> 32);
        }
        private Config LoadConfig()
        {
            var configPath = Path.Combine(ModuleDirectory, "settings.json");
            if (!File.Exists(configPath)) return CreateConfig(configPath);

            return JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;
        }
        private Config CreateConfig(string configPath)
        {
            var config = new Config
            {
                ConnectWebhookUrl = "",
                DisconnectWebhookUrl = "",
                LKSAdminHost = "",
                LKSAdminUser = "",
                LKSAdminPassword = "",
                LKSAdmindbName = "",
                PisexVipHost = "",
                PisexVipUser = "",
                PisexVipdbName = "",
                PisexVipPassword = "",
            };
            try
            {
                File.WriteAllText(configPath,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("[ReportSystem] Config was successfully saved: " + configPath);
                Console.ResetColor();

            }
            catch (Exception ex) 
            {
                Logger.LogError($"{ex}");
            }
            return config;
        }
        private Phrases LoadPhrases()
        {
            var phrasesPath = Path.Combine(ModuleDirectory, "phrases.json");
            if (!File.Exists(phrasesPath)) return CreatePhrases(phrasesPath);

            return JsonSerializer.Deserialize<Phrases>(File.ReadAllText(phrasesPath))!;
        }
        private Phrases CreatePhrases(string phrasesPath)
        {
            var phrases = new Phrases
            {
                Player = "",
                PlayerConnect = "",
                PlayerDisConnect = "",
                Name = "",
                SteamID = "",
                Admin = "",
                NoAdmin = "",
                Vip = "",
                NoVip = "",
                Url = "",
                UrlClick = "",
                PlayerTime = "",
                Server = "",
                ServerIp = "",
                ServerName = "",
            };
            try
            {
                File.WriteAllText(phrasesPath,
                    JsonSerializer.Serialize(phrases, new JsonSerializerOptions { WriteIndented = true }));

                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("[ReportSystem] Phrases was successfully saved: " + phrasesPath);
                Console.ResetColor();

            }
            catch (Exception ex)
            {
                Logger.LogError($"{ex}");
            }
            return phrases;
        }
        public class Phrases
        {
            public required string Player { get; set; }
            public required string PlayerConnect { get; set; }
            public required string PlayerDisConnect { get; set; }
            public required string Name { get; set; }
            public required string SteamID { get; set; }
            public required string Admin { get; set; }
            public required string NoAdmin { get; set; }
            public required string Vip{ get; set; }
            public required string NoVip { get; set; }
            public required string Url { get; set; }
            public required string UrlClick { get; set; }
            public required string PlayerTime { get; set; }
            public required string Server { get; set; }
            public required string ServerIp {  get; set; }
            public required string ServerName { get; set; }
        }
        public class Config
        {
            public required string ConnectWebhookUrl { get; set; }
            public required string DisconnectWebhookUrl { get; set; }
            public required string LKSAdminHost { get; set; }
            public required string LKSAdminUser { get; set; }
            public required string LKSAdminPassword { get; set; }
            public required string LKSAdmindbName { get; set; }
            public required string PisexVipHost { get; set; }
            public required string PisexVipUser { get; set; }
            public required string PisexVipPassword { get; set; }
            public required string PisexVipdbName { get; set; }
        }
    }
}
