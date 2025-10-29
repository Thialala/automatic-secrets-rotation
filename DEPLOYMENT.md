# Deployment Guide

This guide provides step-by-step instructions for deploying the Automatic Secrets Rotation solution.

## Table of Contents
- [Prerequisites](#prerequisites)
- [Pre-Deployment Steps](#pre-deployment-steps)
- [Infrastructure Deployment](#infrastructure-deployment)
- [Permissions Configuration](#permissions-configuration)
- [Function App Deployment](#function-app-deployment)
- [Secret Configuration](#secret-configuration)
- [Verification](#verification)
- [Post-Deployment Testing](#post-deployment-testing)

## Prerequisites

### Required Tools
- **Azure CLI**: Version 2.40.0 or higher
  ```bash
  az --version
  az upgrade
  ```
- **PowerShell**: Version 7.0 or higher (for deployment scripts)
  ```bash
  pwsh --version
  ```
- **.NET SDK**: Version 6.0 or higher
  ```bash
  dotnet --version
  ```
- **Git**: For cloning the repository
  ```bash
  git --version
  ```

### Required Permissions
- **Azure Subscription**: Owner or Contributor + User Access Administrator roles
- **Azure AD**: Ability to grant admin consent for API permissions
- **Azure DevOps**: Organization Administrator or Project Collection Administrator

### Azure Resources Required
- Active Azure subscription
- Azure DevOps organization
- Existing Azure AD Application (Service Principal) for the service connection

## Pre-Deployment Steps

### 1. Clone the Repository

```bash
git clone https://github.com/Thialala/automatic-secrets-rotation.git
cd automatic-secrets-rotation
```

### 2. Login to Azure

```bash
az login
az account set --subscription "<your-subscription-id>"
```

### 3. Set Variables

Create a PowerShell script or set environment variables:

```powershell
# Azure subscription details
$subscriptionId = "<your-subscription-id>"
$resourceGroupName = "rg-secrets-rotation"
$location = "eastus"

# Resource names (customize as needed)
$managedIdentityName = "id-secrets-rotation-001"
$keyVaultName = "kv-secrets-rot-$(Get-Random -Maximum 9999)"  # Must be globally unique
$functionAppName = "func-secrets-rot-$(Get-Random -Maximum 9999)"  # Must be globally unique
$storageAccountName = "stgsecretsrot$(Get-Random -Maximum 9999)"  # Must be globally unique and lowercase

# Azure DevOps details
$azureDevOpsOrgUrl = "https://dev.azure.com/<your-org>"
$azureDevOpsProjectName = "<your-project>"
$serviceConnectionName = "<your-service-connection>"
$azureADAppId = "<app-registration-client-id>"
```

### 4. Create Resource Group

```bash
az group create --name $resourceGroupName --location $location
```

## Infrastructure Deployment

### Option 1: Using the Deployment Script (Recommended)

1. Navigate to the deploy folder:
   ```powershell
   cd deploy
   ```

2. Review and customize `main.bicep` parameters:
   ```powershell
   # Edit main.bicep to set your resource names
   code main.bicep
   ```

3. Run the deployment script:
   ```powershell
   ./deploy.ps1
   ```

   If `deploy.ps1` doesn't exist, create it with:
   ```powershell
   # deploy.ps1
   param(
       [string]$ResourceGroupName = "rg-secrets-rotation",
       [string]$Location = "eastus"
   )

   az deployment group create `
       --resource-group $ResourceGroupName `
       --template-file main.bicep `
       --parameters location=$Location
   ```

### Option 2: Manual Deployment Using Azure CLI

```bash
az deployment group create \
    --resource-group $resourceGroupName \
    --template-file deploy/main.bicep \
    --parameters \
        location=$location \
        managedIdentityName=$managedIdentityName \
        keyVaultName=$keyVaultName \
        functionAppName=$functionAppName \
        storageAccountName=$storageAccountName
```

### 3. Capture Deployment Outputs

```bash
# Get the managed identity details
$managedIdentityClientId = az deployment group show \
    --resource-group $resourceGroupName \
    --name main \
    --query properties.outputs.managedIdentityClientId.value \
    --output tsv

$managedIdentityPrincipalId = az deployment group show \
    --resource-group $resourceGroupName \
    --name main \
    --query properties.outputs.managedIdentityPrincipalId.value \
    --output tsv

echo "Managed Identity Client ID: $managedIdentityClientId"
echo "Managed Identity Principal ID: $managedIdentityPrincipalId"
```

## Permissions Configuration

### 1. Grant Microsoft Graph API Permissions

The managed identity needs permissions to manage Azure AD Application passwords.

#### Using PowerShell Script

```powershell
# Navigate to deploy folder
cd deploy

# Run the Graph permission script
./assignGraphPermission.ps1 -ManagedIdentityPrincipalId $managedIdentityPrincipalId
```

#### Manual Configuration

If the script doesn't exist, use these commands:

```powershell
# Connect to Azure AD
Connect-AzureAD

# Get Microsoft Graph Service Principal
$graphSP = Get-AzureADServicePrincipal -Filter "AppId eq '00000003-0000-0000-c000-000000000000'"

# Get the Application.ReadWrite.All permission
$appRoleId = $graphSP.AppRoles | Where-Object {$_.Value -eq "Application.ReadWrite.All"} | Select-Object -ExpandProperty Id

# Get the managed identity service principal
$managedIdentitySP = Get-AzureADServicePrincipal -ObjectId $managedIdentityPrincipalId

# Assign the permission
New-AzureADServiceAppRoleAssignment `
    -ObjectId $managedIdentitySP.ObjectId `
    -PrincipalId $managedIdentitySP.ObjectId `
    -ResourceId $graphSP.ObjectId `
    -Id $appRoleId
```

### 2. Configure Azure DevOps Permissions

The managed identity must be added as a user in Azure DevOps.

#### Add Managed Identity to Azure DevOps

1. Navigate to Azure DevOps: `https://dev.azure.com/<your-org>`

2. Go to **Organization Settings** → **Users**

3. Click **Add users**

4. Enter the managed identity's **Application (Client) ID**: `$managedIdentityClientId`

5. Set access level to **Stakeholder** (minimum) or **Basic**

6. Click **Add**

#### Grant Service Connection Permissions

1. Navigate to **Project Settings** → **Service connections**

2. Select your service connection

3. Click **...** → **Security**

4. Click **Add** and search for the managed identity by its Client ID

5. Assign the **Endpoint Administrator** role

## Function App Deployment

### 1. Build the Function App

```bash
cd src/SecretsRotation
dotnet restore
dotnet build -c Release
```

### 2. Publish the Function App

```bash
dotnet publish -c Release -o ./publish
```

### 3. Deploy to Azure

#### Using Azure Functions Core Tools

```bash
# Install Azure Functions Core Tools if not already installed
npm install -g azure-functions-core-tools@4

# Deploy
cd publish
func azure functionapp publish $functionAppName
```

#### Using Azure CLI

```bash
cd publish
zip -r ../function.zip .
cd ..

az functionapp deployment source config-zip \
    --resource-group $resourceGroupName \
    --name $functionAppName \
    --src function.zip
```

#### Using Visual Studio

1. Right-click the `SecretsRotation` project
2. Select **Publish**
3. Choose **Azure**
4. Select **Azure Function App (Windows)**
5. Select your function app
6. Click **Publish**

## Secret Configuration

### 1. Prepare Secret Tags

Each secret in Key Vault must have specific tags for the rotation to work.

### 2. Create or Update Secrets

```bash
# Set the secret value and tags
az keyvault secret set \
    --vault-name $keyVaultName \
    --name "<secret-name>" \
    --value "<initial-secret-value>" \
    --tags \
        azureADAppId="$azureADAppId" \
        azureDevOpsAccountUrl="$azureDevOpsOrgUrl" \
        azureDevOpsProjectName="$azureDevOpsProjectName" \
        azureDevOpsConnectionName="$serviceConnectionName" \
        SecretDurationInMonths="6"
```

### 3. Set Expiration Date

```bash
# Set expiration to 30 days from now (for testing)
$expiryDate = (Get-Date).AddDays(30).ToString("yyyy-MM-ddTHH:mm:ssZ")

az keyvault secret set-attributes \
    --vault-name $keyVaultName \
    --name "<secret-name>" \
    --expires $expiryDate
```

## Verification

### 1. Verify Infrastructure Deployment

```bash
# Check all resources are created
az resource list --resource-group $resourceGroupName --output table
```

Expected resources:
- User Assigned Managed Identity
- Key Vault
- Storage Account
- Function App
- App Service Plan
- Application Insights
- Event Grid System Topic

### 2. Verify Function App Configuration

```bash
# Check function app settings
az functionapp config appsettings list \
    --resource-group $resourceGroupName \
    --name $functionAppName \
    --output table
```

Verify these settings exist:
- `ManagedIdentityClientId`
- `AZURE_CLIENT_ID`
- `AzureWebJobsStorage__accountName`
- `FUNCTIONS_WORKER_RUNTIME=dotnet`
- `FUNCTIONS_EXTENSION_VERSION=~4`

### 3. Verify RBAC Assignments

```bash
# Check Key Vault role assignments
az role assignment list \
    --scope $(az keyvault show --name $keyVaultName --query id -o tsv) \
    --assignee $managedIdentityPrincipalId \
    --output table

# Check Storage role assignments
az role assignment list \
    --scope $(az storage account show --name $storageAccountName --query id -o tsv) \
    --assignee $managedIdentityPrincipalId \
    --output table
```

### 4. Verify Event Grid Subscription

```bash
# List event subscriptions
az eventgrid system-topic event-subscription list \
    --resource-group $resourceGroupName \
    --system-topic-name "<topic-name>" \
    --output table
```

### 5. Verify Function Deployment

```bash
# List functions
az functionapp function list \
    --resource-group $resourceGroupName \
    --name $functionAppName \
    --output table
```

Should show `KeyVaultSecretNearExpiry` function.

## Post-Deployment Testing

### 1. Monitor Function Logs

```bash
# Stream logs
az webapp log tail \
    --resource-group $resourceGroupName \
    --name $functionAppName
```

Or use Application Insights in the Azure Portal.

### 2. Trigger Test Event

#### Option A: Update Secret Expiry

Set a secret to expire soon:

```bash
# Set expiry to 29 days from now (triggers SecretNearExpiry)
$expiryDate = (Get-Date).AddDays(29).ToString("yyyy-MM-ddTHH:mm:ssZ")

az keyvault secret set-attributes \
    --vault-name $keyVaultName \
    --name "<secret-name>" \
    --expires $expiryDate
```

#### Option B: Manual Queue Message

Send a test message to the queue:

```powershell
$queueMessage = @{
    id = "test-event"
    topic = "/subscriptions/<sub>/resourceGroups/$resourceGroupName/providers/Microsoft.KeyVault/vaults/$keyVaultName"
    subject = "secrets/<secret-name>"
    eventType = "Microsoft.KeyVault.SecretNearExpiry"
    data = @{
        Id = "https://$keyVaultName.vault.azure.net/secrets/<secret-name>/version"
        VaultName = $keyVaultName
        ObjectType = "Secret"
        ObjectName = "<secret-name>"
        Version = "version"
        NBF = $null
        EXP = [DateTimeOffset]::UtcNow.AddDays(29).ToUnixTimeSeconds()
    }
    dataVersion = "1"
    metadataVersion = "1"
    eventTime = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
} | ConvertTo-Json -Depth 10

# Send to queue (requires Azure PowerShell module)
# Or use Azure Portal Storage Explorer
```

### 3. Verify Rotation Success

After triggering the function:

1. **Check Function Logs**: Verify successful execution
2. **Check Key Vault**: Verify secret was updated with new value
3. **Check Azure AD**: Verify new password credential was added
4. **Check Azure DevOps**: Verify service connection works with new password

### 4. Test Service Connection

In Azure DevOps:

1. Navigate to **Project Settings** → **Service connections**
2. Select your service connection
3. Click **Verify** or **Validate**
4. Should succeed with the rotated password

## Troubleshooting

### Function Not Triggering

**Issue**: Function doesn't execute when secret is near expiry.

**Resolution**:
```bash
# Check Event Grid subscription
az eventgrid system-topic event-subscription show \
    --resource-group $resourceGroupName \
    --system-topic-name "<topic-name>" \
    --name "<subscription-name>"

# Check queue for messages
az storage message peek \
    --queue-name "kv-secrets-near-expiry" \
    --account-name $storageAccountName
```

### Authentication Errors

**Issue**: Function fails with "Unauthorized" errors.

**Resolution**:
```bash
# Verify managed identity has correct permissions
az role assignment list --assignee $managedIdentityPrincipalId --all

# Re-run Graph permission script
./assignGraphPermission.ps1 -ManagedIdentityPrincipalId $managedIdentityPrincipalId
```

### Secret Not Found

**Issue**: Function cannot find secret in Key Vault.

**Resolution**:
```bash
# Verify secret exists
az keyvault secret list --vault-name $keyVaultName --output table

# Verify secret has correct tags
az keyvault secret show --vault-name $keyVaultName --name "<secret-name>" --query "properties.tags"
```

## Cleanup

To remove all deployed resources:

```bash
# Delete the entire resource group
az group delete --name $resourceGroupName --yes --no-wait
```

## Next Steps

1. **Set up monitoring alerts** in Application Insights
2. **Configure backup** for Key Vault secrets
3. **Document** your specific secret rotation schedule
4. **Test** rotation process in a staging environment
5. **Create runbook** for common operational tasks

## Support

For issues or questions:
- Check the [Implementation Guide](IMPLEMENTATION.md)
- Review the [Architecture Documentation](ARCHITECTURE.md)
- Consult the blog posts:
  - [English](https://enamsuobarry.medium.com/automating-the-rotation-of-azure-devops-service-connection-passwords-with-key-vault-event-grid-204afe6bd489)
  - [French](https://enamsuobarry.medium.com/automatisation-de-la-rotation-des-mots-de-passe-des-service-connexions-azure-devops-avec-azure-9e290f724465)
