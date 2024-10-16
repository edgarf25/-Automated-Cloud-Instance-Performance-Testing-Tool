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
            string instanceId = "";  // Will be updated after instance launch
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

            // 1. Launch the EC2 instance and capture the instanceId
            instanceId = await LaunchInstance(ec2Client, amiId, securityGroupId, keyPair, subnetId, iamRole);

            // 2. Wait for the instance to be ready
            Console.WriteLine("Waiting for the server to be ready...");
            await Task.Delay(60000);  // Wait for 60 seconds
            
            /*
            // 3. Run the sysbench tests on the instance via SSM
            string command = "sudo apt-get update -y && sudo apt-get install -y sysbench && " +
                             "sysbench --test=cpu run && " +
                             "sysbench --test=memory run && " +
                             "sysbench --test=fileio --file-test-mode=seqwr run";
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
            

            string output = await RunCommandOnInstance(ssmClient, instanceId, command);
            

            File.WriteAllText(filePath, output);

            // 4. Terminate the instance
            await TerminateInstance(ec2Client, instanceId);
        }

        // Function to launch the EC2 instance using AWS SDK and return the instanceId
        static async Task<string> LaunchInstance(IAmazonEC2 ec2Client, string amiId, string securityGroupId, string keyPair, string subnetId, string iamInstanceProfileName)
        {
            Console.WriteLine("Launching the instance...");

            var request = new RunInstancesRequest
            {
                ImageId = amiId,
                InstanceType = InstanceType.T2Micro,  // Adjust the instance type as needed
                MinCount = 1,
                MaxCount = 1,
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
            var instance = response.Reservation.Instances[0];
            string instanceId = instance.InstanceId;  // Set the instanceId locally

            Console.WriteLine($"Instance {instanceId} launched successfully.");
            Console.WriteLine($"Public IP: {instance.PublicIpAddress}, Public DNS: {instance.PublicDnsName}");

            // Save instance ID and IP address to files if needed
            File.WriteAllText("instanceIP.txt", instance.PublicIpAddress);
            File.WriteAllText("instanceID.txt", instanceId);

            return instanceId;  // Return the instanceId
        }

        // Function to terminate the EC2 instance using AWS SDK
        static async Task TerminateInstance(IAmazonEC2 ec2Client, string instanceId)
        {
            Console.WriteLine($"Terminating instance: {instanceId}");
            var terminateRequest = new TerminateInstancesRequest
            {
                InstanceIds = new List<string> { instanceId }
            };
            var response = await ec2Client.TerminateInstancesAsync(terminateRequest);

            var terminatedInstance = response.TerminatingInstances[0];
            Console.WriteLine($"Instance {terminatedInstance.InstanceId} terminated successfully with current state: {terminatedInstance.CurrentState.Name}");
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
