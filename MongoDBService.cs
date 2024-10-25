using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoDbAtlasService
{
    public class CloudPerformanceData
    {
        public ObjectId Id { get; set; }
        public string? Provider { get; set; } //this is the cloud provider, ex: AWS, Azure, GCP
        public string? VmSize { get; set; } //this is instance type, ex: t2.micro or Standard_B1s
        public string? Location { get; set; } //this is the region, ex: us-west-1
        public string? CPU { get; set; } //this is the time the CPU test took, same for Memory and Disk
        public string? Memory { get; set; }
        public string? Disk { get; set; }
        public string? totalTime { get; set; }
        public string? Os { get; set; }
        public string? Date { get; set; } //this is the date the test was run
    }

    // Service class to handle MongoDB operations
    public class MongoDbService
    {
        private readonly IMongoCollection<CloudPerformanceData> _collection;

        // Constructor: Initializes MongoDB client and collection
        public MongoDbService(string connectionString, string dbName, string collectionName)
        {
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(dbName);
            _collection = database.GetCollection<CloudPerformanceData>(collectionName);
        }

        // Function to insert sample data into the collection
        public void InsertData(CloudPerformanceData data)
        {
            _collection.InsertOne(data);
            Console.WriteLine("Data inserted into MongoDB Atlas");
        }

        // Function to retrieve all data from the collection
        public List<CloudPerformanceData> GetAllData()
        {
            return _collection.Find(new BsonDocument()).ToList();
        }
    }
}
