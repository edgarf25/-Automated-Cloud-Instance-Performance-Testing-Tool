using DotNetEnv;
using Microsoft.Identity.Client;
using System;
using System.Threading.Tasks;

// This class is responsible for getting the access token from Azure AD
class TokenService
{
    public static async Task<string> GetAccessTokenAsync()
    {
        // Load the .env file at the beginning of the method
        Env.Load();

        Console.WriteLine("[AZURE] " + Environment.GetEnvironmentVariable("AZURE_TENANT_ID"));

        // Retrieve the environment variables
        string tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") ?? throw new InvalidOperationException("AZURE_TENANT_ID environment variable is not set.");
        string clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? throw new InvalidOperationException("AZURE_CLIENT_ID environment variable is not set.");
        string clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") ?? throw new InvalidOperationException("AZURE_CLIENT_SECRET environment variable is not set.");
        string authority = $"https://login.microsoftonline.com/{tenantId}";

        IConfidentialClientApplication app = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri(authority))
            .Build();

        string[] scopes = new string[] { "https://management.azure.com/.default" };

        // Acquire the token
        AuthenticationResult result = await app.AcquireTokenForClient(scopes)
            .ExecuteAsync();

        return result.AccessToken;
    }
}
