namespace SecretsRotation.Functions;

public class KeyVaultSecretNearExpiry
{
    private readonly IConfiguration _configuration;

    public KeyVaultSecretNearExpiry(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [FunctionName("KeyVaultSecretNearExpiry")]
    public async Task Run([QueueTrigger("kv-secrets-near-expiry", Connection = "AzureWebJobsStorage")] string queueItem, ILogger log)
    {
        try
        {
            log.LogInformation($"Received queue message: {queueItem}");

            var eventData = JsonSerializer.Deserialize<EventGridData>(queueItem);

            var secretName = eventData.Data.ObjectName;
            var keyVaultName = eventData.Data.VaultName;
            var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net");

            log.LogInformation($"Secret value: {secretName}");
            log.LogInformation($"Keyvault name: {keyVaultName}");
            log.LogInformation($"ClientId: {_configuration["ManagedIdentityClientId"]}");

            var credential = GetChainedTokenCredential();
            var secretClient = new SecretClient(keyVaultUri, credential);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
            var tags = secret.Properties.Tags;

            foreach (var tag in tags)
            {
                log.LogInformation($"{tag}");
            }

            var graphClient = new GraphServiceClient(credential);

            Application azureADApp = await GetAzureADApp(graphClient, tags["azureADAppId"]);

            if (azureADApp != null)
            {
                var secretEndDateTime = GetSecretExpiryDate(tags);
                var newPassword = await GenerateNewSecret(graphClient, azureADApp, secret.Name, secretEndDateTime);
                log.LogInformation($"Secret of the AAD App '{azureADApp.DisplayName}' successfully updated with expiration date: {secretEndDateTime}.");

                await UpdateSecretInKeyVault(secretClient, secret, newPassword, secretEndDateTime);
                log.LogInformation($"Secret '{secret.Name}' successfully updated in the Keyvault.");

                await UpdateServiceConnectionInAzureDevOps(credential, tags, newPassword);
                log.LogInformation($"Secret of the SPN of the Service Connection '{tags["azureDevOpsConnectionName"]}' successfully updated.");
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, ex.Message);
            throw;
        }
    }

    private TokenCredential GetChainedTokenCredential()
    {
        var managedIdentityClientId = _configuration["ManagedIdentityClientId"];
        return new ChainedTokenCredential(new DefaultAzureCredential(), new ManagedIdentityCredential(managedIdentityClientId));
    }

    private async Task<Application> GetAzureADApp(GraphServiceClient graphClient, string appId)
    {
        var applications = await graphClient.Applications
            .GetAsync(a => a.QueryParameters = new ApplicationsRequestBuilderGetQueryParameters { Filter = $"appId eq '{appId}'" });

        return applications?.Value?.FirstOrDefault();
    }

    private async Task<string> GenerateNewSecret(GraphServiceClient graphClient, Application azureADApp, string secretDisplayName, DateTimeOffset secretEndDateTime)
    {
        var currentCredential = azureADApp.PasswordCredentials
            .FirstOrDefault(p => p.DisplayName.Equals(secretDisplayName, StringComparison.OrdinalIgnoreCase));


        var passwordCredential = new PasswordCredential
        {
            EndDateTime = secretEndDateTime,
            StartDateTime = DateTimeOffset.UtcNow,
            DisplayName = secretDisplayName,
        };

        if (currentCredential != null)
        {
            await graphClient.Applications[azureADApp.Id]
                             .RemovePassword
                             .PostAsync(body: new RemovePasswordPostRequestBody { KeyId = currentCredential.KeyId });
        }

        var result = await graphClient.Applications[azureADApp.Id]
                                      .AddPassword
                                      .PostAsync(body: new AddPasswordPostRequestBody { PasswordCredential = passwordCredential });

        return result.SecretText;
    }

    private async Task UpdateSecretInKeyVault(SecretClient secretClient, KeyVaultSecret secret, string newPassword, DateTimeOffset secretEndDateTime)
    {
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

        updatedSecret.Properties.Tags.AddRange(secret.Properties.Tags);
        await secretClient.SetSecretAsync(updatedSecret);
    }

    private DateTimeOffset GetSecretExpiryDate(IDictionary<string, string> tags)
    {
        var secretDurationInMonths = Convert.ToInt32(tags["SecretDurationInMonths"]);
        var secretEndDateTime = DateTimeOffset.UtcNow.AddMonths(secretDurationInMonths);
        return secretEndDateTime;
    }

    private async Task UpdateServiceConnectionInAzureDevOps(TokenCredential credential, IDictionary<string, string> tags, string newPassword)
    {

        var vssConnection = await CreateVssConnectionAsync(credential, tags["azureDevOpsAccountUrl"]);

        var endpointService = vssConnection.GetClient<ServiceEndpointHttpClient2>();
        var connection = (await endpointService.GetServiceEndpointsByNamesAsync(tags["azureDevOpsProjectName"], new[] { tags["azureDevOpsConnectionName"] })).FirstOrDefault();
        connection.Authorization.Parameters["serviceprincipalkey"] = newPassword;

        await endpointService.UpdateServiceEndpointAsync(connection.Id, connection);
    }

    private async Task<VssConnection> CreateVssConnectionAsync(TokenCredential credential, string azDevOpsUrl)
    {
        var accessToken = await GetManagedIdentityAccessTokenAsync(credential);
        var token = new VssAadToken("Bearer", accessToken);
        var credentials = new VssAadCredential(token);
        var settings = VssClientHttpRequestSettings.Default.Clone();
        var organizationUrl = new Uri(azDevOpsUrl);

        return new VssConnection(organizationUrl, credentials, settings);
    }

    private async Task<string> GetManagedIdentityAccessTokenAsync(TokenCredential credential)
    {
        var tokenRequestContext = new TokenRequestContext(VssAadSettings.DefaultScopes);
        var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);

        return token.Token;
    }
}
