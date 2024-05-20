using MongoDB.Bson;
using MongoDB.Driver;

namespace Assistant.Net.Services;

public class MongoDbService(BotConfig config)
{
    private readonly string _database_name = config.mongodb.database_name;

    public MongoClient Client { get; } = new MongoClient(connectionString: config.mongodb.connection_string);

    public IMongoDatabase Database => Client.GetDatabase(_database_name);

    public async Task UpdateUserLastSeen(ulong userId)
    {
        var lastSeen = DateTime.UtcNow;
        var collection = Database.GetCollection<BsonDocument>("users");
        string UID = userId.ToString();

        var filter = Builders<BsonDocument>.Filter.Eq("_id", UID);
        var update = Builders<BsonDocument>.Update.Set("last_seen", lastSeen);
        var options = new UpdateOptions { IsUpsert = true };

        await collection.UpdateOneAsync(filter, update, options);
    }

    public async Task SetUserDescription(ulong userId, string description)
    {
        var collection = Database.GetCollection<BsonDocument>("users");
        string UID = userId.ToString();

        var filter = Builders<BsonDocument>.Filter.Eq("_id", UID);
        var update = Builders<BsonDocument>.Update.Set("description", description);
        var options = new UpdateOptions { IsUpsert = true };

        await collection.UpdateOneAsync(filter, update, options);
    }

    public async Task<(DateTime?, string?)> GetUserLastSeenAndDescription(ulong userId)
    {
        var collection = Database.GetCollection<BsonDocument>("users");
        string UID = userId.ToString();

        var projection = Builders<BsonDocument>.Projection.Include("last_seen").Include("description");
        var user = await collection.Find(new BsonDocument("_id", UID)).Project(projection).FirstOrDefaultAsync();

        if (user == null)
            return (null, null);

        DateTime? lastSeen = user.Contains("last_seen") ? user["last_seen"].ToUniversalTime() : null;
        string? description = user.Contains("description") ? user["description"].AsString : null;
        return (lastSeen, description);
    }

}
