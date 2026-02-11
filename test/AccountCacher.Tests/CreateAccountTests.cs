using Common;
using Grpc.Core;

namespace AccountCacher.Tests;

public class CreateAccountTests : IClassFixture<AccountCacherFixture>, IAsyncLifetime
{
    private readonly AccountCacherFixture _fixture;
    private AccountMgr.AccountMgrClient Client => _fixture.Client!;
    
    public CreateAccountTests(AccountCacherFixture fixture)
    {
        _fixture = fixture;
    }
    
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    
    public async ValueTask DisposeAsync()
    {
        // Clean up test data after each test
        await _fixture.ClearAccountsAsync();
    }
    [Fact]
    public async Task CreateAccount_WithValidDetails_ShouldSucceed()
    {
        // GIVEN
        var request = new CreateAccountRequest
        {
            Username = "newuser",
            Password = "password123",
            Email = "newuser@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Created.ShouldBeTrue();
        
        // Verify account was created in database by fetching it
        var getRequest = new GetAccountRequest { Username = "newuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        getResponse.Account.ShouldNotBeNull();
        getResponse.Account.Username.ShouldBe("newuser");
        getResponse.Account.Email.ShouldBe("newuser@test.com");
        getResponse.Account.GmLevel.ShouldBe(0);
    }
    
    [Fact]
    public async Task CreateAccount_WithDuplicateUsername_ShouldFail()
    {
        // GIVEN
        var username = "duplicateuser";
        await _fixture.InsertTestAccountAsync(username, "password123");
        
        var request = new CreateAccountRequest
        {
            Username = username,
            Password = "password456",
            Email = "duplicate@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Created.ShouldBeFalse();
    }
    
    [Fact]
    public async Task CreateAccount_WithSystemUsername_ShouldFail()
    {
        // GIVEN
        var request = new CreateAccountRequest
        {
            Username = "System",
            Password = "password123",
            Email = "system@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Created.ShouldBeFalse();
    }
    
    [Fact]
    public async Task CreateAccount_WithGmLevel_ShouldCreateGmAccount()
    {
        // GIVEN
        var request = new CreateAccountRequest
        {
            Username = "gmuser",
            Password = "password123",
            Email = "gm@test.com",
            GmLevel = 40,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Created.ShouldBeTrue();
        
        // Verify GM level was set
        var getRequest = new GetAccountRequest { Username = "gmuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        getResponse.Account.ShouldNotBeNull();
        getResponse.Account.GmLevel.ShouldBe(40);
    }
    
    [Fact]
    public async Task CreateAccount_WithLocalhost_ShouldNotRequireVerification()
    {
        // GIVEN
        var request = new CreateAccountRequest
        {
            Username = "localhostuser",
            Password = "password123",
            Email = "localhost@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.ShouldNotBeNull();
        response.Created.ShouldBeTrue();
        
        // Account should be immediately available for authentication
        var authRequest = new AuthenticateUserRequest
        {
            Username = "localhostuser",
            Password = "password123"
        };
        var authResponse = await Client.AuthenticateUserAsync(authRequest);
        
        authResponse.Result.ShouldBe(LoginResult.Success);
    }
    
    [Fact]
    public async Task CreateAccount_CaseInsensitive_ShouldNormalizeUsername()
    {
        // GIVEN
        var request = new CreateAccountRequest
        {
            Username = "MixedCaseUser",
            Password = "password123",
            Email = "mixedcase@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // WHEN
        var response = await Client!.CreateAccountAsync(request);
        
        // THEN
        response.Created.ShouldBeTrue();
        
        // Verify username is stored in lowercase
        var getRequest = new GetAccountRequest { Username = "mixedcaseuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        getResponse.Account.ShouldNotBeNull();
        getResponse.Account.Username.ShouldBe("mixedcaseuser");
    }
}
