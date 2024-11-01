using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using MongoDB.Driver;
using MongoDbAtlasService;

namespace EC2SysbenchTest
{
    class Program
    {
        private const string connectionString = "";
        public class Config
        {
            public string amiId { get; set; }
            public string securityGroupId { get; set; }
            public string keyPair { get; set; }
            public string subnetId { get; set; }
            public string iamRole { get; set; }
            public int totalInstances { get; set; }
        }

        static async Task Main(string[] args)
        {
            // Loading configuration from JSON file
            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            Config config = LoadConfig(configFilePath);

            string amiId = config.amiId;
            string securityGroupId = config.securityGroupId;
            string keyPair = config.keyPair;
            string subnetId = config.subnetId;
            string iamRole = config.iamRole;
            string instanceType = "t2.micro";   
            int totalInstances = config.totalInstances;
            //var cloudPerformanceData = new MongoDbService(connectionString, "CloudPerformanceData", "CloudPerformanceData");

            string filePath = Path.Combine("sysbench_outputs.txt");
            List<string> instanceIds = new List<string>();

            Console.WriteLine(amiId);

            // Region and EC2/SSM clients
            var region = RegionEndpoint.USWest1;  
            var ec2Client = new AmazonEC2Client(region);
            var ssmClient = new AmazonSimpleSystemsManagementClient(region);
    
            // 1. Launching the EC2 instance and obtaining instanceId
            instanceIds = await LaunchInstance(ec2Client, amiId, securityGroupId, keyPair, subnetId, iamRole, totalInstances);
            //instanceIds.Add("i-08048cd335ae939be");

            // 2. Giving time for the instances to be ready
            Console.WriteLine("Waiting for the server to be ready...");
            await Task.Delay(180000);  // 360000 for 6 mins (time needed for 5 instances to fully set up)
            
            // 3. Running sysbench tests on the instance through SSM
            
            /*
            string command = "sudo apt-get update -y > /dev/null 2>&1 && sudo apt-get install -y sysbench > /dev/null 2>&1 && " +
                             "sysbench --test=cpu run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' && " +
                             "sysbench --test=memory run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' && " +
                             "sysbench --test=fileio --file-test-mode=seqwr run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//'";
            */
            
            string command = "sudo apt-get update > /dev/null 2>&1 && " +
                 "sudo apt-get install sysbench -y > /dev/null 2>&1 && " +
                 "cpu_time=$(sysbench cpu --cpu-max-prime=20000 --time=0 --events=2000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//') && " +
                 "memory_time=$(sysbench memory --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//') && " +
                 "sysbench fileio --file-test-mode=seqwr prepare > /dev/null 2>&1 && " +
                 "fileio_time=$(sysbench fileio --file-test-mode=seqwr --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//') && " +
                 "sysbench fileio --file-test-mode=seqwr cleanup > /dev/null 2>&1 && " + 
                 "echo $cpu_time && " +
                 "echo $memory_time && " +
                 "echo $fileio_time";
            
            List<string> allOutputs = new List<string>();
            
            foreach (var instanceId in instanceIds)
            {
                Console.WriteLine($"Checking if instance {instanceId} is ready for SSM commands...");

                bool isReady = false;
                int retryCount = 0;
                int maxRetries = 30; // Set a maximum retry limit to avoid infinite loops

                while (!isReady && retryCount < maxRetries)
                {
                    isReady = await IsSSMReady(ec2Client, ssmClient, instanceId);

                    if (!isReady)
                    {
                        retryCount++;
                        Console.WriteLine($"Instance {instanceId} is not ready. Retrying in 5 seconds... (Attempt {retryCount}/{maxRetries})");
                        await Task.Delay(5000); // Wait for 5 seconds before retrying
                    }
                }

                if (!isReady)
                {
                    Console.WriteLine($"Instance {instanceId} could not be made ready after {maxRetries} attempts. Skipping...");
                    continue; // Skip if the instance still isn't ready after maximum retries
                }

                var request = new DescribeInstanceTypesRequest
                {
                    InstanceTypes = new List<string> { instanceType }
                };

                var response = await ec2Client.DescribeInstanceTypesAsync(request);
                var instanceTypeInfo = response.InstanceTypes.FirstOrDefault();

                Console.WriteLine($"Instance {instanceId} is ready. Running sysbench...");

                // Run the command on this instance
                string output = await RunCommandOnInstance(ssmClient, instanceId, command);

                string[] results = output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var cpuTime = double.Parse(results[0]);
                var memoryTime = double.Parse(results[1]);
                var fileIOTime = double.Parse(results[2]);

                double totalTime = cpuTime + memoryTime + fileIOTime;

                var data = new CloudPerformanceData
                {
                    Provider = "AWS",
                    VmSize = instanceType,
                    Location = region.SystemName,
                    CPU = cpuTime.ToString(),
                    Memory = memoryTime.ToString(),
                    Disk = fileIOTime.ToString(),
                    totalTime = totalTime.ToString(),
                    Os = "Ubuntu 18.04",
                    Date = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss")
                };

                //cloudPerformanceData.InsertData(data); //send data to DB

                Console.WriteLine($"CPU Time: {cpuTime}");
                Console.WriteLine($"Memory Time: {memoryTime}");
                Console.WriteLine($"File I/O Time: {fileIOTime}");
                Console.WriteLine($"Total Time: {totalTime}");

                // Collect output
                allOutputs.Add($"Instance {instanceId} Output:\n{output}");

                // Wait for a while before moving on to the next instance (if needed)
                Console.WriteLine("Waiting before moving to the next instance...");
                await Task.Delay(5000);
            }

                    
            File.WriteAllLines(filePath, allOutputs);

            // 4. Terminate the instance
            await TerminateInstances(ec2Client, instanceIds);
        }

