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
using DotNetEnv;
using System.Linq;

namespace EC2SysbenchTest
{
    class AWSProgram
    {
        public class Config
        {
            public required string instanceType{ get; set; }
            public required string region{ get; set; }
            public int totalInstances { get; set; }
        }

        public static async Task AwsRun(string[] args)
        {
            //Environment variables
            //json config
            Env.Load();
            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "AWS" ,"config.json");
            Config config = LoadConfig(configFilePath);


            string region = config.region; //Saves region name as string
            var regionEndpoint = RegionEndpoint.GetBySystemName(region);  //Saves region as RegionEndpoint
            string amiId = GetAmiID(region);
            string keyPair = await CreateKeyPairAsync(region);
            string iamRole = Environment.GetEnvironmentVariable("AWS_IAM_ROLE") ?? throw new InvalidOperationException("AWS_IAM_ROLE environment variable is not set.");
            string instanceType = config.instanceType;
            int totalInstances = config.totalInstances;

            //Setting up connection with Database
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("DB_CONNECTION_STRING environment variable is not set.");
            }
            
            var cloudPerformanceData = new MongoDbService(connectionString, "CloudPerformanceData", "CloudPerformanceData");
            
            //Storing launched instance IDs
            List<string> instanceIds = new List<string>();

            // Region and EC2/SSM clients
            var ec2Client = new AmazonEC2Client(regionEndpoint);
            var ssmClient = new AmazonSimpleSystemsManagementClient(regionEndpoint);
            string vpcID = await GetVpcId(ec2Client, region);
            string subnetId = await GetSubnetId(ec2Client, vpcID);
            string securityGroupId = await CreateSecurityGroup(ec2Client, vpcID);

            Console.WriteLine($"[AWS] {amiId}");
    
            // 1. Launching the EC2 instance and obtaining instanceId
            instanceIds = await LaunchInstance(ec2Client, amiId, securityGroupId, keyPair, subnetId, iamRole, totalInstances, instanceType);

            // 2. Giving time for the instances to be ready
            Console.WriteLine($"[AWS] Using VPC ID: {vpcID} and Subnet ID: {subnetId}");
            await Task.Delay(15000);
            Console.WriteLine("[AWS] Waiting for the server to be ready...");
            await Task.Delay(60000);              
            
            //Benchmarks to perform on instances
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
                        

            //Running command on each instance and sending data to Database.            
            foreach (var instanceId in instanceIds)
            {
                Console.WriteLine($"[AWS] Checking if instance {instanceId} is ready for SSM commands...");

                var request = new DescribeInstanceTypesRequest
                {
                    InstanceTypes = new List<string> { instanceType }
                };

                var response = await ec2Client.DescribeInstanceTypesAsync(request);
                var instanceTypeInfo = response.InstanceTypes.FirstOrDefault();

                Console.WriteLine($"[AWS] Instance {instanceId} is ready. Running sysbench...");

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
                    Location = regionEndpoint.SystemName,
                    CPU = cpuTime.ToString(),
                    Memory = memoryTime.ToString(),
                    Disk = fileIOTime.ToString(),
                    totalTime = totalTime.ToString(),
                    Os = "Ubuntu 24.04",
                    Date = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss")
                };

                cloudPerformanceData.InsertData(data); //send data to DB

                Console.WriteLine($"[AWS] CPU Time: {cpuTime}");
                Console.WriteLine($"[AWS] Memory Time: {memoryTime}");
                Console.WriteLine($"[AWS] File I/O Time: {fileIOTime}");
                Console.WriteLine($"[AWS] Total Time: {totalTime}");


