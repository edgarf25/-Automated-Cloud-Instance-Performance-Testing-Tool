// Program.cs
using System.Globalization;
using System.Text.Json;
using DotNetEnv;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.Network;
using Microsoft.Azure.Management.Network.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using MongoDbAtlasService;

class Program
{
    public static async Task Run(string[] args)
    {
        Env.Load(); // Load the .env file

        // Replace with your Azure subscription ID
        string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID") ?? throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID environment variable is not set.");

        // Getting the access token once
        string accessToken = await TokenService.GetAccessTokenAsync();
        var tokenCredentials = new TokenCredentials(accessToken);

        // Read and parse the JSON configuration file
        string configFilePath = "vmConfigurations.json";
        var vmConfigurations = await LoadVmConfigurationsAsync(configFilePath);

        // Check if vmConfigurations is null
        if (vmConfigurations == null)
        {
            Console.WriteLine("vmConfigurations is null.");
            return;
        }

        // Check if vmConfigurations.Vms is null
        if (vmConfigurations.Vms == null)
        {
            Console.WriteLine("vmConfigurations.Vms is null.");
            return;
        }

        // Check if vmConfigurations.Vms has any items
        if (vmConfigurations.Vms.Count == 0)
        {
            Console.WriteLine("vmConfigurations.Vms is empty.");
            return;
        }

        // Iterate over all VM configurations
        foreach (var vmConfig in vmConfigurations.Vms)
        {
            try
            {
                Console.WriteLine($"Processing VM configuration: {vmConfig.Name}");
                await CreateAndManageVmAsync(vmConfig, tokenCredentials, subscriptionId);
                Console.WriteLine($"Finished processing VM: {vmConfig.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while processing VM '{vmConfig.Name}': {ex.Message}");
            }
        }

