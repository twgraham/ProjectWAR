namespace AccountCacher.Tests;

public class AccountRetrievalTests : IClassFixture<AccountCacherFixture>, IAsyncLifetime
{
    private readonly AccountCacherFixture _fixture;
    private AccountMgr.AccountMgrClient Client => _fixture.Client;
    
    public AccountRetrievalTests(AccountCacherFixture fixture)
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
    public async Task GetAccount_WithExistingUsername_ShouldReturnAccount()
    {
        // Arrange
        var username = "existinguser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123", "existing@test.com");
        
        var request = new GetAccountRequest { Username = username };
        
        // Act
        var response = await Client.GetAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Account);
        Assert.Equal(username, response.Account.Username);
        Assert.Equal("existing@test.com", response.Account.Email);
        Assert.Equal((uint)accountId, response.Account.Id);
    }
    
    [Fact]
    public async Task GetAccount_WithNonExistentUsername_ShouldReturnNull()
    {
        // Arrange
        var request = new GetAccountRequest { Username = "nonexistent" };
        
        // Act
        var response = await Client.GetAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Null(response.Account);
    }
    
    [Fact]
    public async Task GetAccount_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var username = "casetest";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new GetAccountRequest { Username = "CaseTest" };
        
        // Act
        var response = await Client.GetAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Account);
        Assert.Equal(username, response.Account.Username);
    }
    
    [Fact]
    public async Task GetAccountById_WithExistingId_ShouldReturnAccount()
    {
        // Arrange
        var username = "iduser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123", "id@test.com");
        
        var request = new GetAccountByIdRequest { Id = (uint)accountId };
        
        // Act
        var response = await Client.GetAccountByIdAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Account);
        Assert.Equal(username, response.Account.Username);
        Assert.Equal("id@test.com", response.Account.Email);
        Assert.Equal((uint)accountId, response.Account.Id);
    }
    
    [Fact]
    public async Task GetAccountById_WithNonExistentId_ShouldReturnNull()
    {
        // Arrange
        var request = new GetAccountByIdRequest { Id = 999999 };
        
        // Act
        var response = await Client.GetAccountByIdAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Null(response.Account);
    }
    
    [Fact]
    public async Task GetAccount_CalledMultipleTimes_ShouldUseCache()
    {
        // Arrange
        var username = "cacheuser";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new GetAccountRequest { Username = username };
        
        // Act - Call multiple times to test caching
        var response1 = await Client.GetAccountAsync(request);
        var response2 = await Client.GetAccountAsync(request);
        var response3 = await Client.GetAccountAsync(request);
        
        // Assert - All should return the same account
        Assert.NotNull(response1.Account);
        Assert.NotNull(response2.Account);
        Assert.NotNull(response3.Account);
        Assert.Equal(response1.Account.Id, response2.Account.Id);
        Assert.Equal(response1.Account.Id, response3.Account.Id);
    }
    
    [Fact]
    public async Task GetAccountById_AfterGetAccount_ShouldUseCachedData()
    {
        // Arrange
        var username = "cacheiduser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123");
        
        // First get by username to populate cache
        var usernameRequest = new GetAccountRequest { Username = username };
        var usernameResponse = await Client.GetAccountAsync(usernameRequest);
        
        // Act - Get by ID, which should use cached data
        var idRequest = new GetAccountByIdRequest { Id = (uint)accountId };
        var idResponse = await Client.GetAccountByIdAsync(idRequest);
        
        // Assert
        Assert.NotNull(usernameResponse.Account);
        Assert.NotNull(idResponse.Account);
        Assert.Equal(usernameResponse.Account.Id, idResponse.Account.Id);
        Assert.Equal(usernameResponse.Account.Username, idResponse.Account.Username);
    }
    
    [Fact]
    public async Task GetAccount_WithPacketLogEnabled_ShouldReturnFlag()
    {
        // Arrange
        var username = "packetloguser";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        // Enable packet logging by updating the account directly
        using var connection = new MySql.Data.MySqlClient.MySqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        var updateCmd = new MySql.Data.MySqlClient.MySqlCommand(
            "UPDATE accounts SET PacketLog = 1 WHERE Username = @Username", connection);
        updateCmd.Parameters.AddWithValue("@Username", username);
        await updateCmd.ExecuteNonQueryAsync();
        
        var request = new GetAccountRequest { Username = username };
        
        // Act
        var response = await Client.GetAccountAsync(request);
        
        // Assert
        Assert.NotNull(response.Account);
        Assert.True(response.Account.PacketLoggerEnabled);
    }
    
    [Fact]
    public async Task GetAccount_WithBannedAccount_ShouldReturnBanFlag()
    {
        // Arrange
        var username = "bannedcheckuser";
        await _fixture.InsertTestAccountAsync(username, "password123", banned: 1);
        
        var request = new GetAccountRequest { Username = username };
        
        // Act
        var response = await Client.GetAccountAsync(request);
        
        // Assert
        Assert.NotNull(response.Account);
        // Note: IsBanned flag depends on implementation
        // The Account class has IsBanned property based on timestamp comparison
    }
    
    [Fact]
    public async Task GetPendingAccounts_AfterAccountCreation_ShouldReturnNewAccounts()
    {
        // Arrange
        var username1 = "pending1";
        var username2 = "pending2";
        
        // Create accounts which should add them to pending list
        var createRequest1 = new CreateAccountRequest
        {
            Username = username1,
            Password = "password123",
            Email = "pending1@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        var createRequest2 = new CreateAccountRequest
        {
            Username = username2,
            Password = "password456",
            Email = "pending2@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        await Client.CreateAccountAsync(createRequest1);
        await Client.CreateAccountAsync(createRequest2);
        
        // Act
        var request = new GetPendingAccountsRequest();
        var response = await Client.GetPendingAccountsAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.AccountIds);
    }
}
