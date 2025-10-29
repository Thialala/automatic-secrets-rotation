# Secrets Rotation Implementation Overview

## Table of Contents
- [Architecture Overview](#architecture-overview)
- [Components](#components)
- [Data Flow](#data-flow)
- [Implementation Details](#implementation-details)
- [Deployment](#deployment)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)

## Architecture Overview

This solution automatically rotates Azure DevOps service connection passwords by leveraging Azure Key Vault, Event Grid, Azure Functions, and Managed Identity. The system is designed to be fully automated and secure, eliminating the need for manual secret rotation.

### High-Level Flow
1. **Event Detection**: Key Vault emits events when secrets are near expiry or expired
2. **Event Routing**: Event Grid captures these events and routes them to a Storage Queue
3. **Processing**: Azure Function is triggered by queue messages and performs the rotation
4. **Update Cycle**: The function rotates secrets in Azure AD, Key Vault, and Azure DevOps

## Components

### 1. Azure Key Vault
- **Purpose**: Stores service connection secrets with expiration dates
- **Configuration**: Secrets must be tagged with specific metadata
- **Events**: Emits `SecretNearExpiry` and `SecretExpired` events

Required secret tags:
- `azureADAppId`: The Azure AD Application (App Registration) ID
- `azureDevOpsAccountUrl`: Azure DevOps organization URL (e.g., `https://dev.azure.com/myorg`)
- `azureDevOpsProjectName`: Project name in Azure DevOps
- `azureDevOpsConnectionName`: Service connection name
- `SecretDurationInMonths`: Duration for the new secret (e.g., `6` for 6 months)

### 2. Event Grid System Topic
- **Purpose**: Captures Key Vault events
- **Type**: System topic for `Microsoft.KeyVault.vaults`
- **Managed Identity**: Uses system-assigned managed identity to send messages to queue

### 3. Storage Queue
- **Name**: `kv-secrets-near-expiry`
- **Purpose**: Decouples event handling from processing
- **Permissions**: Event Grid topic has `Storage Queue Data Message Sender` role

### 4. Azure Function App
- **Runtime**: .NET 6.0
- **Trigger**: Queue trigger on `kv-secrets-near-expiry`
- **Identity**: User-assigned managed identity
- **Permissions Required**:
  - Key Vault: `Key Vault Secrets Officer` role
  - Storage Account: `Storage Account Contributor`, `Storage Blob Data Owner`, `Storage Queue Data Contributor`
  - Azure AD: `Application.ReadWrite.All` Graph API permission
  - Azure DevOps: Must be added as a user with appropriate permissions

### 5. User-Assigned Managed Identity
- **Purpose**: Provides secure authentication for the Function App
- **Assigned To**: Function App
- **Permissions**: See Azure Function App section above

## Data Flow

```
┌─────────────────┐
│   Key Vault     │
│  (Secret Near   │
│    Expiry)      │
└────────┬────────┘
         │ Event
         ▼
┌─────────────────┐
│  Event Grid     │
│  System Topic   │
└────────┬────────┘
         │ Message
         ▼
┌─────────────────┐
│ Storage Queue   │
│ kv-secrets-     │
│ near-expiry     │
└────────┬────────┘
         │ Trigger
         ▼
┌─────────────────────────────────────────────┐
│         Azure Function                      │
│  (KeyVaultSecretNearExpiry)                 │
│                                             │
│  1. Retrieve secret from Key Vault         │
│  2. Get Azure AD App from tags             │
│  3. Generate new password in Azure AD      │
│  4. Update secret in Key Vault             │
│  5. Update service connection in DevOps    │
└─────────────────────────────────────────────┘
```

## Implementation Details

### KeyVaultSecretNearExpiry Function

The main Azure Function that orchestrates the secret rotation process.

#### Process Steps

1. **Deserialize Event Data**
   - Parses the queue message containing Event Grid event
   - Extracts secret name and Key Vault name

2. **Retrieve Secret and Metadata**
   - Connects to Key Vault using managed identity
   - Retrieves the secret and its tags
   - Tags contain all necessary configuration

3. **Get Azure AD Application**
   - Uses Microsoft Graph API to find the application by App ID
   - Retrieves current password credentials

4. **Generate New Secret**
   - Calculates expiration date based on `SecretDurationInMonths` tag
   - Removes old password credential (if exists)
   - Adds new password credential with new expiration date
   - Returns the new secret value

5. **Update Key Vault**
   - Updates the secret value in Key Vault
   - Preserves all properties and tags
   - Sets new expiration date

6. **Update Azure DevOps Service Connection**
   - Creates authenticated connection to Azure DevOps using managed identity
   - Retrieves the service connection by name
   - Updates the `serviceprincipalkey` parameter
   - Commits the update

### Authentication Flow

#### For Azure Resources (Key Vault, Storage)
```csharp
var credential = new ChainedTokenCredential(
    new DefaultAzureCredential(), 
    new ManagedIdentityCredential(managedIdentityClientId)
);
```

#### For Microsoft Graph API
```csharp
var graphClient = new GraphServiceClient(credential);
```

#### For Azure DevOps
```csharp
// Obtain access token for Azure DevOps
var tokenRequestContext = new TokenRequestContext(VssAadSettings.DefaultScopes);
var token = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);

// Create credentials
var vssToken = new VssAadToken("Bearer", token.Token);
var credentials = new VssAadCredential(vssToken);
var vssConnection = new VssConnection(organizationUrl, credentials, settings);
```

### Error Handling

The function implements comprehensive error handling:
- All exceptions are logged with full stack traces
- Failed operations are re-thrown to trigger retry logic
- Queue message visibility timeout allows for automatic retries

## Deployment

### Prerequisites
- Azure subscription with appropriate permissions
- Azure DevOps organization
- PowerShell (for deployment scripts)

### Deployment Steps

1. **Clone the Repository**
   ```bash
   git clone https://github.com/Thialala/automatic-secrets-rotation.git
   cd automatic-secrets-rotation
   ```

2. **Review and Update Parameters**
   Edit `deploy/main.bicep` to customize resource names and settings.

3. **Deploy Infrastructure**
   ```powershell
   cd deploy
   ./deploy.ps1
   ```

4. **Configure Azure AD Permissions**
   ```powershell
   ./assignGraphPermission.ps1
   ```
   
   This script assigns the necessary Microsoft Graph API permissions to the managed identity.

5. **Configure Azure DevOps Permissions**
   - Add the managed identity as a user in Azure DevOps
   - Assign appropriate permissions (typically Endpoint Administrator or Stakeholder)

6. **Deploy Function Code**
   ```bash
   cd ../src
   dotnet publish -c Release
   # Deploy to Azure Function App using your preferred method
   ```

7. **Configure Secrets in Key Vault**
   Create secrets with the required tags:
   ```bash
   az keyvault secret set \
     --vault-name <vault-name> \
     --name <secret-name> \
     --value <initial-secret-value> \
     --tags \
       azureADAppId=<app-id> \
       azureDevOpsAccountUrl=https://dev.azure.com/<org> \
       azureDevOpsProjectName=<project-name> \
       azureDevOpsConnectionName=<connection-name> \
       SecretDurationInMonths=6
   ```

## Configuration

### Application Settings

The Function App requires the following configuration:

```json
{
  "AzureWebJobsStorage__accountName": "<storage-account-name>",
  "AZURE_CLIENT_ID": "<managed-identity-client-id>",
  "ManagedIdentityClientId": "<managed-identity-client-id>",
  "FUNCTIONS_WORKER_RUNTIME": "dotnet",
  "FUNCTIONS_EXTENSION_VERSION": "~4"
}
```

### Secret Tags Schema

| Tag Name | Description | Example |
|----------|-------------|---------|
| `azureADAppId` | Azure AD Application ID | `12345678-1234-1234-1234-123456789abc` |
| `azureDevOpsAccountUrl` | Azure DevOps organization URL | `https://dev.azure.com/myorg` |
| `azureDevOpsProjectName` | Project name | `MyProject` |
| `azureDevOpsConnectionName` | Service connection name | `MyServiceConnection` |
| `SecretDurationInMonths` | Secret validity duration | `6` |

## Troubleshooting

### Common Issues

#### 1. Function Not Triggering
**Symptom**: Secrets near expiry but function doesn't execute

**Checks**:
- Verify Event Grid subscription is active
- Check Storage Queue for messages
- Verify Function App is running
- Review Application Insights logs

#### 2. Authentication Failures
**Symptom**: Function fails with authentication errors

**Checks**:
- Verify managed identity has required RBAC roles
- Check Graph API permissions are granted and admin consented
- Verify managed identity is added to Azure DevOps

#### 3. Secret Not Found in Key Vault
**Symptom**: Function fails to retrieve secret

**Checks**:
- Verify secret exists with exact name from event
- Check managed identity has `Key Vault Secrets Officer` role
- Verify Key Vault RBAC is enabled

#### 4. Azure DevOps Update Fails
**Symptom**: Secret rotated in Azure AD and Key Vault but not in DevOps

**Checks**:
- Verify managed identity has permissions in Azure DevOps
- Check service connection name matches tag exactly
- Verify project name is correct

### Monitoring

Use Application Insights to monitor:
- Function execution success/failure rates
- Execution duration
- Exception details
- Custom traces with secret rotation details

### Testing

To test the rotation without waiting for expiry:
1. Manually send a test message to the queue with proper format
2. Or temporarily modify secret expiry date to trigger event

## Security Considerations

1. **Managed Identity**: All authentication uses managed identity - no stored credentials
2. **RBAC**: Principle of least privilege applied to all role assignments
3. **Secret Access**: Function only accesses secrets it needs to rotate (via tags)
4. **Audit Logs**: All operations logged to Application Insights
5. **Network Security**: Consider using Private Endpoints for production

## Maintenance

### Regular Tasks
- Monitor Application Insights for failures
- Review rotated secrets periodically
- Update secret tags when service connections change
- Keep Function App runtime and packages updated

### Updating the Solution
1. Update code in repository
2. Build and test locally
3. Deploy to staging environment (if available)
4. Deploy to production

## References

- [Blog Post (English)](https://enamsuobarry.medium.com/automating-the-rotation-of-azure-devops-service-connection-passwords-with-key-vault-event-grid-204afe6bd489)
- [Blog Post (French)](https://enamsuobarry.medium.com/automatisation-de-la-rotation-des-mots-de-passe-des-service-connexions-azure-devops-avec-azure-9e290f724465)
- [Azure Key Vault Events](https://docs.microsoft.com/azure/key-vault/general/event-grid-overview)
- [Azure Functions Queue Trigger](https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-queue-trigger)
- [Microsoft Graph API](https://docs.microsoft.com/graph/overview)
