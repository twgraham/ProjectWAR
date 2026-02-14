using Common;
using FrameWork;

namespace AccountCacher.Tests;

public class AuthenticationTests : IClassFixture<AccountCacherFixture>, IAsyncLifetime
{
    private readonly AccountCacherFixture _fixture;
    private AccountMgr.AccountMgrClient Client => _fixture.Client!;
    
    public AuthenticationTests(AccountCacherFixture fixture)
    {
        _fixture = fixture;
    }
    
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    
    public async ValueTask DisposeAsync()
    {
        await _fixture.ClearAccountsAsync();
    }
    [Fact]
    public async Task AuthenticateUser_WithValidCredentials_ShouldSucceed()
    {
        // GIVEN a valid user account with correct credentials
        var username = "validuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password, "valid@test.com", gmLevel: 0);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // WHEN authenticating with valid credentials
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should succeed and return account details
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
        response.Account.ShouldNotBeNull();
        response.Account.Username.ShouldBe(username);
        response.Account.Email.ShouldBe("valid@test.com");
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInvalidPassword_ShouldFail()
    {
        // GIVEN a user account with a specific password
        var username = "invalidpwdtest";
        await _fixture.InsertTestAccountAsync(username, "correctpassword");
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "wrongpassword"
        };
        
        // WHEN attempting authentication with incorrect password
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should fail with invalid credentials
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.InvalidCredentials);
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_WithNonExistentUser_ShouldFail()
    {
        // GIVEN no account exists with the specified username
        var request = new AuthenticateUserRequest
        {
            Username = "nonexistent",
            Password = "password123"
        };
        
        // WHEN attempting to authenticate a non-existent user
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should fail to prevent user enumeration
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.InvalidCredentials);
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_WithBannedAccount_ShouldFail()
    {
        // GIVEN a user account that has been permanently banned
        var username = "banneduser";
        // Use banned = 1 for permanent ban
        await _fixture.InsertTestAccountAsync(username, "password123", banned: 1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // WHEN attempting to authenticate with a banned account
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should fail indicating the ban status
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.AccountBanned);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInactiveAccount_ShouldFail()
    {
        // GIVEN an inactive user account (GM level < 0 indicates inactive)
        var username = "inactiveuser";
        // GM level < 0 means inactive
        await _fixture.InsertTestAccountAsync(username, "password123", gmLevel: -1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // WHEN attempting to authenticate with an inactive account
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should fail until account is activated
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.NotActive);
    }
    
    [Fact]
    public async Task AuthenticateUser_CaseInsensitive_ShouldWork()
    {
        // GIVEN a user account with lowercase username
        var username = "caseuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        // Test with different case
        var request = new AuthenticateUserRequest
        {
            Username = "CaseUser",
            Password = password
        };
        
        // WHEN authenticating with mixed-case username
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should succeed because usernames are case-insensitive
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
        response.Account.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_PasswordCaseInsensitive_ShouldWork()
    {
        // GIVEN a user account with mixed-case password
        var username = "pwduser";
        var password = "PassWord123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        // Test with different case password
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "PASSWORD123"
        };
        
        // WHEN authenticating with different password casing
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should succeed because passwords are case-insensitive
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
    }
    
    [Fact]
    public async Task AuthenticateUser_MultipleTimes_ShouldSucceed()
    {
        // GIVEN a valid user account
        var username = "multiuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // WHEN authenticating multiple times in succession
        var response1 = await Client!.AuthenticateUserAsync(request);
        var response2 = await Client.AuthenticateUserAsync(request);
        var response3 = await Client.AuthenticateUserAsync(request);
        
        // THEN all authentication attempts should succeed (no rate limiting or session conflicts)
        response1.Result.ShouldBe(LoginResult.Success);
        response2.Result.ShouldBe(LoginResult.Success);
        response3.Result.ShouldBe(LoginResult.Success);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithExpiredBan_ShouldSucceed()
    {
        // GIVEN a user account with an expired ban (timestamp in the past)
        var username = "expiredbanuser";
        var password = "password123";
        // Use a timestamp in the past (ban expired)
        var expiredTimestamp = TCPManager.GetTimeStamp() - 10000;
        await _fixture.InsertTestAccountAsync(username, password, banned: expiredTimestamp);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // WHEN attempting to authenticate after ban expiration
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN authentication should succeed because the ban has expired
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
    }
}