        Console.WriteLine("All VMs have been processed.");
    }

    // Function to load the VM configurations from the JSON file
    private static async Task<VmConfigurations?> LoadVmConfigurationsAsync(string filePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(filePath);

            // Read the file content
            string jsonString = await File.ReadAllTextAsync(filePath);
            //Console.WriteLine("Configuration file content:");
            //Console.WriteLine(jsonString);

            // Deserialize the JSON content
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var vmConfigurations = JsonSerializer.Deserialize<VmConfigurations>(jsonString, options);

            if (vmConfigurations == null)
            {
                //Console.WriteLine("Deserialization returned null.");
            }
            else
            {
                Console.WriteLine("Successfully loaded JSON configuration file.");
                Console.WriteLine($"Number of VMs: {vmConfigurations.Vms?.Count ?? 0}");
            }

            return vmConfigurations;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading or deserializing configuration file '{filePath}': {ex.Message}");
            return null;
        }
    }


    // Function to create and manage the VM using the selected configuration
    private static async Task CreateAndManageVmAsync(VmConfiguration vmConfig, TokenCredentials tokenCredentials, string subscriptionId)
    {
        var resourceClient = new ResourceManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };
        var networkClient = new NetworkManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };
        var computeClient = new ComputeManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };

        // Create the Resource Group
        await resourceClient.ResourceGroups.CreateOrUpdateAsync(vmConfig.ResourceGroupName, new ResourceGroup(vmConfig.Location));

        // Create network resources for the VM
        var virtualNetwork = await CreateVirtualNetworkAsync(networkClient, vmConfig);
        var subnet = virtualNetwork.Subnets.FirstOrDefault(s => s.Name == vmConfig.Network.SubnetName);
        if (subnet == null)
        {
            Console.WriteLine("Failed to retrieve subnet.");
            return;
        }
        var publicIp = await CreatePublicIPAddressAsync(networkClient, vmConfig);
        var networkInterface = await CreateNetworkInterfaceAsync(networkClient, vmConfig, subnet, publicIp);

        // Create the VM
        var vm = await CreateVirtualMachineAsync(computeClient, vmConfig, networkInterface);

        // Wait for the VM to be fully provisioned
        await WaitForVmProvisioningAsync(computeClient, vmConfig.ResourceGroupName, vmConfig.Name);

        // Run commands on the VM
        string resultMessage = await RunCommandOnVmAsync(computeClient, vmConfig.ResourceGroupName, vmConfig.Name, vmConfig);
        Console.WriteLine(resultMessage);

        Console.WriteLine($"VM '{vmConfig.Name}' created and commands have been executed.");

        // Delete resources after tests
        await DeleteResourcesAsync(computeClient, networkClient, resourceClient, vmConfig);

        Console.WriteLine($"Resources for VM '{vmConfig.Name}' have been deleted.");
    }

    // Function to create a virtual network
    private static async Task<VirtualNetwork> CreateVirtualNetworkAsync(NetworkManagementClient networkClient, VmConfiguration vmConfig)
    {
        var vnetParams = new VirtualNetwork
        {
            Location = vmConfig.Location,
            AddressSpace = new AddressSpace { AddressPrefixes = new[] { vmConfig.Network.AddressPrefix } },
            Subnets = new[]
            {
                new Subnet
                {
                    Name = vmConfig.Network.SubnetName,
                    AddressPrefix = vmConfig.Network.SubnetPrefix
                }
            }
        };
        Console.WriteLine("Creating virtual network...");
        return await networkClient.VirtualNetworks.CreateOrUpdateAsync(vmConfig.ResourceGroupName, vmConfig.Network.VirtualNetworkName, vnetParams);
    }

    // Function to create a public IP address
    private static async Task<PublicIPAddress> CreatePublicIPAddressAsync(NetworkManagementClient networkClient, VmConfiguration vmConfig)
    {
        var uniqueDnsLabel = "mydnslabel-" + Guid.NewGuid().ToString().Substring(0, 6);

        var publicIpParams = new PublicIPAddress
        {
            Location = vmConfig.Location,
            PublicIPAllocationMethod = IPAllocationMethod.Dynamic,
            DnsSettings = new PublicIPAddressDnsSettings { DomainNameLabel = uniqueDnsLabel }
        };
        Console.WriteLine("Creating public IP address...");
        return await networkClient.PublicIPAddresses.CreateOrUpdateAsync(vmConfig.ResourceGroupName, vmConfig.PublicIpName, publicIpParams);
    }

    // Function to create a network interface
    private static async Task<NetworkInterface> CreateNetworkInterfaceAsync(NetworkManagementClient networkClient, VmConfiguration vmConfig, Subnet subnet, PublicIPAddress publicIp)
    {
        var nicParams = new NetworkInterface
        {
            Location = vmConfig.Location,
            IpConfigurations = new[]
            {
                new NetworkInterfaceIPConfiguration
                {
                    Name = "myIPConfig",
                    Subnet = subnet,
                    PublicIPAddress = publicIp
                }
            }
        };
        Console.WriteLine("Creating network interface...");
        return await networkClient.NetworkInterfaces.CreateOrUpdateAsync(vmConfig.ResourceGroupName, vmConfig.NetworkInterfaceName, nicParams);
    }

    // Function to create a virtual machine
    private static async Task<VirtualMachine> CreateVirtualMachineAsync(ComputeManagementClient computeClient, VmConfiguration vmConfig, NetworkInterface networkInterface)
    {
        var vmParams = new VirtualMachine
        {
            Location = vmConfig.Location,
            OsProfile = new OSProfile
            {
                AdminUsername = vmConfig.AdminUsername,
                AdminPassword = vmConfig.AdminPassword,
                ComputerName = vmConfig.Name
            },
            HardwareProfile = new HardwareProfile
            {
                VmSize = vmConfig.VmSize
            },
            NetworkProfile = new Microsoft.Azure.Management.Compute.Models.NetworkProfile
            {
                NetworkInterfaces = new[]
                {
                    new NetworkInterfaceReference { Id = networkInterface.Id }
                }
            },
            StorageProfile = new StorageProfile
            {
                ImageReference = new ImageReference
                {
                    Publisher = "Canonical",
                    Offer = "UbuntuServer",
                    Sku = "18.04-LTS",
                    Version = "latest"
                },
                OsDisk = new OSDisk
                {
                    Name = vmConfig.OsDiskName,
                    CreateOption = DiskCreateOptionTypes.FromImage,
                    ManagedDisk = new ManagedDiskParameters { StorageAccountType = StorageAccountTypes.StandardLRS }
                }
            }
        };
        Console.WriteLine("Creating virtual machine...");
        return await computeClient.VirtualMachines.CreateOrUpdateAsync(vmConfig.ResourceGroupName, vmConfig.Name, vmParams);
    }

    // Function to wait for VM provisioning
    private static async Task WaitForVmProvisioningAsync(ComputeManagementClient computeClient, string resourceGroupName, string vmName)
    {
        VirtualMachine vm;
        do
        {
            vm = await computeClient.VirtualMachines.GetAsync(resourceGroupName, vmName);
            Console.WriteLine($"Current VM provisioning state: {vm.ProvisioningState}");
            if (vm.ProvisioningState == "Succeeded")
            {
                break;
            }
            await Task.Delay(5000); // Wait for 5 seconds before checking again
        } while (true);
    }

    // Function to run commands on the VM
    private static async Task<string> RunCommandOnVmAsync(ComputeManagementClient computeClient, string resourceGroupName, string vmName, VmConfiguration vmConfig)
    {
        Console.WriteLine("Running Tests on VM...");
        var runCommandParams = new RunCommandInput
        {
            CommandId = "RunShellScript",
            Script = new[]
            {
                "sudo apt-get update > /dev/null 2>&1",
                "sudo apt-get install sysbench -y > /dev/null 2>&1",
                "cpu_time=$(sysbench cpu --cpu-max-prime=20000 --time=0 --events=2000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//')",
                "memory_time=$(sysbench memory --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//')",
                "sysbench fileio --file-test-mode=seqwr prepare > /dev/null 2>&1",
                "fileio_time=$(sysbench fileio --file-test-mode=seqwr --time=0 --events=1000 run 2>/dev/null | grep 'total time:' | awk '{print $3}' | sed 's/s//')",
                "sysbench fileio --file-test-mode=seqwr cleanup > /dev/null 2>&1",
                "echo $cpu_time",
                "echo $memory_time",
                "echo $fileio_time"
            }
        };

        try
        {
            var result = await computeClient.VirtualMachines.RunCommandAsync(resourceGroupName, vmName, runCommandParams);

            Console.WriteLine("Successfully ran commands on VM");

            if (result.Value != null && result.Value.Count > 0)
            {
                var output = result.Value[0].Message;

                // Split the output into lines and trim each line
                var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(line => line.Trim())
                                .Where(line => !string.IsNullOrWhiteSpace(line))
                                .ToList();

                // List to store the total times
                List<double> totalTimes = new List<double>();

                foreach (var line in lines)
                {
                    // Try to parse the line as a double
                    if (double.TryParse(line, NumberStyles.Any, CultureInfo.InvariantCulture, out double time))
                    {
                        totalTimes.Add(time);
                    }
                    else
                    {
                        //Console.WriteLine($"Failed to parse line as double: '{line}'");
                    }
                }

                if (totalTimes.Count == 3)
                {
                    Console.WriteLine($"CPU Test Time: {totalTimes[0]} seconds");
                    Console.WriteLine($"Memory Test Time: {totalTimes[1]} seconds");
                    Console.WriteLine($"File I/O Test Time: {totalTimes[2]} seconds");
                    double totalTime = totalTimes.Sum();

                    //Adding the data to MongoDB
                    var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("DB_CONNECTION_STRING environment variable is not set.");
                    }
                    var cloudPerformanceData = new MongoDbService(connectionString, "CloudPerformanceData", "CloudPerformanceData");

                    var data = new CloudPerformanceData
                    {
                        Provider = "Azure",
                        VmSize = vmConfig.VmSize,
                        Location = vmConfig.Location,
                        CPU = totalTimes[0].ToString(),
                        Memory = totalTimes[1].ToString(),
                        Disk = totalTimes[2].ToString(),
                        totalTime = totalTime.ToString(),
                        Os = "Ubuntu 18.04",
                        Date = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss")
                    };

                    cloudPerformanceData.InsertData(data);
                    
                    return $"Total time taken to run all tests: {totalTime} seconds";
                }
                else
                {
                    return "Could not parse all test times from the output.";
                }
            }
            else
            {
                return "No output returned from sysbench command.";
            }
        }
        catch (CloudException ex)
        {
            Console.WriteLine("Error occurred during command execution:");
            Console.WriteLine($"Code: {ex.Body.Code}");
            Console.WriteLine($"Message: {ex.Body.Message}");
            return "An error occurred during command execution.";
        }
        catch (Exception ex)
        {
            Console.WriteLine("An unexpected error occurred:");
            Console.WriteLine(ex.Message);
            return "An unexpected error occurred during command execution.";
        }
    }

    // Function to delete resources
    private static async Task DeleteResourcesAsync(
        ComputeManagementClient computeClient,
        NetworkManagementClient networkClient,
        ResourceManagementClient resourceClient,
        VmConfiguration vmConfig)
    {
        // Delete the virtual machine
        await DeleteVirtualMachineAsync(computeClient, vmConfig.ResourceGroupName, vmConfig.Name);

        // Delete network interface
        await DeleteNetworkInterfaceAsync(networkClient, vmConfig.ResourceGroupName, vmConfig.NetworkInterfaceName);

        // Delete public IP address
        await DeletePublicIpAddressAsync(networkClient, vmConfig.ResourceGroupName, vmConfig.PublicIpName);

        // Delete virtual network
        await DeleteVirtualNetworkAsync(networkClient, vmConfig.ResourceGroupName, vmConfig.Network.VirtualNetworkName);

        // Delete disk
        await DeleteDiskAsync(computeClient, vmConfig.ResourceGroupName, vmConfig.OsDiskName);

        // Optionally, delete the resource group
        await DeleteResourceGroupAsync(resourceClient, vmConfig.ResourceGroupName);
    }

    // Function to delete virtual machine
    private static async Task DeleteVirtualMachineAsync(ComputeManagementClient computeClient, string resourceGroupName, string vmName)
    {
        try
        {
            Console.WriteLine($"Deleting virtual machine '{vmName}'...");
            await computeClient.VirtualMachines.DeleteAsync(resourceGroupName, vmName);
            Console.WriteLine("Virtual machine deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting virtual machine '{vmName}': {ex.Message}");
        }
    }

    // Function to delete network interface
    private static async Task DeleteNetworkInterfaceAsync(NetworkManagementClient networkClient, string resourceGroupName, string networkInterfaceName)
    {
        try
        {
            Console.WriteLine($"Deleting network interface '{networkInterfaceName}'...");
            await networkClient.NetworkInterfaces.DeleteAsync(resourceGroupName, networkInterfaceName);
            Console.WriteLine("Network interface deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting network interface '{networkInterfaceName}': {ex.Message}");
        }
    }

    // Function to delete public IP address
    private static async Task DeletePublicIpAddressAsync(NetworkManagementClient networkClient, string resourceGroupName, string publicIpName)
    {
        try
        {
            Console.WriteLine($"Deleting public IP address '{publicIpName}'...");
            await networkClient.PublicIPAddresses.DeleteAsync(resourceGroupName, publicIpName);
            Console.WriteLine("Public IP address deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting public IP address '{publicIpName}': {ex.Message}");
        }
    }

    // Function to delete virtual network
    private static async Task DeleteVirtualNetworkAsync(NetworkManagementClient networkClient, string resourceGroupName, string virtualNetworkName)
    {
        try
        {
            Console.WriteLine($"Deleting virtual network '{virtualNetworkName}'...");
            await networkClient.VirtualNetworks.DeleteAsync(resourceGroupName, virtualNetworkName);
            Console.WriteLine("Virtual network deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting virtual network '{virtualNetworkName}': {ex.Message}");
        }
    }

    // Function to delete disk
    private static async Task DeleteDiskAsync(ComputeManagementClient computeClient, string resourceGroupName, string diskName)
    {
        try
        {
            Console.WriteLine($"Deleting disk '{diskName}'...");
            await computeClient.Disks.DeleteAsync(resourceGroupName, diskName);
            Console.WriteLine("Disk deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting disk '{diskName}': {ex.Message}");
        }
    }

    // Function to delete resource group
    private static async Task DeleteResourceGroupAsync(ResourceManagementClient resourceClient, string resourceGroupName)
    {
        try
        {
            Console.WriteLine($"Deleting resource group '{resourceGroupName}'...");
            await resourceClient.ResourceGroups.DeleteAsync(resourceGroupName);
            Console.WriteLine("Resource group deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting resource group '{resourceGroupName}': {ex.Message}");
        }
    }
}
