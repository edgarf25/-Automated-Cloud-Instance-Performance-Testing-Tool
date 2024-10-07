using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.Network;
using Microsoft.Azure.Management.Network.Models;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using System.Globalization;


class Program
{
    private static string subscriptionId = "GET ID AND REPLACE";
    private static string resourceGroupName = "myResourceGroupAutomation";
    private static string vmName = "myVM";
    private static string location = "westus";
    private static string virtualNetworkName = "myVNet";
    private static string subnetName = "mySubnet";
    private static string publicIpName = "myPublicIP";
    private static string networkInterfaceName = "myNIC";
    private static string osDiskName = "myOsDisk";

    static async Task Main(string[] args)
    {
        // Authenticate using the access token
        string accessToken = await TokenService.GetAccessTokenAsync();
        var tokenCredentials = new TokenCredentials(accessToken);

        // Create the Resource Group
        var resourceClient = new ResourceManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };
        await resourceClient.ResourceGroups.CreateOrUpdateAsync(resourceGroupName, new ResourceGroup(location));

        // Create network resources for the VM
        var networkClient = new NetworkManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };
        var virtualNetwork = await CreateVirtualNetworkAsync(networkClient, resourceGroupName, location);
        var subnet = virtualNetwork.Subnets[0];
        var publicIp = await CreatePublicIPAddressAsync(networkClient, resourceGroupName, location);
        var networkInterface = await CreateNetworkInterfaceAsync(networkClient, resourceGroupName, location, subnet, publicIp);

        // Create the VM
        var computeClient = new ComputeManagementClient(tokenCredentials) { SubscriptionId = subscriptionId };
        var vm = await CreateVirtualMachineAsync(computeClient, resourceGroupName, location, networkInterface, vmName);

        // Wait for the VM to be fully provisioned
        await WaitForVmProvisioningAsync(computeClient, resourceGroupName, vmName);

        // Run commands on the VM
        string resultMessage = await RunCommandOnVmAsync(computeClient, resourceGroupName, vmName);
        Console.WriteLine(resultMessage);

        Console.WriteLine("VM created and commands have been executed.");

        // Delete resources after tests
        await DeleteResourcesAsync(computeClient, networkClient, resourceClient, resourceGroupName, vmName);
        Console.WriteLine("Resources have been deleted.");
    }

    // Function to create a virtual network
    private static async Task<VirtualNetwork> CreateVirtualNetworkAsync(NetworkManagementClient networkClient, string resourceGroupName, string location)
    {
        var vnetParams = new VirtualNetwork
        {
            Location = location,
            AddressSpace = new AddressSpace { AddressPrefixes = new[] { "10.0.0.0/16" } },
            Subnets = new[] { new Subnet { Name = subnetName, AddressPrefix = "10.0.0.0/24" } }
        };
        Console.WriteLine("Successfully created virtual network");
        return await networkClient.VirtualNetworks.CreateOrUpdateAsync(resourceGroupName, virtualNetworkName, vnetParams);
    }


    // Function to create a public IP address
    private static async Task<PublicIPAddress> CreatePublicIPAddressAsync(NetworkManagementClient networkClient, string resourceGroupName, string location)
    {
        // Below is the name of the DNS label for the public IP address
        var uniqueDnsLabel = "mydnslabel-" + Guid.NewGuid().ToString().Substring(0, 6);  // Appending a random suffix

        var publicIpParams = new PublicIPAddress
        {
            Location = location,
            PublicIPAllocationMethod = IPAllocationMethod.Dynamic,
            DnsSettings = new PublicIPAddressDnsSettings { DomainNameLabel = uniqueDnsLabel }
        };
        Console.WriteLine("Successfully created public IP address");
        return await networkClient.PublicIPAddresses.CreateOrUpdateAsync(resourceGroupName, publicIpName, publicIpParams);
    }


    // Function to create a network interface
    private static async Task<NetworkInterface> CreateNetworkInterfaceAsync(NetworkManagementClient networkClient, string resourceGroupName, string location, Subnet subnet, PublicIPAddress publicIp)
    {
        var nicParams = new NetworkInterface
        {
            Location = location,
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
        Console.WriteLine("Successfully created network interface");
        return await networkClient.NetworkInterfaces.CreateOrUpdateAsync(resourceGroupName, networkInterfaceName, nicParams);
    }


    // Function to create a virtual machine
    private static async Task<VirtualMachine> CreateVirtualMachineAsync(ComputeManagementClient computeClient, string resourceGroupName, string location, NetworkInterface networkInterface, string vmName)
    {
        var vmParams = new VirtualMachine
        {
            Location = location,
            OsProfile = new OSProfile
            {
                AdminUsername = "USER",    // Define username here
                AdminPassword = "PASSWORD", // Define password here
                ComputerName = vmName
            },
            HardwareProfile = new HardwareProfile
            {
                VmSize = VirtualMachineSizeTypes.StandardB1s // Define VM size here
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
                    Publisher = "Canonical", // Define publisher here
                    Offer = "UbuntuServer", // Define offer here
                    Sku = "18.04-LTS", // Define SKU here
                    Version = "latest" // Define version here
                },
                OsDisk = new OSDisk
                {
                    Name = osDiskName,
                    CreateOption = DiskCreateOptionTypes.FromImage, // Define disk creation option here
                    ManagedDisk = new ManagedDiskParameters { StorageAccountType = StorageAccountTypes.StandardLRS } // Define storage account type here
                }
            }
        };
        Console.WriteLine("Successfully created virtual machine");
        return await computeClient.VirtualMachines.CreateOrUpdateAsync(resourceGroupName, vmName, vmParams);
    }


    //function to wait for VM provisioning
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


    // Apply Custom Script Extension to run sysbench
    private static async Task<string> RunCommandOnVmAsync(ComputeManagementClient computeClient, string resourceGroupName, string vmName)
    {
        var runCommandParams = new RunCommandInput
        {
            CommandId = "RunShellScript",
            Script = new[] // Commands to run on the VM
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
            var result = await computeClient.VirtualMachines.RunCommandAsync(
                resourceGroupName, vmName, runCommandParams);

            Console.WriteLine("Successfully ran commands on VM");

            if (result.Value != null && result.Value.Count > 0)
            {
                var output = result.Value[0].Message;

                // Printing the raw output for debugging
                Console.WriteLine("Raw output from VM:");
                Console.WriteLine(output);

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
                    // else
                    // {
                    //     Console.WriteLine($"Failed to parse line as double: '{line}'");
                    // }
                }

                if (totalTimes.Count == 3)
                {
                    Console.WriteLine($"CPU Test Time: {totalTimes[0]} seconds");
                    Console.WriteLine($"Memory Test Time: {totalTimes[1]} seconds");
                    Console.WriteLine($"File I/O Test Time: {totalTimes[2]} seconds");
                    double totalTime = totalTimes.Sum();
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

    private static async Task DeleteResourcesAsync(
    ComputeManagementClient computeClient,
    NetworkManagementClient networkClient,
    ResourceManagementClient resourceClient,
    string resourceGroupName,
    string vmName)
    {
        // Delete the virtual machine
        await DeleteVirtualMachineAsync(computeClient, resourceGroupName, vmName);

        // Delete network interface
        await DeleteNetworkInterfaceAsync(networkClient, resourceGroupName, networkInterfaceName);

        // Delete public IP address
        await DeletePublicIpAddressAsync(networkClient, resourceGroupName, publicIpName);

        // Delete virtual network
        await DeleteVirtualNetworkAsync(networkClient, resourceGroupName, virtualNetworkName);

        // Delete disk
        await DeleteDiskAsync(computeClient, resourceGroupName, osDiskName);

        // delete the resource group
        await DeleteResourceGroupAsync(resourceClient, resourceGroupName);
    }

}
