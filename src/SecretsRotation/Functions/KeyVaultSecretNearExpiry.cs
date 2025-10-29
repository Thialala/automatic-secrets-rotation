namespace SecretsRotation.Functions;

/// <summary>
/// Azure Function that automatically rotates secrets for Azure DevOps service connections.
/// Triggered by Key Vault events (SecretNearExpiry/SecretExpired) delivered via Event Grid to a Storage Queue.
/// </summary>
/// <remarks>
/// This function orchestrates the complete secret rotation process:
/// 1. Retrieves the expiring secret from Key Vault
/// 2. Generates a new password in the associated Azure AD Application
/// 3. Updates the secret in Key Vault with the new password
/// 4. Updates the corresponding Azure DevOps service connection
/// 
/// Required Secret Tags:
/// - azureADAppId: The Azure AD Application ID
/// - azureDevOpsAccountUrl: Azure DevOps organization URL
/// - azureDevOpsProjectName: Project name in Azure DevOps
/// - azureDevOpsConnectionName: Service connection name
/// - SecretDurationInMonths: Duration for the new secret (e.g., 6)
/// </remarks>
public class KeyVaultSecretNearExpiry
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the KeyVaultSecretNearExpiry function.
    /// </summary>
    /// <param name="configuration">Configuration containing the Managed Identity Client ID</param>
    public KeyVaultSecretNearExpiry(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Main function entry point triggered by messages in the secrets queue.
    /// Processes Event Grid events for Key Vault secrets near expiry and rotates them.
    /// </summary>
    /// <param name="queueItem">Serialized Event Grid event containing secret information</param>
    /// <param name="log">Logger for tracking execution and diagnostics</param>
    [FunctionName("KeyVaultSecretNearExpiry")]
    public async Task Run([QueueTrigger("kv-secrets-near-expiry", Connection = "AzureWebJobsStorage")] string queueItem, ILogger log)
    {
        try
        {
            log.LogInformation($"Received queue message: {queueItem}");

            // Deserialize Event Grid event from the queue message
            var eventData = JsonSerializer.Deserialize<EventGridData>(queueItem);

            // Extract Key Vault and secret information from the event
            var secretName = eventData.Data.ObjectName;
            var keyVaultName = eventData.Data.VaultName;
            var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net");

            log.LogInformation($"Secret value: {secretName}");
            log.LogInformation($"Keyvault name: {keyVaultName}");
            log.LogInformation($"ClientId: {_configuration["ManagedIdentityClientId"]}");

            // Create credential and Key Vault client using managed identity
            var credential = GetChainedTokenCredential();
            var secretClient = new SecretClient(keyVaultUri, credential);
            
            // Retrieve the secret and its metadata tags
            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
            var tags = secret.Properties.Tags;

            foreach (var tag in tags)
            {
                log.LogInformation($"{tag}");
            }

            // Initialize Microsoft Graph client for Azure AD operations
            var graphClient = new GraphServiceClient(credential);

            // Find the Azure AD Application using the App ID from tags
            Application azureADApp = await GetAzureADApp(graphClient, tags["azureADAppId"]);

            if (azureADApp != null)
            {
                // Step 1: Generate new password in Azure AD Application
                var secretEndDateTime = GetSecretExpiryDate(tags);
                var newPassword = await GenerateNewSecret(graphClient, azureADApp, secret.Name, secretEndDateTime);
                log.LogInformation($"Secret of the AAD App '{azureADApp.DisplayName}' successfully updated with expiration date: {secretEndDateTime}.");

                // Step 2: Update the secret in Key Vault with the new password
                await UpdateSecretInKeyVault(secretClient, secret, newPassword, secretEndDateTime);
                log.LogInformation($"Secret '{secret.Name}' successfully updated in the Keyvault.");

                // Step 3: Update the service connection in Azure DevOps with the new password
                await UpdateServiceConnectionInAzureDevOps(credential, tags, newPassword);
                log.LogInformation($"Secret of the SPN of the Service Connection '{tags["azureDevOpsConnectionName"]}' successfully updated.");
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, ex.Message);
            // Re-throw to trigger queue retry mechanism
            throw;
        }
    }

    /// <summary>
    /// Creates a chained token credential combining DefaultAzureCredential and ManagedIdentityCredential.
    /// This provides authentication for Azure services, Microsoft Graph API, and Azure DevOps.
    /// </summary>
    /// <returns>A TokenCredential that tries multiple authentication methods</returns>
    private TokenCredential GetChainedTokenCredential()
    {
        var managedIdentityClientId = _configuration["ManagedIdentityClientId"];
        return new ChainedTokenCredential(new DefaultAzureCredential(), new ManagedIdentityCredential(managedIdentityClientId));
    }

    /// <summary>
    /// Retrieves an Azure AD Application by its Application (Client) ID using Microsoft Graph API.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph API client</param>
    /// <param name="appId">The Application (Client) ID of the Azure AD App</param>
    /// <returns>The Application object if found, null otherwise</returns>
    private async Task<Application> GetAzureADApp(GraphServiceClient graphClient, string appId)
    {
        var applications = await graphClient.Applications
            .GetAsync(a => a.QueryParameters = new ApplicationsRequestBuilderGetQueryParameters { Filter = $"appId eq '{appId}'" });

        return applications?.Value?.FirstOrDefault();
    }

    /// <summary>
    /// Generates a new password credential for the Azure AD Application.
    /// Removes the old password credential if it exists and adds a new one.
    /// </summary>
    /// <param name="graphClient">Authenticated Graph API client</param>
    /// <param name="azureADApp">The Azure AD Application to update</param>
    /// <param name="secretDisplayName">Display name for the password credential (should match secret name)</param>
    /// <param name="secretEndDateTime">Expiration date for the new password</param>
    /// <returns>The newly generated password value</returns>
    private async Task<string> GenerateNewSecret(GraphServiceClient graphClient, Application azureADApp, string secretDisplayName, DateTimeOffset secretEndDateTime)
    {
        // Find existing password credential with the same display name
        var currentCredential = azureADApp.PasswordCredentials
            .FirstOrDefault(p => p.DisplayName.Equals(secretDisplayName, StringComparison.OrdinalIgnoreCase));

        // Create new password credential with specified expiration
        var passwordCredential = new PasswordCredential
        {
            EndDateTime = secretEndDateTime,
            StartDateTime = DateTimeOffset.UtcNow,
            DisplayName = secretDisplayName,
        };

        // Remove old password credential if it exists to avoid accumulation
        if (currentCredential != null)
        {
            await graphClient.Applications[azureADApp.Id]
                             .RemovePassword
                             .PostAsync(body: new RemovePasswordPostRequestBody { KeyId = currentCredential.KeyId });
        }

        // Add new password credential and get the generated secret value
        var result = await graphClient.Applications[azureADApp.Id]
                                      .AddPassword
                                      .PostAsync(body: new AddPasswordPostRequestBody { PasswordCredential = passwordCredential });

        return result.SecretText;
    }

    /// <summary>
    /// Updates the secret value in Azure Key Vault with the new password.
    /// Preserves all existing properties and tags while updating the value and expiration.
    /// </summary>
    /// <param name="secretClient">Authenticated Key Vault secret client</param>
    /// <param name="secret">The existing secret to update</param>
    /// <param name="newPassword">The new password value</param>
    /// <param name="secretEndDateTime">New expiration date</param>
    private async Task UpdateSecretInKeyVault(SecretClient secretClient, KeyVaultSecret secret, string newPassword, DateTimeOffset secretEndDateTime)
    {
        // Create updated secret preserving all properties
        var updatedSecret = new KeyVaultSecret(secret.Name, newPassword)
        {
            Properties =
            {
              ExpiresOn = secretEndDateTime,
              ContentType = secret.Properties.ContentType,
              NotBefore = secret.Properties.NotBefore,
              Enabled = secret.Properties.Enabled
            }
        };

        // Preserve all tags (contains metadata for rotation logic)
        updatedSecret.Properties.Tags.AddRange(secret.Properties.Tags);
        await secretClient.SetSecretAsync(updatedSecret);
    }

    /// <summary>
    /// Calculates the expiration date for the new secret based on the configured duration.
    /// </summary>
    /// <param name="tags">Secret tags containing SecretDurationInMonths</param>
    /// <returns>The calculated expiration date</returns>
    private DateTimeOffset GetSecretExpiryDate(IDictionary<string, string> tags)
    {
        var secretDurationInMonths = Convert.ToInt32(tags["SecretDurationInMonths"]);
        var secretEndDateTime = DateTimeOffset.UtcNow.AddMonths(secretDurationInMonths);
        return secretEndDateTime;
    }

    /// <summary>
    /// Updates the Azure DevOps service connection with the new password.
    /// Connects to Azure DevOps using managed identity authentication.
    /// </summary>
    /// <param name="credential">Token credential for authentication</param>
    /// <param name="tags">Secret tags containing Azure DevOps connection information</param>
    /// <param name="newPassword">The new password to set in the service connection</param>
    private async Task UpdateServiceConnectionInAzureDevOps(TokenCredential credential, IDictionary<string, string> tags, string newPassword)
    {
        // Create authenticated connection to Azure DevOps
        var vssConnection = await CreateVssConnectionAsync(credential, tags["azureDevOpsAccountUrl"]);

        // Get the service endpoint client and retrieve the specific connection
        var endpointService = vssConnection.GetClient<ServiceEndpointHttpClient2>();
        var connection = (await endpointService.GetServiceEndpointsByNamesAsync(tags["azureDevOpsProjectName"], new[] { tags["azureDevOpsConnectionName"] })).FirstOrDefault();
        
        // Update the service principal key (password) parameter
        connection.Authorization.Parameters["serviceprincipalkey"] = newPassword;

        // Commit the update to Azure DevOps
        await endpointService.UpdateServiceEndpointAsync(connection.Id, connection);
    }

    /// <summary>
    /// Creates an authenticated VSS connection to Azure DevOps using managed identity.
    /// </summary>
    /// <param name="credential">Token credential for obtaining access token</param>
    /// <param name="azDevOpsUrl">Azure DevOps organization URL</param>
    /// <returns>Authenticated VssConnection</returns>
    private async Task<VssConnection> CreateVssConnectionAsync(TokenCredential credential, string azDevOpsUrl)
    {
        // Get access token for Azure DevOps API
        var accessToken = await GetManagedIdentityAccessTokenAsync(credential);
        var token = new VssAadToken("Bearer", accessToken);
        var credentials = new VssAadCredential(token);
        var settings = VssClientHttpRequestSettings.Default.Clone();
        var organizationUrl = new Uri(azDevOpsUrl);

        return new VssConnection(organizationUrl, credentials, settings);
    }

    /// <summary>
    /// Obtains an access token for Azure DevOps using managed identity.
    /// </summary>
    /// <param name="credential">Token credential to use for authentication</param>
    /// <returns>Access token string</returns>
    private async Task<string> GetManagedIdentityAccessTokenAsync(TokenCredential credential)
    {
        // Request token with Azure DevOps default scopes
        var tokenRequestContext = new TokenRequestContext(VssAadSettings.DefaultScopes);
        var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);

        return token.Token;
    }
}
