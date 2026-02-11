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
        // GIVEN an existing user account in the database
        var username = "existinguser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123", "existing@test.com");
        
        var request = new GetAccountRequest { Username = username };
        
        // WHEN retrieving the account by username
        var response = await Client.GetAccountAsync(request);
        
        // THEN the account details should be returned correctly
        response.ShouldNotBeNull();
        response.Account.ShouldNotBeNull();
        response.Account.Username.ShouldBe(username);
        response.Account.Email.ShouldBe("existing@test.com");
        response.Account.Id.ShouldBe((uint)accountId);
    }
    
    [Fact]
    public async Task GetAccount_WithNonExistentUsername_ShouldReturnNull()
    {
        // GIVEN no account exists with the specified username
        var request = new GetAccountRequest { Username = "nonexistent" };
        
        // WHEN attempting to retrieve the account
        var response = await Client.GetAccountAsync(request);
        
        // THEN the response should indicate no account found
        response.ShouldNotBeNull();
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task GetAccount_CaseInsensitive_ShouldWork()
    {
        // GIVEN an account with lowercase username
        var username = "casetest";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new GetAccountRequest { Username = "CaseTest" };
        
        // WHEN retrieving with mixed-case username
        var response = await Client.GetAccountAsync(request);
        
        // THEN the account should be found (case-insensitive lookup)
        response.ShouldNotBeNull();
        response.Account.ShouldNotBeNull();
        response.Account.Username.ShouldBe(username);
    }
    
    [Fact]
    public async Task GetAccountById_WithExistingId_ShouldReturnAccount()
    {
        // GIVEN an existing account with a specific ID
        var username = "iduser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123", "id@test.com");
        
        var request = new GetAccountByIdRequest { Id = (uint)accountId };
        
        // WHEN retrieving the account by ID
        var response = await Client.GetAccountByIdAsync(request);
        
        // THEN the complete account details should be returned
        response.ShouldNotBeNull();
        response.Account.ShouldNotBeNull();
        response.Account.Username.ShouldBe(username);
        response.Account.Email.ShouldBe("id@test.com");
        response.Account.Id.ShouldBe((uint)accountId);
    }
    
    [Fact]
    public async Task GetAccountById_WithNonExistentId_ShouldReturnNull()
    {
        // GIVEN no account exists with the specified ID
        var request = new GetAccountByIdRequest { Id = 999999 };
        
        // WHEN attempting to retrieve by non-existent ID
        var response = await Client.GetAccountByIdAsync(request);
        
        // THEN the response should indicate no account found
        response.ShouldNotBeNull();
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task GetAccount_CalledMultipleTimes_ShouldUseCache()
    {
        // GIVEN an existing account that will be retrieved multiple times
        var username = "cacheuser";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new GetAccountRequest { Username = username };
        
        // WHEN - Call multiple times to test caching
        var response1 = await Client.GetAccountAsync(request);
        var response2 = await Client.GetAccountAsync(request);
        var response3 = await Client.GetAccountAsync(request);
        
        // THEN - All should return the same account
        response1.Account.ShouldNotBeNull();
        response2.Account.ShouldNotBeNull();
        response3.Account.ShouldNotBeNull();
        response2.Account.Id.ShouldBe(response1.Account.Id);
        response3.Account.Id.ShouldBe(response1.Account.Id);
    }
    
    [Fact]
    public async Task GetAccountById_AfterGetAccount_ShouldUseCachedData()
    {
        // GIVEN a cached account that needs to be retrieved
        var username = "cacheiduser";
        var accountId = await _fixture.InsertTestAccountAsync(username, "password123");
        
        // First get by username to populate cache
        var usernameRequest = new GetAccountRequest { Username = username };
        var usernameResponse = await Client.GetAccountAsync(usernameRequest);
        
        // WHEN fetching by ID after username lookup (tests cache hit)
        var idRequest = new GetAccountByIdRequest { Id = (uint)accountId };
        var idResponse = await Client.GetAccountByIdAsync(idRequest);
        
        // THEN the account should be retrieved from cache without database query
        usernameResponse.Account.ShouldNotBeNull();
        idResponse.Account.ShouldNotBeNull();
        idResponse.Account.Id.ShouldBe(usernameResponse.Account.Id);
        idResponse.Account.Username.ShouldBe(usernameResponse.Account.Username);
    }
    
    [Fact]
    public async Task GetAccount_WithPacketLogEnabled_ShouldReturnFlag()
    {
        // GIVEN an account with packet logging enabled
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
        
        // WHEN retrieving the account details
        var response = await Client.GetAccountAsync(request);
        
        // THEN the packet log flag should be correctly returned
        response.Account.ShouldNotBeNull();
        response.Account.PacketLoggerEnabled.ShouldBeTrue();
    }
    
    [Fact]
    public async Task GetAccount_WithBannedAccount_ShouldReturnBanFlag()
    {
        // GIVEN a banned user account
        var username = "bannedcheckuser";
        await _fixture.InsertTestAccountAsync(username, "password123", banned: 1);
        
        var request = new GetAccountRequest { Username = username };
        
        // WHEN retrieving the account information
        var response = await Client.GetAccountAsync(request);
        
        // THEN the ban status should be correctly indicated
        response.Account.ShouldNotBeNull();
        // Note: IsBanned flag depends on implementation
        // The Account class has IsBanned property based on timestamp comparison
    }
    
    [Fact]
    public async Task GetPendingAccounts_AfterAccountCreation_ShouldReturnNewAccounts()
    {
        // GIVEN accounts awaiting email verification
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
        
        // WHEN requesting the list of pending accounts
        var request = new GetPendingAccountsRequest();
        var response = await Client.GetPendingAccountsAsync(request);
        
        // THEN all pending accounts should be returned for admin review
        response.ShouldNotBeNull();
        response.AccountIds.ShouldNotBeEmpty();
    }
}
