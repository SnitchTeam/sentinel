using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Oxide.Plugins;
using Xunit;
using SentinelPlugin = Oxide.Plugins.Sentinel;

namespace Sentinel.Tests
{
    [Collection("WebServerSequential")]
    public class SentinelWebApiRoutingTests : IDisposable
    {
        private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private const string TestAuthToken = "test-token-api-12345";

        public void Dispose() => _client.Dispose();

        private HttpRequestMessage AuthReq(HttpMethod method, string url)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestAuthToken);
            return req;
        }

        private static SentinelWebServer CreateServer(ISentinelWebApi api)
        {
            var logger = new TestRuntimeBridge();
            var listener = new HttpListenerWrapper();
            var server = new SentinelWebServer(listener, logger, TestAuthToken);
            server.SetApi(api);
            return server;
        }

        [Fact]
        public async Task Api_Players_Get_Returns200_WithSchema()
        {
            var port = 31200;
            var mock = new MockSentinelApi();
            mock.OnlinePlayers.Add(new OnlinePlayerDto
            {
                SteamId = "76561190000000001",
                Name = "TestPlayer",
                Ip = "1.2.3.4",
                Ping = 45,
                ConnectedSince = DateTime.UtcNow.ToString("O"),
                ViolationScore = 0
            });

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/players"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.EnumerateArray().ToList();
                Assert.Single(arr);
                Assert.Equal("76561190000000001", arr[0].GetProperty("steamId").GetString());
                Assert.Equal("TestPlayer", arr[0].GetProperty("name").GetString());
                Assert.True(arr[0].TryGetProperty("violationScore", out _));
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Players_KickOnline_Returns204()
        {
            var port = 31201;
            var mock = new MockSentinelApi();
            mock.PlayerActionResult = new PlayerActionResult { Success = true };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/players/76561190000000001/kick"));
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Players_KickOffline_Returns404()
        {
            var port = 31202;
            var mock = new MockSentinelApi();
            mock.PlayerActionResult = new PlayerActionResult { Success = false, NotFound = true };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/players/76561190000000001/kick"));
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Players_WarnMuteFreeze_Returns204()
        {
            var port = 31203;
            var mock = new MockSentinelApi();
            mock.PlayerActionResult = new PlayerActionResult { Success = true };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                foreach (var action in new[] { "warn", "mute", "freeze" })
                {
                    var resp = await _client.SendAsync(AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/players/76561190000000001/{action}"));
                    Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
                }
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Bans_Get_Paginated_DefaultLimit50()
        {
            var port = 31204;
            var mock = new MockSentinelApi();
            mock.BansResult = new PaginatedResult<BanDto>
            {
                Items = new List<BanDto> { new BanDto { Id = 1, SteamId = "s1", Reason = "test", BannedBy = "admin", CreatedAt = 1, Active = true } },
                Total = 1,
                Page = 1,
                Limit = 50
            };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/bans"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                Assert.Equal(1, doc.RootElement.GetProperty("page").GetInt32());
                Assert.Equal(50, doc.RootElement.GetProperty("limit").GetInt32());
                Assert.True(doc.RootElement.TryGetProperty("items", out _));
                Assert.True(doc.RootElement.TryGetProperty("total", out _));
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Bans_Get_MaxLimit200()
        {
            var port = 31205;
            var mock = new MockSentinelApi();
            mock.BansResult = new PaginatedResult<BanDto> { Items = new List<BanDto>(), Total = 0, Page = 1, Limit = 200 };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/bans?limit=500"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                // The limit should be clamped by the server before calling the API
                Assert.True(mock.LastBansLimit <= 200);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Bans_Post_CreatesBan_Returns201()
        {
            var port = 31206;
            var mock = new MockSentinelApi();
            mock.CreateBanResult = new BanDto { Id = 1, SteamId = "s1", Reason = "cheating", BannedBy = "Web API", CreatedAt = 1, Active = true };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/bans");
                req.Content = new StringContent("{\"steamId\":\"s1\",\"reason\":\"cheating\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("\"id\":1", json);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Bans_Delete_Revokes_Returns204()
        {
            var port = 31207;
            var mock = new MockSentinelApi();
            mock.RevokeBanResult = true;

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Delete, $"http://127.0.0.1:{port}/api/bans/1"));
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Bans_Delete_NotFound_Returns404()
        {
            var port = 31208;
            var mock = new MockSentinelApi();
            mock.RevokeBanResult = false;

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Delete, $"http://127.0.0.1:{port}/api/bans/999"));
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Actions_Get_ReturnsPaginated_WithTypeAndSinceFilters()
        {
            var port = 31209;
            var mock = new MockSentinelApi();
            mock.ActionsResult = new PaginatedResult<AuditDto>
            {
                Items = new List<AuditDto> { new AuditDto { Id = 1, ActionType = "kick", Actor = "a1", Timestamp = 1 } },
                Total = 1,
                Page = 1,
                Limit = 50
            };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/actions?type=kick&since=0"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("kick", mock.LastActionsType);
                Assert.NotNull(mock.LastActionsSince);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_AiLog_Get_ReturnsPaginated()
        {
            var port = 31210;
            var mock = new MockSentinelApi();
            mock.AiLogResult = new PaginatedResult<AiLogDto> { Items = new List<AiLogDto>(), Total = 0, Page = 1, Limit = 50 };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/ai/log"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_AiFeedback_Post_Returns204()
        {
            var port = 31211;
            var mock = new MockSentinelApi();
            mock.RecordAiFeedbackResult = true;

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/ai/feedback");
                req.Content = new StringContent("{\"id\":1,\"verdict\":\"accept\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_AiConfig_Get_RedactsApiKey()
        {
            var port = 31212;
            var mock = new MockSentinelApi();
            mock.AiConfigResult = new AiConfigDto { Provider = "openai", ApiKey = "***" };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/ai/config"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("***", json);
                Assert.DoesNotContain("sk-", json);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_AiQuery_Post_Returns200()
        {
            var port = 31213;
            var mock = new MockSentinelApi();
            mock.AiQueryResult = new SearchAgentResult { Success = true, Sql = "SELECT 1" };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/ai/query");
                req.Content = new StringContent("{\"query\":\"show bans\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Config_Get_RedactsAuthToken()
        {
            var port = 31214;
            var mock = new MockSentinelApi();
            mock.ConfigResult = new Dictionary<string, object>
            {
                ["webPanel"] = new Dictionary<string, object> { ["authToken"] = "***", ["port"] = 31002 },
                ["discord"] = new Dictionary<string, object> { ["webhooks"] = new Dictionary<string, string> { ["ban"] = "https://discord.com" } }
            };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/config"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("***", json);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Config_Post_Valid_Returns204()
        {
            var port = 31215;
            var mock = new MockSentinelApi();
            mock.ConfigUpdateResult = new ConfigUpdateResult { Success = true };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/config");
                req.Content = new StringContent("{\"bans\":{\"defaultDurationMinutes\":60}}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Config_Post_UnknownKey_Returns400()
        {
            var port = 31216;
            var mock = new MockSentinelApi();
            mock.ConfigUpdateResult = new ConfigUpdateResult { Success = false, Error = "Unknown config key: foo" };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/config");
                req.Content = new StringContent("{\"foo\":\"bar\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Perms_Get_ReturnsGroups()
        {
            var port = 31217;
            var mock = new MockSentinelApi();
            mock.PermissionGroups = new List<GroupHierarchyDto>
            {
                new GroupHierarchyDto { GroupId = 1, GroupName = "admin", Permissions = new List<string> { "sentinel.*" }, Members = new List<GroupMemberDto>() }
            };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/perms"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("admin", json);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Perms_CreateGroup_Returns201()
        {
            var port = 31218;
            var mock = new MockSentinelApi();
            mock.CreateGroupResult = (true, 2, "");

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/perms/groups");
                req.Content = new StringContent("{\"name\":\"mods\",\"title\":\"Moderators\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Perms_UpdateGroup_Returns204()
        {
            var port = 31219;
            var mock = new MockSentinelApi();
            mock.UpdateGroupResult = true;

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var req = AuthReq(HttpMethod.Put, $"http://127.0.0.1:{port}/api/perms/groups/1");
                req.Content = new StringContent("{\"title\":\"New Title\"}", Encoding.UTF8, "application/json");
                var resp = await _client.SendAsync(req);
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Perms_DeleteGroup_Returns204()
        {
            var port = 31220;
            var mock = new MockSentinelApi();
            mock.DeleteGroupResult = true;

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Delete, $"http://127.0.0.1:{port}/api/perms/groups/1"));
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Baselines_Get_Returns200()
        {
            var port = 31221;
            var mock = new MockSentinelApi();
            mock.Baselines = new List<BaselineDto> { new BaselineDto { SteamId = "s1", LastUpdated = 1 } };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/baselines"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Baselines_Recalculate_Returns202_WithLocation()
        {
            var port = 31222;
            var mock = new MockSentinelApi();
            mock.BaselineJobId = "job-123";

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Post, $"http://127.0.0.1:{port}/api/baselines/recalculate"));
                Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
                Assert.True(resp.Headers.Contains("Location"));
                Assert.Contains("job-123", resp.Headers.GetValues("Location").First());
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Stats_DefaultDays7_Returns200()
        {
            var port = 31223;
            var mock = new MockSentinelApi();
            mock.StatsResult = new StatsResult { AiQueryVolume = 5, BanRate = 2 };

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/stats"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("aiQueryVolume", json);
                Assert.Contains("banRate", json);
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Api_Stats_Days30_Returns200()
        {
            var port = 31224;
            var mock = new MockSentinelApi();
            mock.StatsResult = new StatsResult();

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/stats?days=30"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(31)]
        public async Task Api_Stats_InvalidDays_Returns400(int days)
        {
            var port = 31225;
            var mock = new MockSentinelApi();

            var server = CreateServer(mock);
            server.Start(port);
            try
            {
                var resp = await _client.SendAsync(AuthReq(HttpMethod.Get, $"http://127.0.0.1:{port}/api/stats?days={days}"));
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            }
            finally { server.Stop(); }
        }

        private class MockSentinelApi : ISentinelWebApi
        {
            public List<OnlinePlayerDto> OnlinePlayers { get; } = new();
            public PlayerActionResult PlayerActionResult { get; set; } = new();
            public PaginatedResult<BanDto> BansResult { get; set; } = new();
            public int LastBansLimit { get; set; }
            public BanDto? CreateBanResult { get; set; }
            public bool RevokeBanResult { get; set; }
            public PaginatedResult<AuditDto> ActionsResult { get; set; } = new();
            public string? LastActionsType { get; set; }
            public long? LastActionsSince { get; set; }
            public PaginatedResult<AiLogDto> AiLogResult { get; set; } = new();
            public bool RecordAiFeedbackResult { get; set; }
            public AiConfigDto AiConfigResult { get; set; } = new();
            public SearchAgentResult AiQueryResult { get; set; } = new();
            public object ConfigResult { get; set; } = new { };
            public ConfigUpdateResult ConfigUpdateResult { get; set; } = new();
            public List<GroupHierarchyDto> PermissionGroups { get; set; } = new();
            public (bool success, int id, string error) CreateGroupResult { get; set; }
            public bool UpdateGroupResult { get; set; }
            public bool DeleteGroupResult { get; set; }
            public List<BaselineDto> Baselines { get; set; } = new();
            public string BaselineJobId { get; set; } = "";
            public StatsResult StatsResult { get; set; } = new();

            public List<OnlinePlayerDto> ApiGetOnlinePlayers() => OnlinePlayers;
            public PlayerActionResult ExecutePlayerAction(string steamId, string action, string? reason, int? durationMinutes) => PlayerActionResult;
            public PaginatedResult<BanDto> GetBans(int page, int limit) { LastBansLimit = limit; return BansResult; }
            public BanDto? CreateBan(string steamId, string? name, string reason, int? durationMinutes) => CreateBanResult;
            public bool RevokeBan(long id) => RevokeBanResult;
            public PaginatedResult<AuditDto> GetActions(string? type, long? since, int page, int limit) { LastActionsType = type; LastActionsSince = since; return ActionsResult; }
            public PaginatedResult<AiLogDto> GetAiLog(int page, int limit) => AiLogResult;
            public bool RecordAiFeedback(long id, string verdict) => RecordAiFeedbackResult;
            public AiConfigDto GetAiConfig() => AiConfigResult;
            public SearchAgentResult QueryAi(string nlQuery) => AiQueryResult;
            public object GetConfig() => ConfigResult;
            public ConfigUpdateResult UpdateConfig(string json) => ConfigUpdateResult;
            public List<GroupHierarchyDto> GetPermissionGroups() => PermissionGroups;
            public (bool success, int id, string error) CreatePermissionGroup(string name, string title, string? parent) => CreateGroupResult;
            public bool UpdatePermissionGroup(int id, string? title, string? parent, List<string>? permissions) => UpdateGroupResult;
            public bool DeletePermissionGroup(int id) => DeleteGroupResult;
            public List<BaselineDto> GetBaselines() => Baselines;
            public string TriggerBaselineRecalculation() => BaselineJobId;
            public StatsResult GetStats(int days) => StatsResult;
        }

        private class TestRuntimeBridge : IRuntimeBridge
        {
            public RuntimeType Runtime => RuntimeType.Oxide;
            public void LogInfo(string message) { }
            public void LogWarning(string message) { }
            public void LogError(string message) { }
        }
    }

    public class SentinelWebApiIntegrationTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly TestableSentinel _plugin;

        public SentinelWebApiIntegrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"sentinel_api_test_{Guid.NewGuid()}.db");
            _plugin = new TestableSentinel();
            _plugin.InitializeDatabase(_dbPath);
        }

        public void Dispose()
        {
            _plugin.CloseDatabase();
            CleanupDbFiles(_dbPath);
        }

        private static void CleanupDbFiles(string dbPath)
        {
            try { File.Delete(dbPath); } catch { }
            try { File.Delete(dbPath + "-shm"); } catch { }
            try { File.Delete(dbPath + "-wal"); } catch { }
        }

        [Fact]
        public void ApiGetOnlinePlayers_ReturnsCorrectSchema()
        {
            var p = new TestPlayer { UserIDString = "76561190000000001", displayName = "Alice" };
            BasePlayer.activePlayerList.Add(p);
            try
            {
                _plugin.OnPlayerConnected(p);
                var list = _plugin.ApiGetOnlinePlayers();
                Assert.Single(list);
                Assert.Equal("76561190000000001", list[0].SteamId);
                Assert.Equal("Alice", list[0].Name);
                Assert.True(list[0].ViolationScore >= 0);
            }
            finally { BasePlayer.activePlayerList.Remove(p); }
        }

        [Fact]
        public void ApiGetBans_Paginated()
        {
            // Insert a ban directly
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sentinel_bans (steam_id, name, banned_by_steam_id, banned_by_name, reason, active, created_at) VALUES (@s, @n, @b, @bn, @r, 1, @c);";
            cmd.Parameters.AddWithValue("@s", "s1");
            cmd.Parameters.AddWithValue("@n", "Player1");
            cmd.Parameters.AddWithValue("@b", "admin");
            cmd.Parameters.AddWithValue("@bn", "Admin");
            cmd.Parameters.AddWithValue("@r", "cheating");
            cmd.Parameters.AddWithValue("@c", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();

            var result = _plugin.GetBans(1, 50);
            Assert.Equal(1, result.Total);
            Assert.Single(result.Items);
            Assert.Equal("s1", result.Items[0].SteamId);
            Assert.Equal("cheating", result.Items[0].Reason);
        }

        [Fact]
        public void ApiCreateBan_InsertsIntoDatabase()
        {
            var ban = _plugin.CreateBan("s2", "Player2", "hacking", 1440);
            Assert.NotNull(ban);
            Assert.True(ban.Id > 0);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sentinel_bans WHERE steam_id = 's2';";
            Assert.Equal(1L, cmd.ExecuteScalar());
        }

        [Fact]
        public void ApiRevokeBan_SetsActiveToZero()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sentinel_bans (steam_id, name, banned_by_steam_id, banned_by_name, reason, active, created_at) VALUES ('s3', 'P', 'a', 'A', 'r', 1, 1);";
            cmd.ExecuteNonQuery();
            long id;
            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            id = (long)idCmd.ExecuteScalar()!;

            Assert.True(_plugin.RevokeBan(id));

            using var check = conn.CreateCommand();
            check.CommandText = "SELECT active FROM sentinel_bans WHERE id = @id;";
            check.Parameters.AddWithValue("@id", id);
            Assert.Equal(0L, check.ExecuteScalar());
        }

        [Fact]
        public void ApiGetActions_FiltersByType()
        {
            _plugin.LogAuditAction("a1", "Admin", "t1", "Target", "kick", "reason", null, true);
            _plugin.LogAuditAction("a1", "Admin", "t1", "Target", "ban", "reason", null, true);

            var result = _plugin.GetActions("kick", null, 1, 50);
            Assert.Equal(1, result.Total);
            Assert.Single(result.Items);
            Assert.Equal("kick", result.Items[0].ActionType);
        }

        [Fact]
        public void ApiGetActions_FiltersBySince()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _plugin.LogAuditAction("a1", "Admin", null, null, "kick", null, null, true);

            var result = _plugin.GetActions(null, now - 10, 1, 50);
            Assert.True(result.Total >= 1);
        }

        [Fact]
        public void ApiGetActions_OrderedByTimestampDesc()
        {
            _plugin.LogAuditAction("a1", "Admin", null, null, "action1", null, null, true);
            System.Threading.Thread.Sleep(50);
            _plugin.LogAuditAction("a1", "Admin", null, null, "action2", null, null, true);

            var result = _plugin.GetActions(null, null, 1, 50);
            Assert.True(result.Items.Count >= 2);
            Assert.True(result.Items[0].Timestamp >= result.Items[1].Timestamp);
        }

        [Fact]
        public void ApiRecordAiFeedback_UpdatesVerdict()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sentinel_ai_log (agent_name, request_id, timestamp) VALUES ('Triage', 'req1', 1);";
            cmd.ExecuteNonQuery();
            long id;
            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            id = (long)idCmd.ExecuteScalar()!;

            Assert.True(_plugin.RecordAiFeedback(id, "accept"));

            using var check = conn.CreateCommand();
            check.CommandText = "SELECT verdict FROM sentinel_ai_log WHERE id = @id;";
            check.Parameters.AddWithValue("@id", id);
            Assert.Equal("accept", check.ExecuteScalar());
        }

        [Fact]
        public void ApiGetAiConfig_RedactsApiKey()
        {
            _plugin.SetConfig(new SentinelConfig { AI = new AIConfig { ApiKey = "secret-key", FallbackApiKey = "fallback-secret" } });
            var cfg = _plugin.GetAiConfig();
            Assert.Equal("***", cfg.ApiKey);
            Assert.Equal("***", cfg.FallbackApiKey);
        }

        [Fact]
        public void ApiGetConfig_RedactsAuthToken()
        {
            _plugin.SetConfig(new SentinelConfig
            {
                WebPanel = new WebPanelConfig { AuthToken = "super-secret" },
                Discord = new DiscordConfig { Webhooks = new Dictionary<string, string> { ["ban"] = "https://hooks.discord.com/abc123" } }
            });
            var cfg = _plugin.GetConfig();
            var dict = (Dictionary<string, JsonElement>)cfg;
            var wp = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dict["webPanel"].GetRawText())!;
            Assert.Equal("***", wp["authToken"].GetString());

            var disc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dict["discord"].GetRawText())!;
            var wh = JsonSerializer.Deserialize<Dictionary<string, string>>(disc["webhooks"].GetRawText())!;
            Assert.Equal("https://hooks.discord.com", wh["ban"]);
        }

        [Fact]
        public void ApiUpdateConfig_RejectsUnknownKeys()
        {
            _plugin.SetConfig(new SentinelConfig());
            var result = _plugin.UpdateConfig("{\"unknownKey\":true}");
            Assert.False(result.Success);
            Assert.Contains("unknown", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ApiUpdateConfig_AcceptsKnownKeys()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sentinel_cfg_{Guid.NewGuid()}");
            var configPath = Path.Combine(dir, "Sentinel.json");
            Directory.CreateDirectory(dir);

            var plugin = new TestableSentinel();
            plugin.Config = new Oxide.Core.Plugins.DynamicConfigFile(configPath);
            plugin.SetConfig(new SentinelConfig());

            var result = plugin.UpdateConfig("{\"bans\":{\"defaultDurationMinutes\":60}}");
            Assert.True(result.Success);
            Assert.Equal(60, plugin.PluginConfig.Bans.DefaultDurationMinutes);

            try { File.Delete(configPath); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }

        [Fact]
        public void ApiPermissionGroups_ReturnsHierarchy()
        {
            _plugin.CreateGroup("test_group", "Test Group", null, out _);
            var groups = _plugin.GetPermissionGroups();
            Assert.Contains(groups, g => g.GroupName == "test_group");
        }

        [Fact]
        public void ApiCreatePermissionGroup_ReturnsId()
        {
            var (success, id, error) = _plugin.CreatePermissionGroup("new_group", "New Group", null);
            Assert.True(success);
            Assert.True(id > 0);
        }

        [Fact]
        public void ApiUpdatePermissionGroup_UpdatesTitle()
        {
            _plugin.CreateGroup("upd_group", "Old Title", null, out _);
            var group = _plugin.GetGroupFromDb("upd_group");
            Assert.NotNull(group);
            Assert.True(_plugin.UpdatePermissionGroup(group.Id, "New Title", null, null));
            var updated = _plugin.GetGroupFromDb("upd_group");
            Assert.Equal("New Title", updated?.Title);
        }

        [Fact]
        public void ApiDeletePermissionGroup_RemovesGroup()
        {
            _plugin.CreateGroup("del_group", "Delete Me", null, out _);
            var group = _plugin.GetGroupFromDb("del_group");
            Assert.NotNull(group);
            Assert.True(_plugin.DeletePermissionGroup(group.Id));
            Assert.Null(_plugin.GetGroupFromDb("del_group"));
        }

        [Fact]
        public void ApiGetBaselines_ReturnsAggregatedData()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sentinel_baselines (steam_id, metric_name, mean, std_dev, sample_count, last_updated)
                VALUES ('s1', 'headshot_ratio', 0.2, 0.05, 100, 1),
                       ('s1', 'shot_interval', 150.0, 20.0, 100, 1);";
            cmd.ExecuteNonQuery();

            var result = _plugin.GetBaselines();
            Assert.Single(result);
            Assert.Equal(2, result[0].Metrics.Count);
            Assert.Equal("s1", result[0].SteamId);
        }

        [Fact]
        public void ApiTriggerBaselineRecalculation_ReturnsJobId()
        {
            var jobId = _plugin.TriggerBaselineRecalculation();
            Assert.False(string.IsNullOrEmpty(jobId));
        }

        [Fact]
        public void ApiGetStats_ReturnsAggregates()
        {
            _plugin.LogAuditAction("a", "A", null, null, "kick", null, null, true);
            _plugin.LogAuditAction("a", "A", null, null, "ban", null, null, true);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO sentinel_ai_log (agent_name, request_id, timestamp) VALUES ('Triage', 'r1', @t);";
            cmd.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();

            var stats = _plugin.GetStats(7);
            Assert.True(stats.ActionCountsByType.ContainsKey("kick"));
            Assert.True(stats.ActionCountsByType.ContainsKey("ban"));
            Assert.True(stats.AiQueryVolume >= 1);
        }

        [Fact]
        public void ApiGetStats_BoundaryDays1And30()
        {
            var stats1 = _plugin.GetStats(1);
            Assert.NotNull(stats1);
            var stats30 = _plugin.GetStats(30);
            Assert.NotNull(stats30);
        }

        private class TestableSentinel : SentinelPlugin
        {
            public override void Puts(string message) { }
            public override void PrintWarning(string message) { }
            public override void PrintError(string message) { }
            public void SetConfig(SentinelConfig config) => PluginConfig = config;
        }

        private class TestPlayer : BasePlayer { }
    }
}
