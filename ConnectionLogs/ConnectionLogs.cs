using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using MySqlConnector;


namespace HelloWorldPlugin;
public class HelloWorldPlugin : BasePlugin
{
    public override string ModuleAuthor => "ZIRA";
    public override string ModuleName => "[Discord] Pro Connection Logs";
    public override string ModuleVersion => "v0.5";

    private Config _config = null!;

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();
    }
    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo info)
    {
        if (@event.Userid != null && @event.Userid.IsValid)
        {
            var _PlayerName = @event.Userid.PlayerName;
            var _PlayerSteamId32 = ((int)@event.Userid.SteamID);
            var _PlayerSteamId64 = @event.Userid.SteamID;
            var _AdminStatus = CheckAdminStatus(SteamId32To64(_PlayerSteamId32));
            var _VIPStatus = CheckVipStatus(_PlayerSteamId32);
            // var _AdminStatus = "pidoras";
            // var _VIPStatus = "Pidoras po jizni";

            if (@event.Userid.IsValid)
            {
                Logger.LogInformation("NAME = " + _PlayerName);
                Logger.LogInformation("ID32 = " + _PlayerSteamId32);
                Logger.LogInformation("ID64 = " + _PlayerSteamId64);

                SendMessageToDiscord(GetWebhook(), _PlayerName, _PlayerSteamId32, _AdminStatus, _VIPStatus);
            }
            else
            {
                Logger.LogInformation("Error on player connect");
            }
        }
        return HookResult.Continue;
    }

    public string CheckAdminStatus(long sid)
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
            command.CommandText = @"                    
                    SELECT g.name
                    FROM iks_admins a
                    JOIN iks_groups g ON a.group_id = g.id
                    WHERE a.sid = @Sid;";
            command.Parameters.AddWithValue("@Sid", sid);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                string groupname = reader.GetString("name");
                Logger.LogInformation("Ответ от бд - " + groupname);
                return groupname;
            }
            else
            {
                Logger.LogInformation("Ответ от бд - " + "No results found");
                return "Нет админки";
            }
        }
        catch (Exception ex)
        {
            Logger.LogInformation("Ответ от бд - " + ex.ToString());
            return $"{ex}";
        }
    }
    public string CheckVipStatus(long sid)
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
            command.CommandText = @"SELECT `group` FROM `vip_users` WHERE `account_id` = @Sid";
            command.Parameters.AddWithValue("@Sid", sid);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                string groupname = reader.GetString("name");
                Logger.LogInformation("Ответ от бд - " + groupname);
                return groupname;
            }
            else
            {
                Logger.LogInformation("Ответ от бд - " + "No results found");
                return "Нет випки";
            }
        }
        catch (Exception ex)
        {
            Logger.LogInformation("Ответ от бд - " + ex.ToString());
            return $"{ex}";
        }
    }
    public async void SendMessageToDiscord(string WebhookUrl, string PlayerName, int PlayerId, string AdminStatus, string VIPStatus)
    {
        Logger.LogInformation("trying to send message");
        try
        {
            using var httpClient = new HttpClient();
            var payload = JsonSerializer.Serialize(new
            {
                embeds = new[]
                {
                        new
                        {
                            title = "Членикс подключился",
                            description = "IP Сервера - 46.8.220.103:27015",  // Замените на необходимое значение IP
                            color = 255,
                            fields = new[]
                            {
                                new
                                {
                                    name = "Игрок:",
                                    value = 
                                    $"**Имя:** {PlayerName}\n" +
                                    $"**SteamID:** {SteamId32To64(PlayerId)}\n" +
                                    $"**Админка** {AdminStatus}\n" +
                                    $"**VIP** {VIPStatus}\n" +
                                    $"**Ссылка:** [кликни, чтобы открыть стим](https://steamcommunity.com/profiles/{SteamId32To64(PlayerId)})",
                                    inline = false
                                }
                            }
                        }
                    }
            });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(WebhookUrl, content);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            WebhookUrl = "",
            LKSAdminHost = "",
            LKSAdminUser = "",
            LKSAdminPassword = "",
            LKSAdmindbName = "",
            PisexVipHost = "",
            PisexVipUser = "",
            PisexVipdbName = "",
            PisexVipPassword = "",
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("[ReportSystem] The configuration was successfully saved to a file: " + configPath);
        Console.ResetColor();

        return config;
    }
    private Config LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "settings.json");

        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

        return config;
    }
    static long SteamId32To64(int steamId)
    {
        return steamId | 76561197960265728L;
    }
    static int SteamId64To32(long steamId)
    {
        return (int)((steamId << 32) >> 32);
    }
    private string GetWebhook()
    {
        return _config.WebhookUrl;
    }
    public class Config
    {
        public required string WebhookUrl { get; set; }
        public required string LKSAdminHost { get; set; }
        public required string LKSAdminUser { get; set; }
        public required string LKSAdmindbName { get; set; }
        public required string LKSAdminPassword { get; set; }
        public required string PisexVipHost { get; set; }
        public required string PisexVipUser { get; set; }
        public required string PisexVipPassword { get; set; }
        public required string PisexVipdbName { get; set; }
    }
}