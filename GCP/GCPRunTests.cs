using Google.Cloud.Compute.V1;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using DotNetEnv;
using MongoDB.Driver;
using MongoDbAtlasService;

namespace GCPInstanceManager{
    class GCPRunTests{
        public class Config
        {
            public string zone { get; set; }
            public required string machineType { get; set; }
            public int numInstances { get; set; }
        }

        public static async Task Run(string[] args)
        {
            Env.Load();
            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "GCP" ,"gcpConfig.json");
            Config config = LoadConfig(configFilePath);
            string machineType = config.machineType; 
            int numInstances = config.numInstances; 
            var zone = config.zone; 
            string projectId = Environment.GetEnvironmentVariable("PROJECT_ID") ?? throw new InvalidOperationException("PROJECT_ID environment variable is not set."); 
            List<string> instanceNames = new List<string>();
            List<string> allOutputs = new List<string>();
            
            //Database
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("DB_CONNECTION_STRING environment variable is not set.");
            }
        
            var cloudPerformanceData = new MongoDbService(connectionString, "CloudPerformanceData", "CloudPerformanceData");
            
            //Initializing instances
            await CreateInstanceAsyncSample.CreateInstances(numInstances, projectId, machineType);

            for(int i = 1 ; i <= numInstances; i++)
            {
                string machineName = "test-machine" + i;
                instanceNames.Add(machineName);
            }            

            /* //Quicker command set to execute (testing purposes)
            string[] commands = new string[]
            {
                "sudo apt-get update -y > /dev/null 2>&1",
                "sudo apt-get install -y sysbench > /dev/null 2>&1",
                "sysbench --test=cpu run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//'",
                "sysbench --test=memory run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//'",
                "sysbench --test=fileio --file-test-mode=seqwr run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//'"
            };
            */
            
            string[] commands = new string[]
            {
                "sudo apt-get update -y > /dev/null 2>&1",
                "sudo apt-get install -y sysbench > /dev/null 2>&1",
                "sysbench cpu --cpu-max-prime=20000 --time=0 --events=2000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' > cpu_time.txt",
                "sysbench memory --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' > memory_time.txt",
                "sysbench fileio --file-test-mode=seqwr prepare > /dev/null 2>&1",
                "sysbench fileio --file-test-mode=seqwr --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' > fileio_time.txt",
                "sysbench fileio --file-test-mode=seqwr cleanup > /dev/null 2>&1",
                "cat cpu_time.txt",
                "cat memory_time.txt",
                "cat fileio_time.txt"
            };
            
            Console.WriteLine("Waiting for instance to be ready...");
            await Task.Delay(30000);

            foreach (var instanceName in instanceNames)
            {
                Console.WriteLine($"Executing commands on instance: {instanceName}");
                string output = "";
                foreach (var command in commands)
                {
                    output += "\n" + await ExecuteCommandAsync(command, instanceName, zone);
                }

                string[] results = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var cpuTime = double.Parse(results[0]);
                var memoryTime = double.Parse(results[1]);
                var fileIOTime = double.Parse(results[2]);

                double totalTime = cpuTime + memoryTime + fileIOTime;

                var data = new CloudPerformanceData
                {
                    Provider = "GCP",
                    VmSize = machineType,
                    Location = zone,
                    CPU = cpuTime.ToString(),
                    Memory = memoryTime.ToString(),
                    Disk = fileIOTime.ToString(),
                    totalTime = totalTime.ToString(),
                    Os = "Ubuntu 20.04.6",
                    Date = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss")
                };

                //Send data to Database
                cloudPerformanceData.InsertData(data); 

                Console.WriteLine($"CPU Time: {cpuTime}");
                Console.WriteLine($"Memory Time: {memoryTime}");
                Console.WriteLine($"FileIO Time: {fileIOTime}");
            }


            await CreateInstanceAsyncSample.DeleteInstances(projectId, zone, numInstances);
            Console.WriteLine("Instances deleted successfully.");
        }

        static async Task<string> ExecuteCommandAsync(string commandToExecute, string instanceName, string zone)
        {
            var arguments = $"compute ssh {instanceName} --tunnel-through-iap --command \"{commandToExecute}\" --zone {zone}";
            var outputLines = new List<string>();

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "gcloud.cmd",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null) 
                    {
                        outputLines.Add(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
            }

            return string.Join("\n", outputLines);                            
        }


        static Config LoadConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Config file not found: {filePath}");
            }

            string jsonString = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<Config>(jsonString);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration.");
            }
            return config;
        }

    }
    
    public class CreateInstanceAsyncSample
    {
        public static async Task CreateInstanceAsync(
            // TODO(developer): Set your own default values for these parameters or pass different values when calling this method.
            string projectId,
            string zone = "us-west1-b",
            string machineName = "test-machine",
            string machineType = "e2-micro",
            string diskImage = "projects/ubuntu-os-cloud/global/images/family/ubuntu-2004-lts",
            long diskSizeGb = 10,
            string networkName = "my_network")
        {
            Instance instance = new Instance
            {
                Name = machineName,
                // See https://cloud.google.com/compute/docs/machine-types for more information on machine types.
                MachineType = $"zones/{zone}/machineTypes/{machineType}",
                // Instance creation requires at least one persistent disk.
                Disks =
                {
                    new AttachedDisk
                    {
                        AutoDelete = true,
                        Boot = true,
                        Type = ComputeEnumConstants.AttachedDisk.Type.Persistent,
                        InitializeParams = new AttachedDiskInitializeParams 
                        {
                            // See https://cloud.google.com/compute/docs/images for more information on available images.
                            SourceImage = diskImage,
                            DiskSizeGb = diskSizeGb
                        }
                    }
                },
                NetworkInterfaces = 
                { 
                    new NetworkInterface 
                    { 
                        Name = networkName, 
                        AccessConfigs = { new AccessConfig { Name = "External NAT", Type = "ONE_TO_ONE_NAT" } } 
                    } 
                }
            };

            // Initialize client that will be used to send requests. This client only needs to be created
            // once, and can be reused for multiple requests.
            InstancesClient client = await InstancesClient.CreateAsync();

            // Insert the instance in the specified project and zone.
            var instanceCreation = await client.InsertAsync(projectId, zone, instance);

            // Wait for the operation to complete using client-side polling.
            // The server-side operation is not affected by polling,
            // and might finish successfully even if polling times out.
            await instanceCreation.PollUntilCompletedAsync();
        }
        
        //Creating multiple instances
        public static async Task CreateInstances(int numInstances, string projectId, string machineType)
        {
            for(int i = 1; i <= numInstances; i++){
                string machineName = "test-machine" + i;
                await CreateInstanceAsync(machineName: machineName, projectId: projectId, machineType: machineType);
            }
        }

        public static async Task DeleteInstanceAsync(string projectId, string zone, string machineName)
        {
            InstancesClient client = await InstancesClient.CreateAsync();
            var instanceDeletion = await client.DeleteAsync(projectId, zone, machineName);
            await instanceDeletion.PollUntilCompletedAsync();
        }

        //Deleting multiple instances
        public static async Task DeleteInstances(string projectId, string zone, int numInstances)
        {
            for(int i = 1; i <= numInstances; i++){
                string machineName = "test-machine" + i;
                Console.WriteLine($"Deleting Instance {machineName}");
                await DeleteInstanceAsync(projectId, zone, machineName);
            }
        }
    }
}





