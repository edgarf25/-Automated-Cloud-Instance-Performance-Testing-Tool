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
        Console.WriteLine("Welcome to our Cloud Testing Performance Tool!");
        Console.WriteLine("If this is your first time running this application, please type 'setup' to configure the application.");

        // Example of getting user input      

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
                        Console.WriteLine("Type 'normal' to run the normal performance tests, 'custom' to run custom tests, or 'menu' to return to main menu:");
                        Console.Write("> ");
                        testType = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                            switch(testType)
                            {
                                case "normal":
                                    //we might be able to run all these three programs in parallel for quicker test times especially since mine takes like 5 minutes
                                    try{
                                        await AWSProgram.AwsRun(args);
                                    }
                                    catch(Exception ex){
                                        Console.WriteLine($"AWS tests failed: {ex.Message}");
                                        Console.WriteLine("Moving to the next provider...");
                                    }
                                    
                                    try{
                                        await AZRunTests.Run(args);
                                    }
                                    catch(Exception ex){
                                        Console.WriteLine($"Azure tests failed: {ex.Message}");
                                        Console.WriteLine("Moving to the next provider...");
                                    }
                                    
                                    try{
                                        await GCPRunTests.Run(args);
                                    }
                                    catch(Exception ex){
                                        Console.WriteLine($"GCP tests failed: {ex.Message}");
                                        Console.WriteLine("Moving to the next provider...");
                                    }
                                    
                                    Console.WriteLine("All Performance tests have successfully run.");
                                    break;
                                case "custom":
                                    Console.WriteLine("How many providers would you like to run a performance test on? one, two or three?(only AWS, Azure, and GCP are supported)");
                                    Console.Write("> ");
                                    string providerCount = (Console.ReadLine() ?? string.Empty).Trim().ToLower();
                                    switch(providerCount){
                                        case "one":
                                            Console.WriteLine("Enter the provider you would like to test: AWS, Azure, or GCP");
                                            Console.Write("> ");
                                            string provider = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

                                            Console.WriteLine("Enter the instance type you would like to test, e.g. t2.micro, Standard_B1s, f1-micro");
                                            Console.Write("> ");
                                            string instanceType = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

                                            Console.WriteLine("Enter the region you would like to test, e.g. us-east-1, eastus, us-central1");
                                            Console.Write("> ");
                                            string region = (Console.ReadLine() ?? string.Empty).Trim().ToLower();

                                            
                                            break;
                                        case "two":
                                            Console.WriteLine("Running custom test for two providers");
                                            break;
                                        case "three":
                                            Console.WriteLine("Running custom test for three providers");
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
                    string azTenantID = Console.ReadLine();
                    Console.Write("Enter App ID: ");
                    string azClientID = Console.ReadLine();
                    Console.Write("Enter password: ");
                    string azClientSecret = Console.ReadLine();
                    Console.Write("Enter ID: ");
                    string azSubscriptionID = Console.ReadLine();

                    Console.Write("\nAWS:\n");
                    Console.Write("Enter Security Group ID: ");
                    string awsSecGroupID = Console.ReadLine();
                    Console.Write("Enter key-pair name: ");
                    string awsKeyPair = Console.ReadLine();
                    Console.Write("Enter Subnet ID: ");
                    string awsSubnetID = Console.ReadLine();
                    Console.Write("Enter IAM Role Name: ");
                    string awsIAMRole = Console.ReadLine();

                    Console.WriteLine("\nGCP: \n");
                    Console.Write("Enter Project ID: ");
                    string gcpProjectID = Console.ReadLine();

                    Console.WriteLine("Database: \n");
                    Console.Write("Enter Database Key: ");
                    string dbConnectionString = Console.ReadLine();



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
                    Console.WriteLine("Invalid command. Please enter 'run' 'credits' or 'exit'.");
                    break;
            }
        }
    }
}
