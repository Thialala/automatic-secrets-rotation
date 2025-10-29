# Automating the rotation of Azure DevOps service connection passwords with Key Vault, Event Grid, Function App and Managed Identity

This solution provides automatic rotation of Azure DevOps service connection passwords using Azure Key Vault events, Event Grid, and Azure Functions, eliminating manual secret management and improving security posture.

## 📖 Documentation

- **[Implementation Overview](IMPLEMENTATION.md)** - Complete guide to understanding the implementation
- **[Architecture Documentation](ARCHITECTURE.md)** - Detailed architecture, data flows, and design decisions
- **[Deployment Guide](DEPLOYMENT.md)** - Step-by-step deployment instructions

## 🎯 Key Features

- **Automated Rotation**: Secrets are automatically rotated when nearing expiration
- **Event-Driven**: Uses Azure Event Grid for real-time event processing
- **Secure**: Leverages Managed Identity - no stored credentials
- **Complete**: Updates secrets in Azure AD, Key Vault, and Azure DevOps
- **Scalable**: Queue-based processing handles multiple secrets concurrently

## 🏗️ Architecture Overview

```
Key Vault (Secret Near Expiry)
    ↓
Event Grid System Topic
    ↓
Storage Queue
    ↓
Azure Function (Rotation Logic)
    ↓
Updates: Azure AD → Key Vault → Azure DevOps
```

## 🚀 Quick Start

1. **Clone the repository**
   ```bash
   git clone https://github.com/Thialala/automatic-secrets-rotation.git
   cd automatic-secrets-rotation
   ```

2. **Deploy infrastructure**
   ```bash
   cd deploy
   ./deploy.ps1
   ```

3. **Configure permissions**
   ```bash
   ./assignGraphPermission.ps1
   ```

4. **Deploy function code**
   ```bash
   cd ../src
   dotnet publish -c Release
   func azure functionapp publish <function-app-name>
   ```

5. **Configure secrets with tags**
   ```bash
   az keyvault secret set \
     --vault-name <vault-name> \
     --name <secret-name> \
     --value <secret-value> \
     --tags \
       azureADAppId=<app-id> \
       azureDevOpsAccountUrl=https://dev.azure.com/<org> \
       azureDevOpsProjectName=<project> \
       azureDevOpsConnectionName=<connection> \
       SecretDurationInMonths=6
   ```

For detailed instructions, see the [Deployment Guide](DEPLOYMENT.md).

## 📋 Prerequisites

- Azure subscription with appropriate permissions
- Azure DevOps organization
- .NET 6.0 SDK
- Azure CLI
- PowerShell 7.0+

## 🔧 How It Works

1. **Detection**: Key Vault emits events when secrets are near expiry (default: 30 days before)
2. **Routing**: Event Grid captures events and sends them to a Storage Queue
3. **Processing**: Azure Function is triggered by queue messages
4. **Rotation**: Function performs three updates:
   - Generates new password in Azure AD Application
   - Updates secret in Key Vault with new value
   - Updates Azure DevOps service connection

## 📦 Components

| Component | Purpose |
|-----------|---------|
| **Key Vault** | Stores secrets with expiration dates and metadata tags |
| **Event Grid** | Captures and routes Key Vault events |
| **Storage Queue** | Decouples event processing from handling |
| **Function App** | Executes rotation logic |
| **Managed Identity** | Provides secure authentication |

## 🏷️ Secret Tags

Secrets must include these tags for automatic rotation:

| Tag | Description | Example |
|-----|-------------|---------|
| `azureADAppId` | Azure AD Application ID | `12345678-...` |
| `azureDevOpsAccountUrl` | Azure DevOps org URL | `https://dev.azure.com/myorg` |
| `azureDevOpsProjectName` | Project name | `MyProject` |
| `azureDevOpsConnectionName` | Service connection name | `MyConnection` |
| `SecretDurationInMonths` | New secret validity | `6` |

## 🔒 Security

- **No Stored Credentials**: All authentication uses Managed Identity
- **Least Privilege**: Minimal RBAC permissions assigned
- **Audit Trail**: Complete logging to Application Insights
- **Encrypted**: All data encrypted in transit and at rest

## 📊 Monitoring

Monitor the solution using:
- **Application Insights**: Function execution, dependencies, and exceptions
- **Key Vault Logs**: Secret access and modification audit trail
- **Azure DevOps Audit**: Service connection updates

## 🐛 Troubleshooting

Common issues and resolutions are documented in the [Deployment Guide](DEPLOYMENT.md#troubleshooting).

## 📚 Blog Posts

Detailed explanations and walkthroughs:
- **English**: [Automating the rotation of Azure DevOps service connection passwords](https://enamsuobarry.medium.com/automating-the-rotation-of-azure-devops-service-connection-passwords-with-key-vault-event-grid-204afe6bd489)
- **French**: [Automatisation de la rotation des mots de passe des service connexions Azure DevOps](https://enamsuobarry.medium.com/automatisation-de-la-rotation-des-mots-de-passe-des-service-connexions-azure-devops-avec-azure-9e290f724465)

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## 📄 License

This project is provided as-is for educational and reference purposes.

## 🙏 Acknowledgments

This solution implements best practices for Azure secret management and demonstrates event-driven architecture patterns in Azure.
