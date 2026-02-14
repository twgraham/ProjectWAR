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
        // GIVEN an IP address that is not banned
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.1" };
        
        // WHEN checking if the IP is banned
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN the check should return false (IP is allowed)
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeFalse();
    }
    
    [Fact]
    public async Task IsIpBanned_WithBannedIp_ShouldReturnTrue()
    {
        // GIVEN an IP address that has been banned
        var ipAddress = "192.168.1.100";
        // Use 1 for permanent ban
        await _fixture.InsertIpBanAsync(ipAddress, 1);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN checking the ban status of the IP
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN the check should return true (IP is blocked)
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task IsIpBanned_WithExpiredBan_ShouldRemoveBanAndReturnFalse()
    {
        // GIVEN an IP ban that has expired (timestamp in past)
        var ipAddress = "192.168.1.101";
        var expiredTimestamp = TCPManager.GetTimeStamp() - 10000;
        await _fixture.InsertIpBanAsync(ipAddress, expiredTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN checking if the IP is still banned
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN the ban should be cleared and return false
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeFalse();
    }
    
    [Fact]
    public async Task IsIpBanned_WithActiveBan_ShouldReturnTrue()
    {
        // GIVEN an IP ban that is still active (future expiration)
        var ipAddress = "192.168.1.102";
        var futureTimestamp = TCPManager.GetTimeStamp() + 10000;
        await _fixture.InsertIpBanAsync(ipAddress, futureTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // WHEN checking the IP ban status
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN the ban should be enforced and return true
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task IsIpBanned_WithPartialIpMatch_ShouldWork()
    {
        // GIVEN a banned IP subnet (partial IP ban)
        // Ban a subnet
        var bannedSubnet = "192.168.1";
        await _fixture.InsertIpBanAsync(bannedSubnet, 1);
        
        // Test full IP in that subnet
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.50" };
        
        // WHEN checking if an IP in that subnet is banned
        var response = await Client.IsIpBannedAsync(request);
        
        // THEN the ban should match the subnet and return true
        response.ShouldNotBeNull();
        response.IsBanned.ShouldBeTrue();
    }
    
    [Fact]
    public async Task CheckToken_WithValidToken_ShouldSucceed()
    {
        // GIVEN a valid authentication token for a user account
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
        
        // WHEN validating the token
        var response = await Client.CheckTokenAsync(request);
        
        // THEN the validation should succeed and return true
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthSuccess);
    }
    
    [Fact]
    public async Task CheckToken_WithInvalidToken_ShouldFail()
    {
        // GIVEN a user account with an invalid or mismatched token
        var username = "tokenuser2";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new CheckTokenRequest
        {
            Username = username,
            Token = "invalid-token"
        };
        
        // WHEN providing an invalid authentication token
        var response = await Client.CheckTokenAsync(request);
        
        // THEN the validation should fail for security
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthInvalidCredentials);
    }
    
    [Fact]
    public async Task CheckToken_WithNonExistentUser_ShouldFail()
    {
        // GIVEN a token validation request for a user that doesn't exist
        var request = new CheckTokenRequest
        {
            Username = "nonexistent",
            Token = "some-token"
        };
        
        // WHEN attempting to validate token for non-existent user
        var response = await Client.CheckTokenAsync(request);
        
        // THEN the validation should fail to prevent user enumeration
        response.ShouldNotBeNull();
        response.Result.ShouldBe(AuthResult.AuthInvalidCredentials);
    }
    
    [Fact]
    public async Task ModifyAccess_WithExistingAccount_ShouldUpdateLevels()
    {
        // GIVEN a user account with standard access levels
        var username = "accessuser";
        await _fixture.InsertTestAccountAsync(username, "password123", gmLevel: 0);
        
        var request = new ModifyAccessRequest
        {
            Username = username,
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // WHEN updating the GM and core access levels
        var response = await Client.ModifyAccessAsync(request);
        
        // THEN the access levels should be updated successfully
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
        // GIVEN an attempt to modify access for a non-existent account
        var request = new ModifyAccessRequest
        {
            Username = "nonexistent",
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // WHEN attempting the access modification
        var response = await Client.ModifyAccessAsync(request);
        
        // THEN the operation should fail with an error message
        response.ShouldNotBeNull();
        response.Success.ShouldBeFalse();
        response.ErrorMessage.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task BanPlayer_ShouldReturnSuccess()
    {
        // GIVEN a player account that needs to be banned (Note: stub implementation)
        var request = new BanPlayerRequest
        {
            Username = "banuser",
            Reason = "Test ban",
            BanExpiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // WHEN calling the ban player endpoint
        var response = await Client.BanPlayerAsync(request);
        
        // THEN the stub should be invoked successfully (actual ban logic not implemented)
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
    }
    
    [Fact]
    public async Task SanctionPlayer_ShouldReturnSuccess()
    {
        // GIVEN a player that violated terms requiring sanctions (Note: stub implementation)
        var request = new SanctionPlayerRequest
        {
            Username = "sanctionuser",
            SanctionType = "warning",
            Details = "Test sanction",
            Expiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // WHEN calling the sanction player endpoint
        var response = await Client.SanctionPlayerAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Success.ShouldBeTrue();
    }
}
