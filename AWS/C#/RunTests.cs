using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace EC2SysbenchTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Variables
            List<string> instanceIds = new List<string>();  // Stores all instance IDs
            string amiId = "ami-08012c0a9ee8e21c4";
            string securityGroupId = "sg-0247592d9fd24ae7a";
            string keyPair = "my-key-pair";
            string subnetId = "subnet-04cd33806b21ff6e8";
            string localSavePath = Path.Combine("Tests");
            string iamRole = "EnablesEC2ToAccessSystemsManagerRole";
            string filePath = Path.Combine(localSavePath, "sysbench_outputs.txt");

            // Region and EC2/SSM clients
            var region = RegionEndpoint.USWest1;
            var ec2Client = new AmazonEC2Client(region);
            var ssmClient = new AmazonSimpleSystemsManagementClient(region);
            var ssmClient2 = new AmazonSimpleSystemsManagementClient(region);
            var ssmClient3 = new AmazonSimpleSystemsManagementClient(region);

            // 1. Launch the EC2 instance and capture the instanceId
            instanceIds = await LaunchInstance(ec2Client, amiId, securityGroupId, keyPair, subnetId, iamRole);

            // 2. Wait for the instance to be ready
            Console.WriteLine("Waiting for the server to be ready...");
            await Task.Delay(180000);  // Wait for 60 seconds
            
            
            // 3. Run the sysbench tests on the instance via SSM
            string command = "sudo apt-get update -y > /dev/null 2>&1 && sudo apt-get install -y sysbench > /dev/null 2>&1 && " +
                             "sysbench --test=cpu run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' && " +
                             "sysbench --test=memory run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//' && " +
                             "sysbench --test=fileio --file-test-mode=seqwr run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//'";
            
            
            /*
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
            */

            List<string> allOutputs = new List<string>();

            /*
            foreach (var instanceId in instanceIds)
            {
                Console.WriteLine($"Running sysbench on instance {instanceId}...");
                
                // Run the command on this instance
                string output = await RunCommandOnInstance(ssmClient, instanceId, command);
                
                // Collect output
                allOutputs.Add($"Instance {instanceId} Output:\n{output}");
                Console.WriteLine("Waiting for the server to be ready...");
                await Task.Delay(30000);
            }
            */

            foreach (var instanceId in instanceIds)
            {
                // Check if the instance is ready for SSM
                Console.WriteLine($"Checking if instance {instanceId} is ready for SSM commands...");
                bool isReady = await IsSSMReady(ec2Client, ssmClient, instanceId);
                
                if (!isReady)
                {
                    Console.WriteLine($"Instance {instanceId} is not ready. Skipping...");
                    continue;
                }

                Console.WriteLine($"Instance {instanceId} is ready. Running sysbench...");

                // Run the command on this instance
                string output = await RunCommandOnInstance(ssmClient, instanceId, command);

                // Collect output
                allOutputs.Add($"Instance {instanceId} Output:\n{output}");

                // Wait for a while before moving on to the next instance (if needed)
                Console.WriteLine("Waiting before moving to the next instance...");
                await Task.Delay(60000);
            }
                    
            File.WriteAllLines(filePath, allOutputs);

            // 4. Terminate the instance
            await TerminateInstances(ec2Client, instanceIds);
        }

        // Function to launch the EC2 instance using AWS SDK and return the instanceId
        
        static async Task<List<string>> LaunchInstance(IAmazonEC2 ec2Client, string amiId, string securityGroupId, string keyPair, string subnetId, string iamInstanceProfileName)
        {
            Console.WriteLine("Launching the instances...");

            var request = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = InstanceType.T2Micro,  // Adjust the instance type as needed
                MinCount = 1,
                MaxCount = 3,  // Launch multiple instances
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

                // Save instance ID and IP address to files if needed
                File.WriteAllText($"instanceIP_{instance.InstanceId}.txt", instance.PublicIpAddress); //may be an issue. Forgot if we still use text files to retrieve both of these values
                File.WriteAllText($"instanceID_{instance.InstanceId}.txt", instance.InstanceId);
            }

            return instanceIds;  // Return the list of instance IDs
        }

        // Function to terminate the EC2 instance using AWS SDK
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

        // Run a command on the EC2 instance via SSM
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

            // Wait for the command to complete and capture the output
            string output = await WaitForCommandToComplete(ssmClient, commandId, instanceId);

            return output;  // Return the output
        }

        // Wait for the command execution to complete
        static async Task<string> WaitForCommandToComplete(IAmazonSimpleSystemsManagement ssmClient, string commandId, string instanceId)
        {
            while (true)
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
                await Task.Delay(5000);  // Wait 5 seconds before checking again
            }
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
                    } //CHECKPOINT: Use "Value" instead of "Values". Read documentation for errors GPT is giving
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