                Console.WriteLine("[AWS] Waiting before moving to the next instance...");
                await Task.Delay(5000);
            }

            // 4. Terminate the instance and generated attributes
            await TerminateInstances(ec2Client, instanceIds);
            Console.WriteLine("[AWS] Waiting for instances to terminate...");
            await Task.Delay(90000);
            await DeleteSecurityGroup(ec2Client, securityGroupId);
            await DeleteKeyPairAsync(keyPair, ec2Client);
        }

        //Reading json config
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

        static InstanceType getInstanceType(string instanceTypeName)
        {
            return instanceTypeName.ToLower() switch
            {
                "t2.nano" => InstanceType.T2Nano,
                "t2.micro" => InstanceType.T2Micro,
                "t2.small" => InstanceType.T2Small,
                "t2.medium" => InstanceType.T2Medium,
                "t2.xlarge" => InstanceType.T2Xlarge,
                "t3.2xlarge" => InstanceType.T32Xlarge,

                _=> throw new ArgumentException("Unsupported instance type.")
            };
        }
        
        static async Task<List<string>> LaunchInstance(IAmazonEC2 ec2Client, string amiId, string securityGroupId, string keyPair, string subnetId, string iamInstanceProfileName, int totalInstances, string instanceTypeName)
        {
            Console.WriteLine("[AWS] Launching the instances...");

            var request = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = getInstanceType(instanceTypeName),  // Instance type
                MinCount = 1,
                MaxCount = totalInstances,  // Number of instances to be launched
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
                Console.WriteLine($"[AWS] Instance {instance.InstanceId} launched successfully.");
                Console.WriteLine($"[AWS] Public IP: {instance.PublicIpAddress}, Public DNS: {instance.PublicDnsName}");
            }

            return instanceIds;  
        }

        static async Task TerminateInstances(IAmazonEC2 ec2Client, List<string> instanceIds)
        {
            Console.WriteLine($"[AWS] Terminating instances: {string.Join(", ", instanceIds)}");
            var terminateRequest = new TerminateInstancesRequest
            {
                InstanceIds = instanceIds  // Pass the list of instance IDs
            };
            var response = await ec2Client.TerminateInstancesAsync(terminateRequest);

            foreach (var terminatedInstance in response.TerminatingInstances)
            {
                Console.WriteLine($"[AWS] Instance {terminatedInstance.InstanceId} terminated successfully with current state: {terminatedInstance.CurrentState.Name}");
            }
        }

        static async Task<string> RunCommandOnInstance(IAmazonSimpleSystemsManagement ssmClient, string instanceId, string command)
        {
            Console.WriteLine($"[AWS] instanceID2: {instanceId}");
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
            Console.WriteLine($"[AWS] Command sent. Command ID: {commandId}");

            string output = await WaitForCommandToComplete(ssmClient, commandId, instanceId);

            return output;  
        }

        static async Task<string> WaitForCommandToComplete(IAmazonSimpleSystemsManagement ssmClient, string commandId, string instanceId)
        {
            int delayBeforeFirstAttempt = 2000; // Delay in milliseconds 
            int maxRetries = 30;   // Maximum number of retry attempts.
            int delayBetweenRetries = 2000; // Delay in milliseconds between retries
            int retryCount = 0;

            // Initial delay before first status check
            await Task.Delay(delayBeforeFirstAttempt);
            Console.WriteLine("[AWS] Waiting for command to complete...");
            while (true)
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
                        Console.WriteLine("[AWS] Command execution completed successfully.");
                        string output = response.StandardOutputContent;
                        return output;  
                    }
                    else if (response.Status == CommandInvocationStatus.Failed)
                    {
                        Console.WriteLine($"[AWS] Command failed: {response.StandardErrorContent}");
                        return response.StandardErrorContent;  
                    }

                    await Task.Delay(delayBetweenRetries); 
                }
                catch (Amazon.SimpleSystemsManagement.Model.InvocationDoesNotExistException)
                {
                    // Log the exception, but keep retrying up to maxRetries
                    Console.WriteLine($"[AWS] Invocation does not exist. Retrying... ({retryCount + 1}/{maxRetries})");
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

        public static async Task<string> CreateSecurityGroup(AmazonEC2Client ec2Client, string vpcID)
        {
            //Creating the security group
            var createRequest = new CreateSecurityGroupRequest
            {
                GroupName = "MySecurityGroup", 
                Description = "Security group for SSH access",
                VpcId = vpcID 
            };

            var createResponse = await ec2Client.CreateSecurityGroupAsync(createRequest);
            string groupId = createResponse.GroupId;

            Console.WriteLine($"[AWS] Created security group with ID: {groupId}");

            // Step 2: Add Inbound Rule for SSH
            var ingressRequest = new AuthorizeSecurityGroupIngressRequest
            {
                GroupId = groupId,
                IpPermissions = new List<IpPermission>
                {
                    new IpPermission
                    {
                        IpProtocol = "tcp",
                        FromPort = 22,   // SSH port
                        ToPort = 22,
                        Ipv4Ranges = new List<IpRange>
                        {
                            new IpRange
                            {
                                CidrIp = "13.52.6.112/32", // Replace with your specific IP address
                            }
                        }
                    }
                }
            };

            await ec2Client.AuthorizeSecurityGroupIngressAsync(ingressRequest);
            return groupId;
        }

        public static async Task DeleteSecurityGroup(AmazonEC2Client ec2Client, string groupId)
        {
            try
            {
                //Revoking inbound rules (optional, but ensures the group can be deleted)
                var revokeRequest = new RevokeSecurityGroupIngressRequest
                {
                    GroupId = groupId,
                    IpPermissions = new List<IpPermission>
                    {
                        new IpPermission
                        {
                            IpProtocol = "tcp",
                            FromPort = 22,
                            ToPort = 22,
                            Ipv4Ranges = new List<IpRange>
                            {
                                new IpRange
                                {
                                    CidrIp = "13.52.6.112/32", // Replace with your specific IP address
                                }
                            }
                        }
                    }
                };

                await ec2Client.RevokeSecurityGroupIngressAsync(revokeRequest);
                Console.WriteLine("[AWS] Revoked SSH inbound rule from security group.");

                //Deleting the security group
                var deleteRequest = new DeleteSecurityGroupRequest
                {
                    GroupId = groupId
                };

                await ec2Client.DeleteSecurityGroupAsync(deleteRequest);
                Console.WriteLine($"[AWS] Security group with ID {groupId} has been deleted successfully.");
            }
            catch (AmazonEC2Exception ex)
            {
                Console.WriteLine($"Error deleting security group: {ex.Message}");
            }
        }

        //Obtain id of excisting VPC within the specified region
        public static async Task<string> GetVpcId(AmazonEC2Client ec2Client, string region)
        {
            var vpcResponse = await ec2Client.DescribeVpcsAsync(new DescribeVpcsRequest());
            var vpcId = vpcResponse.Vpcs.FirstOrDefault()?.VpcId;
            
            if (string.IsNullOrEmpty(vpcId))
            {
                throw new InvalidOperationException("No VPC found in the region.");
            }

            return vpcId;
        }

        //Obtain id of excisting subnet within the specified region
        public static async Task<string> GetSubnetId(AmazonEC2Client ec2Client, string vpcId)
        {
            var subnetResponse = await ec2Client.DescribeSubnetsAsync(new DescribeSubnetsRequest
            {
                Filters = new List<Filter>
                {
                    new Filter
                    {
                        Name = "vpc-id",
                        Values = new List<string> { vpcId }
                    }
                }
            });

            var subnetId = subnetResponse.Subnets.FirstOrDefault()?.SubnetId;
            
            if (string.IsNullOrEmpty(subnetId))
            {
                throw new InvalidOperationException($"No subnet found in VPC {vpcId}.");
            }

            return subnetId;
        }   

        //Creating keypair on specified region
        public static async Task<string> CreateKeyPairAsync(string region)
        {
            string keyPairName = "my-key-pair";
            var ec2Client = new AmazonEC2Client(RegionEndpoint.GetBySystemName(region));

            var request = new CreateKeyPairRequest
            {
                KeyName = keyPairName,
            };

            var response = await ec2Client.CreateKeyPairAsync(request);

            Console.WriteLine($"[AWS] Key pair '{keyPairName}' created");

            return keyPairName;
        }
        
        public static async Task DeleteKeyPairAsync(string keyPairName, AmazonEC2Client ec2Client)
        {
            var request = new DeleteKeyPairRequest
            {
                KeyName = keyPairName
            };

            await ec2Client.DeleteKeyPairAsync(request);

            Console.WriteLine($"[AWS] Key pair '{keyPairName}' has been deleted.");
        }

        //AMI IDs available per regions. Currently only US regions are supported
        public static string GetAmiID(string region){
            return region switch
            {
                "us-west-1" => "ami-0da424eb883458071",
                "us-west-2" => "ami-04dd23e62ed049936",
                "us-east-1" => "ami-0866a3c8686eaeeba",
                "us-east-2" => "ami-0ea3c35c5c3284d82",
                _=> throw new ArgumentException("Unsupported instance type.")
            };
        }
    }
}


