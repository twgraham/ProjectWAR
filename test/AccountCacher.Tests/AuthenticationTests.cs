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
        // GIVEN
        var username = "validuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password, "valid@test.com", gmLevel: 0);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
        response.Account.ShouldNotBeNull();
        response.Account.Username.ShouldBe(username);
        response.Account.Email.ShouldBe("valid@test.com");
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInvalidPassword_ShouldFail()
    {
        // GIVEN
        var username = "testuser";
        await _fixture.InsertTestAccountAsync(username, "correctpassword");
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "wrongpassword"
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.InvalidCredentials);
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_WithNonExistentUser_ShouldFail()
    {
        // GIVEN
        var request = new AuthenticateUserRequest
        {
            Username = "nonexistent",
            Password = "password123"
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.InvalidCredentials);
        response.Account.ShouldBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_WithBannedAccount_ShouldFail()
    {
        // GIVEN
        var username = "banneduser";
        // Use banned = 1 for permanent ban
        await _fixture.InsertTestAccountAsync(username, "password123", banned: 1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.AccountBanned);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInactiveAccount_ShouldFail()
    {
        // GIVEN
        var username = "inactiveuser";
        // GM level < 0 means inactive
        await _fixture.InsertTestAccountAsync(username, "password123", gmLevel: -1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.NotActive);
    }
    
    [Fact]
    public async Task AuthenticateUser_CaseInsensitive_ShouldWork()
    {
        // GIVEN
        var username = "caseuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        // Test with different case
        var request = new AuthenticateUserRequest
        {
            Username = "CaseUser",
            Password = password
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
        response.Account.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task AuthenticateUser_PasswordCaseInsensitive_ShouldWork()
    {
        // GIVEN
        var username = "pwduser";
        var password = "PassWord123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        // Test with different case password
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "PASSWORD123"
        };
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
    }
    
    [Fact]
    public async Task AuthenticateUser_MultipleTimes_ShouldSucceed()
    {
        // GIVEN
        var username = "multiuser";
        var password = "password123";
        await _fixture.InsertTestAccountAsync(username, password);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // WHEN - Authenticate multiple times
        var response1 = await Client!.AuthenticateUserAsync(request);
        var response2 = await Client.AuthenticateUserAsync(request);
        var response3 = await Client.AuthenticateUserAsync(request);
        
        // THEN - All should succeed
        response1.Result.ShouldBe(LoginResult.Success);
        response2.Result.ShouldBe(LoginResult.Success);
        response3.Result.ShouldBe(LoginResult.Success);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithExpiredBan_ShouldSucceed()
    {
        // GIVEN
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
        
        // WHEN
        var response = await Client!.AuthenticateUserAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Result.ShouldBe(LoginResult.Success);
    }
}
