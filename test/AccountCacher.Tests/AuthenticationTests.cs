using Common;
using FrameWork;

namespace AccountCacher.Tests;

public class AuthenticationTests : AccountCacherTestBase
{
    [Fact]
    public async Task AuthenticateUser_WithValidCredentials_ShouldSucceed()
    {
        // Arrange
        var username = "validuser";
        var password = "password123";
        await InsertTestAccountAsync(username, password, "valid@test.com", gmLevel: 0);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.Success, response.Result);
        Assert.NotNull(response.Account);
        Assert.Equal(username, response.Account.Username);
        Assert.Equal("valid@test.com", response.Account.Email);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInvalidPassword_ShouldFail()
    {
        // Arrange
        var username = "testuser";
        await InsertTestAccountAsync(username, "correctpassword");
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "wrongpassword"
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.InvalidCredentials, response.Result);
        Assert.Null(response.Account);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithNonExistentUser_ShouldFail()
    {
        // Arrange
        var request = new AuthenticateUserRequest
        {
            Username = "nonexistent",
            Password = "password123"
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.InvalidCredentials, response.Result);
        Assert.Null(response.Account);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithBannedAccount_ShouldFail()
    {
        // Arrange
        var username = "banneduser";
        // Use banned = 1 for permanent ban
        await InsertTestAccountAsync(username, "password123", banned: 1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.AccountBanned, response.Result);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithInactiveAccount_ShouldFail()
    {
        // Arrange
        var username = "inactiveuser";
        // GM level < 0 means inactive
        await InsertTestAccountAsync(username, "password123", gmLevel: -1);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "password123"
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.NotActive, response.Result);
    }
    
    [Fact]
    public async Task AuthenticateUser_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var username = "caseuser";
        var password = "password123";
        await InsertTestAccountAsync(username, password);
        
        // Test with different case
        var request = new AuthenticateUserRequest
        {
            Username = "CaseUser",
            Password = password
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.Success, response.Result);
        Assert.NotNull(response.Account);
    }
    
    [Fact]
    public async Task AuthenticateUser_PasswordCaseInsensitive_ShouldWork()
    {
        // Arrange
        var username = "pwduser";
        var password = "PassWord123";
        await InsertTestAccountAsync(username, password);
        
        // Test with different case password
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = "PASSWORD123"
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.Success, response.Result);
    }
    
    [Fact]
    public async Task AuthenticateUser_MultipleTimes_ShouldSucceed()
    {
        // Arrange
        var username = "multiuser";
        var password = "password123";
        await InsertTestAccountAsync(username, password);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // Act - Authenticate multiple times
        var response1 = await Client!.AuthenticateUserAsync(request);
        var response2 = await Client.AuthenticateUserAsync(request);
        var response3 = await Client.AuthenticateUserAsync(request);
        
        // Assert - All should succeed
        Assert.Equal(LoginResult.Success, response1.Result);
        Assert.Equal(LoginResult.Success, response2.Result);
        Assert.Equal(LoginResult.Success, response3.Result);
    }
    
    [Fact]
    public async Task AuthenticateUser_WithExpiredBan_ShouldSucceed()
    {
        // Arrange
        var username = "expiredbanuser";
        var password = "password123";
        // Use a timestamp in the past (ban expired)
        var expiredTimestamp = TCPManager.GetTimeStamp() - 10000;
        await InsertTestAccountAsync(username, password, banned: expiredTimestamp);
        
        var request = new AuthenticateUserRequest
        {
            Username = username,
            Password = password
        };
        
        // Act
        var response = await Client!.AuthenticateUserAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(LoginResult.Success, response.Result);
    }
}
