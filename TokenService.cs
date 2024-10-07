using Microsoft.Identity.Client;
using System;
using System.Threading.Tasks;

// This class is responsible for getting the access token from Azure AD
class TokenService
{
    private static string tenantId = "GET ID AND REPLACE";
    private static string clientId = "GET ID AND REPLACE";
    private static string clientSecret = "GET SECRET AND REPLACE";
    private static string authority = $"https://login.microsoftonline.com/{tenantId}";

    public static async Task<string> GetAccessTokenAsync()
    {
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
