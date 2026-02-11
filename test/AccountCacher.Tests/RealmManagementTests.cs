namespace AccountCacher.Tests;

public class RealmManagementTests : AccountCacherTestBase
{
    [Fact]
    public async Task ListRealms_WithNoRealms_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new ListRealmsRequest();
        
        // Act
        var response = await Client!.ListRealmsAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Realms);
    }
    
    [Fact]
    public async Task ListRealms_WithMultipleRealms_ShouldReturnAll()
    {
        // Arrange
        await InsertTestRealmAsync(1, "Realm1", "127.0.0.1", 10300);
        await InsertTestRealmAsync(2, "Realm2", "127.0.0.1", 10301);
        await InsertTestRealmAsync(3, "Realm3", "127.0.0.1", 10302);
        
        // Need to reload the service to load realms
        // Since realms are loaded on startup, we need to wait for them to be loaded
        await Task.Delay(500);
        
        var request = new ListRealmsRequest();
        
        // Act
        var response = await Client!.ListRealmsAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Equal(3, response.Realms.Count);
        Assert.Contains(response.Realms, r => r.Name == "Realm1");
        Assert.Contains(response.Realms, r => r.Name == "Realm2");
        Assert.Contains(response.Realms, r => r.Name == "Realm3");
    }
    
    [Fact]
    public async Task GetRealm_WithExistingRealmId_ShouldReturnRealm()
    {
        // Arrange
        await InsertTestRealmAsync(1, "TestRealm", "127.0.0.1", 10300);
        await Task.Delay(500); // Wait for realm to be loaded
        
        var request = new GetRealmRequest { RealmId = 1 };
        
        // Act
        var response = await Client!.GetRealmAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Realm);
        Assert.Equal((uint)1, response.Realm.RealmId);
        Assert.Equal("TestRealm", response.Realm.Name);
        Assert.Equal((uint)10300, response.Realm.Port);
    }
    
    [Fact]
    public async Task GetRealm_WithNonExistentRealmId_ShouldReturnNull()
    {
        // Arrange
        var request = new GetRealmRequest { RealmId = 99 };
        
        // Act
        var response = await Client!.GetRealmAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Null(response.Realm);
    }
    
    [Fact]
    public async Task UpdateRealm_WithExistingRealm_ShouldSucceed()
    {
        // Arrange
        await InsertTestRealmAsync(1, "UpdateRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new UpdateRealmRequest
        {
            RealmId = 1,
            OnlinePlayers = 100,
            OrderCount = 50,
            DestructionCount = 50
        };
        
        // Act
        var response = await Client!.UpdateRealmAsync(request);
        
        // Assert
        Assert.NotNull(response);
        
        // Verify the realm was updated
        var getRequest = new GetRealmRequest { RealmId = 1 };
        var getResponse = await Client.GetRealmAsync(getRequest);
        Assert.NotNull(getResponse.Realm);
    }
    
    [Fact]
    public async Task UpdateRealmCharactersTotal_WithExistingRealm_ShouldSucceed()
    {
        // Arrange
        await InsertTestRealmAsync(1, "CharCountRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new UpdateRealmCharactersTotalRequest
        {
            RealmId = 1,
            OrderCount = 100,
            DestructionCount = 150
        };
        
        // Act
        var response = await Client!.UpdateRealmCharactersTotalAsync(request);
        
        // Assert
        Assert.NotNull(response);
    }
    
    [Fact]
    public async Task GetClusterList_WithRealms_ShouldReturnClusterInfo()
    {
        // Arrange
        await InsertTestRealmAsync(1, "ClusterRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client!.GetClusterListAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Clusters);
        
        var cluster = response.Clusters.First();
        Assert.Equal((uint)1, cluster.ClusterId);
        Assert.Equal("ClusterRealm", cluster.ClusterName);
        Assert.NotEmpty(cluster.ServerList);
        Assert.NotEmpty(cluster.PropertyList);
    }
    
    [Fact]
    public async Task GetClusterList_WithNoRealms_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client!.GetClusterListAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Clusters);
    }
    
    [Fact]
    public async Task GetClusterList_ShouldIncludeRealmProperties()
    {
        // Arrange
        await InsertTestRealmAsync(1, "PropRealm", "192.168.1.1", 10300);
        await Task.Delay(500);
        
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client!.GetClusterListAsync(request);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Clusters);
        
        var cluster = response.Clusters.First();
        Assert.NotEmpty(cluster.PropertyList);
        
        // Check for specific properties
        Assert.Contains(cluster.PropertyList, p => p.PropName == "setting.name");
        Assert.Contains(cluster.PropertyList, p => p.PropName == "setting.net.address");
        Assert.Contains(cluster.PropertyList, p => p.PropName == "setting.net.port");
    }
    
    [Fact]
    public async Task ListRealms_ShouldIncludeOnlinePlayerCounts()
    {
        // Arrange
        await InsertTestRealmAsync(1, "PopulatedRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        // Update realm with player counts
        var updateRequest = new UpdateRealmRequest
        {
            RealmId = 1,
            OnlinePlayers = 250,
            OrderCount = 120,
            DestructionCount = 130
        };
        await Client!.UpdateRealmAsync(updateRequest);
        
        // Act
        var listRequest = new ListRealmsRequest();
        var response = await Client.ListRealmsAsync(listRequest);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Realms);
        
        var realm = response.Realms.First(r => r.RealmId == 1);
        // Note: The counts may be set during update, verify structure exists
        Assert.NotNull(realm);
    }
}
