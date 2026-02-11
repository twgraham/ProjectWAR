using FrameWork;

namespace AccountCacher.Tests;

public class SecurityTests : IClassFixture<AccountCacherFixture>, IAsyncLifetime
{
    private readonly AccountCacherFixture _fixture;
    private AccountMgr.AccountMgrClient Client => _fixture.Client;
    
    public SecurityTests(AccountCacherFixture fixture)
    {
        _fixture = fixture;
    }
    
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    
    public async ValueTask DisposeAsync()
    {
        await _fixture.ClearAccountsAsync();
        await _fixture.ClearRealmsAsync();
        await _fixture.ClearIpBansAsync();
    }
    
    [Fact]
    public async Task IsIpBanned_WithNonBannedIp_ShouldReturnFalse()
    {
        // GIVEN
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.1" };
        
        // WHEN
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeFalse();
    }
    
    [Fact]
    public async Task IsIpBanned_WithBannedIp_ShouldReturnTrue()
    {
        // GIVEN
        var ipAddress = "192.168.1.100";
        // Use 1 for permanent ban
        await _fixture.InsertIpBanAsync(ipAddress, 1);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task IsIpBanned_WithExpiredBan_ShouldRemoveBanAndReturnFalse()
    {
        // GIVEN
        var ipAddress = "192.168.1.101";
        var expiredTimestamp = TCPManager.GetTimeStamp() - 10000;
        await _fixture.InsertIpBanAsync(ipAddress, expiredTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeFalse();
    }
    
    [Fact]
    public async Task IsIpBanned_WithActiveBan_ShouldReturnTrue()
    {
        // GIVEN
        var ipAddress = "192.168.1.102";
        var futureTimestamp = TCPManager.GetTimeStamp() + 10000;
        await _fixture.InsertIpBanAsync(ipAddress, futureTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task IsIpBanned_WithPartialIpMatch_ShouldWork()
    {
        // GIVEN
        // Ban a subnet
        var bannedSubnet = "192.168.1";
        await _fixture.InsertIpBanAsync(bannedSubnet, 1);
        
        // Test full IP in that subnet
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.50" };
        
        // WHEN
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task CheckToken_WithValidToken_ShouldSucceed()
    {
        // GIVEN
        var username = "tokenuser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123");
        
        // Set a token for the account
        var token = Guid.NewGuid().ToString();
        using var connection = new MySql.Data.MySqlClient.MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var updateCmd = new MySql.Data.MySqlClient.MySqlCommand(
            "UPDATE accounts SET Token = @Token WHERE AccountId = @AccountId", connection);
        updateCmd.Parameters.AddWithValue("@Token", token);
        updateCmd.Parameters.AddWithValue("@AccountId", accountId);
        await updateCmd.ExecuteNonQueryAsync();
        
        var request = new CheckTokenRequest
        {
            Username = username,
            Token = token
        };
        
        // WHEN
        var response = await Client.CheckTokenAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthSuccess);
    }
    
    [Fact]
    public async Task CheckToken_WithInvalidToken_ShouldFail()
    {
        // GIVEN
        var username = "tokenuser2";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new CheckTokenRequest
        {
            Username = username,
            Token = "invalid-token"
        };
        
        // WHEN
        var response = await Client.CheckTokenAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthInvalidCredentials);
    }
    
    [Fact]
    public async Task CheckToken_WithNonExistentUser_ShouldFail()
    {
        // GIVEN
        var request = new CheckTokenRequest
        {
            Username = "nonexistent",
            Token = "some-token"
        };
        
        // WHEN
        var response = await Client.CheckTokenAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthInvalidCredentials);
    }
    
    [Fact]
    public async Task ModifyAccess_WithExistingAccount_ShouldUpdateLevels()
    {
        // GIVEN
        var username = "accessuser";
        await _fixture.InsertTestAccountAsync(username, "password123", gmLevel: 0);
        
        var request = new ModifyAccessRequest
        {
            Username = username,
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // WHEN
        var response = await Client.ModifyAccessAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
        
        // Verify the changes
        var getRequest = new GetAccountRequest { Username = username };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        getResponse.Account.ShouldNotBeNull();
        getResponse.Account.GmLevel.ShouldBe(40);
        getResponse.Account.CoreLevel.ShouldBe(10);
    }
    
    [Fact]
    public async Task ModifyAccess_WithNonExistentAccount_ShouldFail()
    {
        // GIVEN
        var request = new ModifyAccessRequest
        {
            Username = "nonexistent",
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // WHEN
        var response = await Client.ModifyAccessAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Success.ShouldBeFalse();
        response.ErrorMessage.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task BanPlayer_ShouldReturnSuccess()
    {
        // GIVEN - Note: BanPlayer is marked as TODO in the code
        var request = new BanPlayerRequest
        {
            Username = "banuser",
            Reason = "Test ban",
            BanExpiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // WHEN
        var response = await Client.BanPlayerAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
    }
    
    [Fact]
    public async Task SanctionPlayer_ShouldReturnSuccess()
    {
        // GIVEN - Note: SanctionPlayer is marked as TODO in the code
        var request = new SanctionPlayerRequest
        {
            Username = "sanctionuser",
            SanctionType = "warning",
            Details = "Test sanction",
            Expiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // WHEN
        var response = await Client.SanctionPlayerAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
    }
}
