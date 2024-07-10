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


namespace ProConnectionLogsPlugin
{
    public class ProConnectionLogsPlugin : BasePlugin
    {
        public override string ModuleAuthor => "ZIRA";
        public override string ModuleName => "[Discord] Pro Connection Logs";
        public override string ModuleVersion => "v2.0";
        
        private Config _config = null!;
        private readonly Dictionary<int, PlayerInfo> _playerInfos = new();

        public override void Load(bool hotReload)
        {
            _config = LoadConfig();
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

                    SendToDiscord(_config.ConnectWebhookUrl, "Игрок подключился.", playerName, playerSteamId32, adminStatus, vipStatus);
                }
                catch (Exception ex)
                {
                    Logger.LogInformation($"Ошибка в отправке сообщении о подключении {ex}");
                }
            }
            else
            {
                Logger.LogInformation("Ошибка при подключении игрока");
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

                        SendToDiscord(_config.DisconnectWebhookUrl, "Игрок отключился.", playerInfo.PlayerName, playerSteamId32, playerInfo.AdminStatus, playerInfo.VipStatus, playTime);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogInformation($"Ошибка в отправке сообщения об отключении {ex}");
                    }
                }
                else
                {
                    Logger.LogInformation("Не удалось определить информацию о подключении игрока");
                }
            }
            else
            {
                Logger.LogInformation("Ошибка при отключении игрока");
            }
        }

        private string CheckAdminStatus(long sid)
        {
            Logger.LogInformation($"Айди игрока для бд - {sid}");
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
                    Logger.LogInformation("Ответ от бд - " + groupname);
                    return groupname;
                }
                else
                {
                    Logger.LogInformation("Ответ от бд - No results found");
                    return "Нет админ привилегии";
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation("Ответ от бд - " + ex.ToString());
                return $"{ex}";
            }
        }

        private string CheckVipStatus(long sid)
        {
            Logger.LogInformation($"айдишник типа для бд - {sid}");
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
                    Logger.LogInformation("Ответ от бд - " + groupname);
                    return groupname;
                }
                else
                {
                    Logger.LogInformation("Ответ от бд - No results found");
                    return "Нет вип привилегии";
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation("Ответ от бд - " + ex.ToString());
                return $"{ex}";
            }
        }

        private async void SendToDiscord(string webhookUrl, string title, string playerName, int playerId, string adminStatus, string vipStatus, TimeSpan? playTime = null)
        {
            Logger.LogInformation("Пытаюсь отправить сообщение в дискорд");
            try
            {
                using var httpClient = new HttpClient();
                var playTimeString = playTime.HasValue ? $"**Наиграное время:** {playTime.Value:hh\\:mm\\:ss}\n" : "";
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
                                    name = "Игрок:",
                                    value = $"**Имя:** {playerName}\n" +
                                            $"**SteamID:** {SteamId32To64(playerId)}\n" +
                                            $"**Админка:** {adminStatus}\n" +
                                            $"**VIP:** {vipStatus}\n" +
                                            playTimeString +
                                            $"**Ссылка:** [кликни, чтобы открыть стим](https://steamcommunity.com/profiles/{SteamId32To64(playerId)})",
                                    inline = false
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
                Console.WriteLine("[ReportSystem] Конфиг успешно сохранён: " + configPath);
                Console.ResetColor();

            }
            catch (Exception ex) 
            {
                Logger.LogError($"{ex}");
            }
            return config;
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
