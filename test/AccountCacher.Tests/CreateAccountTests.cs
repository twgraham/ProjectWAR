using Common;
using Grpc.Core;

namespace AccountCacher.Tests;

public class CreateAccountTests : AccountCacherTestBase
{
    [Fact]
    public async Task CreateAccount_WithValidDetails_ShouldSucceed()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            Username = "newuser",
            Password = "password123",
            Email = "newuser@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Created);
        
        // Verify account was created in database by fetching it
        var getRequest = new GetAccountRequest { Username = "newuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        Assert.NotNull(getResponse.Account);
        Assert.Equal("newuser", getResponse.Account.Username);
        Assert.Equal("newuser@test.com", getResponse.Account.Email);
        Assert.Equal(0, getResponse.Account.GmLevel);
    }
    
    [Fact]
    public async Task CreateAccount_WithDuplicateUsername_ShouldFail()
    {
        // Arrange
        var username = "duplicateuser";
        await InsertTestAccountAsync(username, "password123");
        
        var request = new CreateAccountRequest
        {
            Username = username,
            Password = "password456",
            Email = "duplicate@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.False(response.Created);
    }
    
    [Fact]
    public async Task CreateAccount_WithSystemUsername_ShouldFail()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            Username = "System",
            Password = "password123",
            Email = "system@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.False(response.Created);
    }
    
    [Fact]
    public async Task CreateAccount_WithGmLevel_ShouldCreateGmAccount()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            Username = "gmuser",
            Password = "password123",
            Email = "gm@test.com",
            GmLevel = 40,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Created);
        
        // Verify GM level was set
        var getRequest = new GetAccountRequest { Username = "gmuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        Assert.NotNull(getResponse.Account);
        Assert.Equal(40, getResponse.Account.GmLevel);
    }
    
    [Fact]
    public async Task CreateAccount_WithLocalhost_ShouldNotRequireVerification()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            Username = "localhostuser",
            Password = "password123",
            Email = "localhost@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.True(response.Created);
        
        // Account should be immediately available for authentication
        var authRequest = new AuthenticateUserRequest
        {
            Username = "localhostuser",
            Password = "password123"
        };
        var authResponse = await Client.AuthenticateUserAsync(authRequest);
        
        Assert.Equal(LoginResult.Success, authResponse.Result);
    }
    
    [Fact]
    public async Task CreateAccount_CaseInsensitive_ShouldNormalizeUsername()
    {
        // Arrange
        var request = new CreateAccountRequest
        {
            Username = "MixedCaseUser",
            Password = "password123",
            Email = "mixedcase@test.com",
            GmLevel = 0,
            LanguageId = 0,
            IpAddress = "127.0.0.1"
        };
        
        // Act
        var response = await Client!.CreateAccountAsync(request);
        
        // Assert
        Assert.True(response.Created);
        
        // Verify username is stored in lowercase
        var getRequest = new GetAccountRequest { Username = "mixedcaseuser" };
        var getResponse = await Client.GetAccountAsync(getRequest);
        
        Assert.NotNull(getResponse.Account);
        Assert.Equal("mixedcaseuser", getResponse.Account.Username);
    }
}
