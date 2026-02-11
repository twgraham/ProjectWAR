using FrameWork;

namespace AccountCacher.Tests;

public class SecurityTests : AccountCacherTestBase
{
    [Fact]
    public async Task IsIpBanned_WithNonBannedIp_ShouldReturnFalse()
    {
        // Arrange
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.1" };
        
        // Act
        var response = await Client!.IsIpBannedAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsBanned);
    }
    
    [Fact]
    public async Task IsIpBanned_WithBannedIp_ShouldReturnTrue()
    {
        // Arrange
        var ipAddress = "192.168.1.100";
        // Use 1 for permanent ban
        await InsertIpBanAsync(ipAddress, 1);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // Act
        var response = await Client!.IsIpBannedAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsBanned);
    }
    
    [Fact]
    public async Task IsIpBanned_WithExpiredBan_ShouldRemoveBanAndReturnFalse()
    {
        // Arrange
        var ipAddress = "192.168.1.101";
        var expiredTimestamp = TCPManager.GetTimeStamp() - 10000;
        await InsertIpBanAsync(ipAddress, expiredTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // Act
        var response = await Client!.IsIpBannedAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.False(response.IsBanned);
    }
    
    [Fact]
    public async Task IsIpBanned_WithActiveBan_ShouldReturnTrue()
    {
        // Arrange
        var ipAddress = "192.168.1.102";
        var futureTimestamp = TCPManager.GetTimeStamp() + 10000;
        await InsertIpBanAsync(ipAddress, futureTimestamp);
        
        var request = new IsIpBannedRequest { IpAddress = ipAddress };
        
        // Act
        var response = await Client!.IsIpBannedAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsBanned);
    }
    
    [Fact]
    public async Task IsIpBanned_WithPartialIpMatch_ShouldWork()
    {
        // Arrange
        // Ban a subnet
        var bannedSubnet = "192.168.1";
        await InsertIpBanAsync(bannedSubnet, 1);
        
        // Test full IP in that subnet
        var request = new IsIpBannedRequest { IpAddress = "192.168.1.50" };
        
        // Act
        var response = await Client!.IsIpBannedAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsBanned);
    }
    
    [Fact]
    public async Task CheckToken_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var username = "tokenuser";
        var accountId = await InsertTestAccountAsync(username, "password123");
        
        // Set a token for the account
        var token = Guid.NewGuid().ToString();
        using var connection = new MySql.Data.MySqlClient.MySqlConnection(ConnectionString);
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
        
        // Act
        var response = await Client!.CheckTokenAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(AuthResult.AuthSuccess, response.Result);
    }
    
    [Fact]
    public async Task CheckToken_WithInvalidToken_ShouldFail()
    {
        // Arrange
        var username = "tokenuser2";
        await InsertTestAccountAsync(username, "password123");
        
        var request = new CheckTokenRequest
        {
            Username = username,
            Token = "invalid-token"
        };
        
        // Act
        var response = await Client!.CheckTokenAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(AuthResult.AuthInvalidCredentials, response.Result);
    }
    
    [Fact]
    public async Task CheckToken_WithNonExistentUser_ShouldFail()
    {
        // Arrange
        var request = new CheckTokenRequest
        {
            Username = "nonexistent",
            Token = "some-token"
        };
        
        // Act
        var response = await Client!.CheckTokenAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(AuthResult.AuthInvalidCredentials, response.Result);
    }
    
    [Fact]
    public async Task ModifyAccess_WithExistingAccount_ShouldUpdateLevels()
    {
        // Arrange
        var username = "accessuser";
        await InsertTestAccountAsync(username, "password123", gmLevel: 0);
        
        var request = new ModifyAccessRequest
        {
            Username = username,
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // Act
        var response = await Client!.ModifyAccessAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        
        // Verify the changes
        var getRequest = new GetAccountRequest { Username = username };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        Assert.NotNull(getResponse.Account);
        Assert.Equal(40, getResponse.Account.GmLevel);
        Assert.Equal(10, getResponse.Account.CoreLevel);
    }
    
    [Fact]
    public async Task ModifyAccess_WithNonExistentAccount_ShouldFail()
    {
        // Arrange
        var request = new ModifyAccessRequest
        {
            Username = "nonexistent",
            GmLevel = 40,
            CoreLevel = 10
        };
        
        // Act
        var response = await Client!.ModifyAccessAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }
    
    [Fact]
    public async Task BanPlayer_ShouldReturnSuccess()
    {
        // Arrange - Note: BanPlayer is marked as TODO in the code
        var request = new BanPlayerRequest
        {
            Username = "banuser",
            Reason = "Test ban",
            BanExpiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // Act
        var response = await Client!.BanPlayerAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
    }
    
    [Fact]
    public async Task SanctionPlayer_ShouldReturnSuccess()
    {
        // Arrange - Note: SanctionPlayer is marked as TODO in the code
        var request = new SanctionPlayerRequest
        {
            Username = "sanctionuser",
            SanctionType = "warning",
            Details = "Test sanction",
            Expiry = TCPManager.GetTimeStamp() + 86400
        };
        
        // Act
        var response = await Client!.SanctionPlayerAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
    }
}
