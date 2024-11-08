using System;
using System.Threading.Tasks;
using EC2SysbenchTest;
using GCPInstanceManager;

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
                                    //await Program.Run(args);
                                    //await AWSProgram.AwsRun(args);
                                    await GCPRunTests.Run(args);
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
                    break;
                default:
                    Console.WriteLine("Invalid command. Please enter 'run' 'credits' or 'exit'.");
                    break;
            }
        }
    }
}