        static Config LoadConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Config file not found: {filePath}");
            }

            string jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Config>(jsonString);
        }
        
        static async Task<List<string>> LaunchInstance(IAmazonEC2 ec2Client, string amiId, string securityGroupId, string keyPair, string subnetId, string iamInstanceProfileName, int totalInstances)
        {
            Console.WriteLine("Launching the instances...");

            var request = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = InstanceType.T2Micro,  // Adjust the instance type as needed
                MinCount = 1,
                MaxCount = totalInstances,  // Launch multiple instances
                KeyName = keyPair,
                IamInstanceProfile = new IamInstanceProfileSpecification { Name = iamInstanceProfileName }
            };

            if (!string.IsNullOrEmpty(subnetId))
            {
                request.NetworkInterfaces = new List<InstanceNetworkInterfaceSpecification>
                {
                    new InstanceNetworkInterfaceSpecification
                    {
                        DeviceIndex = 0,
                        SubnetId = subnetId,
                        Groups = new List<string> { securityGroupId },
                        AssociatePublicIpAddress = true
                    }
                };
            }

            else
            {
                request.SecurityGroupIds = new List<string> { securityGroupId };
            }

            var response = await ec2Client.RunInstancesAsync(request);
            List<string> instanceIds = new List<string>();

            foreach (var instance in response.Reservation.Instances)
            {
                instanceIds.Add(instance.InstanceId);
                Console.WriteLine($"Instance {instance.InstanceId} launched successfully.");
                Console.WriteLine($"Public IP: {instance.PublicIpAddress}, Public DNS: {instance.PublicDnsName}");
            }

            return instanceIds;  
        }

        static async Task TerminateInstances(IAmazonEC2 ec2Client, List<string> instanceIds)
        {
            Console.WriteLine($"Terminating instances: {string.Join(", ", instanceIds)}");
            var terminateRequest = new TerminateInstancesRequest
            {
                InstanceIds = instanceIds  // Pass the list of instance IDs
            };
            var response = await ec2Client.TerminateInstancesAsync(terminateRequest);

            foreach (var terminatedInstance in response.TerminatingInstances)
            {
                Console.WriteLine($"Instance {terminatedInstance.InstanceId} terminated successfully with current state: {terminatedInstance.CurrentState.Name}");
            }
        }

        static async Task<string> RunCommandOnInstance(IAmazonSimpleSystemsManagement ssmClient, string instanceId, string command)
        {
            Console.WriteLine($"instanceID2: {instanceId}");
            var sendCommandRequest = new SendCommandRequest
            {
                InstanceIds = new List<string> { instanceId },
                DocumentName = "AWS-RunShellScript",  // Predefined SSM document to run shell scripts
                Parameters = new Dictionary<string, List<string>>
                {
                    { "commands", new List<string> { command } }
                }
            };

            var sendCommandResponse = await ssmClient.SendCommandAsync(sendCommandRequest);
            string commandId = sendCommandResponse.Command.CommandId;
            Console.WriteLine($"Command sent. Command ID: {commandId}");

            string output = await WaitForCommandToComplete(ssmClient, commandId, instanceId);

            return output;  
        }

        static async Task<string> WaitForCommandToComplete(IAmazonSimpleSystemsManagement ssmClient, string commandId, string instanceId)
        {
            int delayBeforeFirstAttempt = 2000; // Delay in milliseconds (2 seconds)
            int maxRetries = 30;   // Maximum number of retry attempts
            int delayBetweenRetries = 5000; // Delay in milliseconds between retries (5 seconds)
            int retryCount = 0;

            // Initial delay before first status check
            await Task.Delay(delayBeforeFirstAttempt);

            while (retryCount < maxRetries)
            {
                try
                {
                    var commandInvocationRequest = new GetCommandInvocationRequest
                    {
                        CommandId = commandId,
                        InstanceId = instanceId
                    };

                    var response = await ssmClient.GetCommandInvocationAsync(commandInvocationRequest);

                    if (response.Status == CommandInvocationStatus.Success)
                    {
                        Console.WriteLine("Command execution completed successfully.");
                        string output = response.StandardOutputContent;
                        Console.WriteLine($"Output:\n{output}");
                        return output;  // Return the output
                    }
                    else if (response.Status == CommandInvocationStatus.Failed)
                    {
                        Console.WriteLine($"Command failed: {response.StandardErrorContent}");
                        return response.StandardErrorContent;  // Return the error output
                    }

                    Console.WriteLine("Waiting for command to complete...");
                    await Task.Delay(delayBetweenRetries); // Wait before checking again
                }
                catch (Amazon.SimpleSystemsManagement.Model.InvocationDoesNotExistException)
                {
                    // Log the exception, but keep retrying up to maxRetries
                    Console.WriteLine($"Invocation does not exist. Retrying... ({retryCount + 1}/{maxRetries})");
                    await Task.Delay(delayBetweenRetries);
                }

                retryCount++;
            }

            throw new Exception("Command did not complete within the maximum number of retries.");
        }


        static async Task<bool> IsSSMReady(IAmazonEC2 ec2Client, IAmazonSimpleSystemsManagement ssmClient, string instanceId)
        {
            var describeInstanceStatusRequest = new DescribeInstanceStatusRequest
            {
                InstanceIds = new List<string> { instanceId },
                IncludeAllInstances = true
            };

            var describeInstanceStatusResponse = await ec2Client.DescribeInstanceStatusAsync(describeInstanceStatusRequest);
            var instanceStatus = describeInstanceStatusResponse.InstanceStatuses.FirstOrDefault();

            if (instanceStatus != null &&
                instanceStatus.InstanceState.Name == "running" && 
                instanceStatus.SystemStatus.Status == "ok")
            {
                // Check SSM agent status
                var commandStatus = await ssmClient.ListCommandsAsync(new ListCommandsRequest
                {
                    Filters = new List<CommandFilter>
                    {
                        new CommandFilter { Key = "InstanceId", Value = instanceId }
                    } 
                });
                
                return commandStatus.Commands.Any();
            }

            return false;
        }

        /*
        static async Task WaitForInstanceToBeReady(IAmazonEC2 ec2Client, string instanceId)
        {
            Console.WriteLine("Waiting for instance to pass 2/2 checks...");

            while (true)
            {
                var request = new DescribeInstanceStatusRequest
                {
                    InstanceIds = new List<string> { instanceId },
                    IncludeAllInstances = true
                };

                var response = await ec2Client.DescribeInstanceStatusAsync(request);
                var instanceStatus = response.InstanceStatuses.FirstOrDefault();

                if (instanceStatus != null &&
                    instanceStatus.InstanceState.Name == "running" &&  // Check if instance is running
                    instanceStatus.InstanceStatus.Status == "ok" &&    // Check instance health
                    instanceStatus.SystemStatus.Status == "ok")        // Check system health
                {
                    Console.WriteLine("Instance is ready and passed 2/2 checks.");
                    break;
                }

                Console.WriteLine("Instance not ready yet, waiting 10 seconds...");
                await Task.Delay(10000);  // Wait 10 seconds before checking again
            }
        }
        */
    }
}