using System.Collections.Generic;
using System.Text.Json.Serialization;

public class VmConfigurations
{
    [JsonPropertyName("vms")]
    public required List<VmConfiguration> Vms { get; set; }
}


public class VmConfiguration
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("resourceGroupName")]
    public required string ResourceGroupName { get; set; }

    [JsonPropertyName("location")]
    public required string Location { get; set; }

    [JsonPropertyName("vmSize")]
    public required string VmSize { get; set; }

    [JsonPropertyName("adminUsername")]
    public required string AdminUsername { get; set; }

    [JsonPropertyName("adminPassword")]
    public required string AdminPassword { get; set; }

    [JsonPropertyName("network")]
    public required NetworkConfiguration Network { get; set; }

    [JsonPropertyName("publicIpName")]
    public required string PublicIpName { get; set; }

    [JsonPropertyName("networkInterfaceName")]
    public required string NetworkInterfaceName { get; set; }

    [JsonPropertyName("osDiskName")]
    public required string OsDiskName { get; set; }
}

public class NetworkConfiguration
{
    [JsonPropertyName("virtualNetworkName")]
    public required string VirtualNetworkName { get; set; }

    [JsonPropertyName("subnetName")]
    public required string SubnetName { get; set; }

    [JsonPropertyName("addressPrefix")]
    public required string AddressPrefix { get; set; }

    [JsonPropertyName("subnetPrefix")]
    public required string SubnetPrefix { get; set; }
}

