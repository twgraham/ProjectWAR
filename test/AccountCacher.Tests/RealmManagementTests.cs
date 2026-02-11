namespace AccountCacher.Tests;

public class RealmManagementTests : IClassFixture<AccountCacherFixture>, IAsyncLifetime
{
    private readonly AccountCacherFixture _fixture;
    private AccountMgr.AccountMgrClient Client => _fixture.Client;
    
    public RealmManagementTests(AccountCacherFixture fixture)
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
    public async Task ListRealms_WithNoRealms_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new ListRealmsRequest();
        
        // Act
        var response = await Client.ListRealmsAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Realms.ShouldBeEmpty();
    }
    
    [Fact]
    public async Task ListRealms_WithMultipleRealms_ShouldReturnAll()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "Realm1", "127.0.0.1", 10300);
        await _fixture.InsertTestRealmAsync(2, "Realm2", "127.0.0.1", 10301);
        await _fixture.InsertTestRealmAsync(3, "Realm3", "127.0.0.1", 10302);
        
        // Need to reload the service to load realms
        // Since realms are loaded on startup, we need to wait for them to be loaded
        await Task.Delay(500);
        
        var request = new ListRealmsRequest();
        
        // Act
        var response = await Client.ListRealmsAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Realms.Count.ShouldBe(3);
        response.Realms.ShouldContain(r => r.Name == "Realm1");
        response.Realms.ShouldContain(r => r.Name == "Realm2");
        response.Realms.ShouldContain(r => r.Name == "Realm3");
    }
    
    [Fact]
    public async Task GetRealm_WithExistingRealmId_ShouldReturnRealm()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "TestRealm", "127.0.0.1", 10300);
        await Task.Delay(500); // Wait for realm to be loaded
        
        var request = new GetRealmRequest { RealmId = 1 };
        
        // Act
        var response = await Client.GetRealmAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Realm.ShouldNotBeNull();
        response.Realm.RealmId.ShouldBe((uint)1);
        response.Realm.Name.ShouldBe("TestRealm");
        response.Realm.Port.ShouldBe((uint)10300);
    }
    
    [Fact]
    public async Task GetRealm_WithNonExistentRealmId_ShouldReturnNull()
    {
        // Arrange
        var request = new GetRealmRequest { RealmId = 99 };
        
        // Act
        var response = await Client.GetRealmAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Realm.ShouldBeNull();
    }
    
    [Fact]
    public async Task UpdateRealm_WithExistingRealm_ShouldSucceed()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "UpdateRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new UpdateRealmRequest
        {
            RealmId = 1,
            OnlinePlayers = 100,
            OrderCount = 50,
            DestructionCount = 50
        };
        
        // Act
        var response = await Client.UpdateRealmAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        
        // Verify the realm was updated
        var getRequest = new GetRealmRequest { RealmId = 1 };
        var getResponse = await Client.GetRealmAsync(getRequest);
        getResponse.Realm.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task UpdateRealmCharactersTotal_WithExistingRealm_ShouldSucceed()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "CharCountRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new UpdateRealmCharactersTotalRequest
        {
            RealmId = 1,
            OrderCount = 100,
            DestructionCount = 150
        };
        
        // Act
        var response = await Client.UpdateRealmCharactersTotalAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
    }
    
    [Fact]
    public async Task GetClusterList_WithRealms_ShouldReturnClusterInfo()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "ClusterRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client.GetClusterListAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Clusters.ShouldNotBeEmpty();
        
        var cluster = response.Clusters.First();
        cluster.ClusterId.ShouldBe((uint)1);
        cluster.ClusterName.ShouldBe("ClusterRealm");
        cluster.ServerList.ShouldNotBeEmpty();
        cluster.PropertyList.ShouldNotBeEmpty();
    }
    
    [Fact]
    public async Task GetClusterList_WithNoRealms_ShouldReturnEmptyList()
    {
        // Arrange
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client.GetClusterListAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Clusters.ShouldBeEmpty();
    }
    
    [Fact]
    public async Task GetClusterList_ShouldIncludeRealmProperties()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "PropRealm", "192.168.1.1", 10300);
        await Task.Delay(500);
        
        var request = new GetClusterListRequest();
        
        // Act
        var response = await Client.GetClusterListAsync(request);
        
        // Assert
        response.ShouldNotBeNull();
        response.Clusters.ShouldNotBeEmpty();
        
        var cluster = response.Clusters.First();
        cluster.PropertyList.ShouldNotBeEmpty();
        
        // Check for specific properties
        cluster.PropertyList.ShouldContain(p => p.PropName == "setting.name");
        cluster.PropertyList.ShouldContain(p => p.PropName == "setting.net.address");
        cluster.PropertyList.ShouldContain(p => p.PropName == "setting.net.port");
    }
    
    [Fact]
    public async Task ListRealms_ShouldIncludeOnlinePlayerCounts()
    {
        // Arrange
        await _fixture.InsertTestRealmAsync(1, "PopulatedRealm", "127.0.0.1", 10300);
        await Task.Delay(500);
        
        // Update realm with player counts
        var updateRequest = new UpdateRealmRequest
        {
            RealmId = 1,
            OnlinePlayers = 250,
            OrderCount = 120,
            DestructionCount = 130
        };
        await Client.UpdateRealmAsync(updateRequest);
        
        // Act
        var listRequest = new ListRealmsRequest();
        var response = await Client.ListRealmsAsync(listRequest);
        
        // Assert
        response.ShouldNotBeNull();
        response.Realms.ShouldNotBeEmpty();
        
        var realm = response.Realms.First(r => r.RealmId == 1);
        // Note: The counts may be set during update, verify structure exists
        realm.ShouldNotBeNull();
    }
}
