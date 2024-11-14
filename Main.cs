using System;
using System.IO;
using System.Threading.Tasks;
using EC2SysbenchTest;
using GCPInstanceManager;
using AZInstanceManager;

public class UserInterface
{
    public static async Task Main(string[] args)
    {
        string[] customTests = new string[3];
        Console.WriteLine("Welcome to our Cloud Testing Performance Tool!");
        Console.WriteLine("If this is your first time running this application, please type 'setup' to configure the application.");
     

        while (true)
        {
            Console.WriteLine("Enter 'run' to execute the performance tests, 'setup' to run initial setup,  or 'exit' to quit:");
            Console.Write("> ");
            string input = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

            if (input == "exit")
            {
                Console.WriteLine("Exiting the application.");
                break;
            }

            switch(input)
            {
                case "run":
                    string testType = "first time";

                    while(testType != "menu"){
                        Console.WriteLine("Type 'standard' to run the normal performance tests, 'custom' to run custom tests, or 'menu' to return to main menu:");
                        Console.Write("> ");
                        testType = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                            switch(testType)
                            {
                                case "standard":
                                    //we might be able to run all these three programs in parallel for quicker test times especially since mine takes like 5 minutes
                                    Task task1 = AWSProgram.AwsRun(args);
                                    //Task task2 = AZRunTests.Run(args);
                                    //Task task3 = GCPRunTests.Run(args);

                                    try
                                    {
                                        // Await all tasks to complete
                                        //await Task.WhenAll(task1, task2, task3);
                                        await Task.WhenAll(task1);
                                        Console.WriteLine("All Performance tests have successfully run.");
                                    }
                                    catch (Exception ex)
                                    {
                                        // Handle exceptions
                                        Console.WriteLine(ex);
                                    }     
                                    break;
                                case "custom":
                                    Console.WriteLine("How many providers would you like to run a performance test on? one, two or three?(only AWS, Azure, and GCP are supported)");
                                    Console.Write("> ");
                                    string providerCount = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                    string? provider1 = null, provider2 = null, provider3 = null;

                                    switch(providerCount){
                                        case "one":
                                        case "1":
                                            Console.WriteLine("Enter the provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider1 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[0] = provider1;
                                            Console.WriteLine("Running custom test for one provider");
                                            await RunTests(customTests);
                                            Array.Fill(customTests, null);//resetting the array incase the user wants to run another test

                                            // WILL ADD THIS IN UPCOMING UPDATES
                                            // Console.WriteLine("Enter the instance type you would like to test, e.g. t2.micro, Standard_B1s, f1-micro");
                                            // Console.Write("> ");
                                            // string instanceType = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            // Console.WriteLine("Enter the region you would like to test, e.g. us-east-1, eastus, us-central1");
                                            // Console.Write("> ");
                                            // string region = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

                                            
                                            break;
                                        case "two":
                                        case "2":
                                            Console.WriteLine("Enter the first provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider1 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[0] = provider1;

                                            Console.WriteLine("Enter the second provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider2 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[1] = provider2;
                                            
                                            Console.WriteLine("Running custom test for two providers");
                                            await RunTests(customTests);
                                            Array.Fill(customTests, null);//resetting the array incase the user wants to run another test
                                            break;
                                        case "three":
                                        case "3":
                                            Console.WriteLine("Enter the first provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider1 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[0] = provider1;

                                            Console.WriteLine("Enter the second provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider2 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[1] = provider2;

                                            Console.WriteLine("Enter the third provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            provider3 = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                            customTests[2] = provider3;
                                            
                                            Console.WriteLine("Running custom test for three providers");
                                            await RunTests(customTests);
                                            Array.Fill(customTests, null);//resetting the array incase the user wants to run another test
                                            break;
                                        default:
                                            Console.WriteLine("Invalid number of providers. Please enter 'one', 'two', or 'three'.");
                                            break;
                                    }
                                    break;
                                case "menu":
                                    Console.WriteLine("Returning to main menu.");
                                    break;
                                default:
                                    Console.WriteLine("Invalid command. Please enter 'normal', 'menu' or 'custom'.");
                                    break;
                            }
                    }
                    break;
                case "setup":
                    Console.WriteLine("Here we will guide you through the initial setup of the application.");
                    Console.WriteLine("Setting up environment variables...");
                    Console.WriteLine("Azure:\n");
                    Console.Write("Enter Azure Tenant ID: ");
                    string azTenantID = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter App ID: ");
                    string azClientID = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter password: ");
                    string azClientSecret = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter ID: ");
                    string azSubscriptionID = Console.ReadLine() ?? string.Empty;

                    Console.Write("\nAWS:\n");
                    Console.Write("Enter Security Group ID: ");
                    string awsSecGroupID = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter key-pair name: ");
                    string awsKeyPair = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter Subnet ID: ");
                    string awsSubnetID = Console.ReadLine() ?? string.Empty;
                    Console.Write("Enter IAM Role Name: ");
                    string awsIAMRole = Console.ReadLine() ?? string.Empty;

                    Console.WriteLine("\nGCP: \n");
                    Console.Write("Enter Project ID: ");
                    string gcpProjectID = Console.ReadLine() ?? string.Empty;

                    Console.WriteLine("Database: \n");
                    Console.Write("Enter Database Key: ");
                    string dbConnectionString = Console.ReadLine() ?? string.Empty;



                    string filePath = ".env";
                    string[] lines = {
                        $"AZURE_TENANT_ID = {azTenantID}",
                        $"AZURE_CLIENT_ID = {azClientID}",
                        $"AZURE_CLIENT_SECRET = {azClientSecret}",
                        $"AZURE_SUBSCRIPTION_ID = {azSubscriptionID}",
                        $"AWS_SECURITY_GROUP_ID = {awsSecGroupID}",
                        $"AWS_KEY_PAIR_NAME = {awsKeyPair}",
                        $"AWS_SUBNET_ID = {awsSubnetID}",
                        $"AWS_IAM_ROLE = {awsIAMRole}",
                        $"DB_CONNECTION_STRING = {dbConnectionString}",
                        $"PROJECT_ID = {gcpProjectID}",
                    };

                    File.WriteAllLines(filePath, lines);
                    Console.WriteLine(".env file created");


                    break;
                default:
                    Console.WriteLine("Invalid command. Please enter 'run' 'setup' or 'exit'.");
                    break;
            }
        }
    }

    public static async Task RunTests(string[] providers)
    {
        int nonNullCount = providers.Count(value => value != null);
        Console.WriteLine($"Running performance tests for {nonNullCount} provider(s).");

        if (nonNullCount == 1)
        {
            // Single provider case
            if (providers[0] == "aws")
            {
                await AWSProgram.AwsRun(providers);
            }
            else if (providers[0] == "azure")
            {
                await AZRunTests.Run(providers);
            }
            else if (providers[0] == "gcp")
            {
                await GCPRunTests.Run(providers);
            }
            else
            {
                Console.WriteLine("Invalid provider. Please enter 'AWS', 'Azure', or 'GCP'.");
            }
        }
        else if (nonNullCount == 2) //ADD HOW TO HANDLE IF USER ENTERS THE SAME PROVIDER TWICE
        {
            Task? taskAWS = null, taskAZ = null, taskGCP = null;

            if (providers[0] == "aws" || providers[1] == "aws")
            {
                taskAWS = AWSProgram.AwsRun(providers);
            }
            if (providers[0] == "azure" || providers[1] == "azure")
            {
                taskAZ = AZRunTests.Run(providers);
            }
            if (providers[0] == "gcp" || providers[1] == "gcp")
            {
                taskGCP = GCPRunTests.Run(providers);
            }

            // Filter out null tasks and await them in parallel
            var tasks = new[] { taskAWS, taskAZ, taskGCP }.Where(task => task != null).Cast<Task>().ToArray();

            if (tasks.Length > 0)
            {
                await Task.WhenAll(tasks);
            }
            else
            {
                Console.WriteLine("Invalid providers. Please enter 'AWS', 'Azure', or 'GCP'.");
            }
        }
        else //ADD HOW TO HANDLE IF USER ENTERS THE SAME PROVIDER THREEE TIMES
        {
            Task? taskAWS = AWSProgram.AwsRun(providers);
            Task? taskAZ = AZRunTests.Run(providers);
            Task? taskGCP = GCPRunTests.Run(providers);
            await Task.WhenAll(taskAWS, taskAZ, taskGCP);
        }
    }

}
