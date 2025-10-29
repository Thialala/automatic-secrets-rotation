# Architecture Documentation

## System Architecture

### Overview

The Automatic Secrets Rotation system is built on Azure's event-driven architecture, ensuring secrets are rotated automatically before expiration.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          Azure Subscription                             │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    Resource Group                                │  │
│  │                                                                  │  │
│  │  ┌────────────────┐           ┌──────────────────┐             │  │
│  │  │  Key Vault     │           │  Event Grid      │             │  │
│  │  │                │           │  System Topic    │             │  │
│  │  │ - Secrets with │──Events──▶│                  │             │  │
│  │  │   expiry dates │           │ - SecretNear     │             │  │
│  │  │ - Tags with    │           │   Expiry         │             │  │
│  │  │   metadata     │           │ - SecretExpired  │             │  │
│  │  └───────▲────────┘           └────────┬─────────┘             │  │
│  │          │                              │                       │  │
│  │          │                              │ Sends                 │  │
│  │          │                              │ Message               │  │
│  │          │                              ▼                       │  │
│  │          │                     ┌──────────────────┐             │  │
│  │          │                     │ Storage Account  │             │  │
│  │          │                     │                  │             │  │
│  │          │                     │ ┌──────────────┐ │             │  │
│  │          │                     │ │ Queue:       │ │             │  │
│  │          │                     │ │ kv-secrets-  │ │             │  │
│  │          │                     │ │ near-expiry  │ │             │  │
│  │          │                     │ └──────┬───────┘ │             │  │
│  │          │                     └────────┼─────────┘             │  │
│  │          │                              │                       │  │
│  │          │                              │ Triggers              │  │
│  │          │                              ▼                       │  │
│  │          │                     ┌──────────────────┐             │  │
│  │          │                     │ Function App     │             │  │
│  │          │                     │                  │             │  │
│  │          │                     │ ┌──────────────┐ │             │  │
│  │          │                     │ │  Function:   │ │             │  │
│  │          │                     │ │  KeyVault    │ │             │  │
│  │          │                     │ │  SecretNear  │ │             │  │
│  │          │                     │ │  Expiry      │ │             │  │
│  │          │                     │ └──────┬───────┘ │             │  │
│  │          │                     │        │         │             │  │
│  │          │                     │        │ Uses    │             │  │
│  │          │                     │        ▼         │             │  │
│  │          │                     │ ┌──────────────┐ │             │  │
│  │          │                     │ │  User-       │ │             │  │
│  │          │                     │ │  Assigned    │ │             │  │
│  │          │                     │ │  Managed     │ │             │  │
│  │          │                     │ │  Identity    │ │             │  │
│  │          │                     │ └──────┬───────┘ │             │  │
│  │          │                     └────────┼─────────┘             │  │
│  │          │                              │                       │  │
│  │          └──────────────────────────────┘                       │  │
│  │              Updates secret                                     │  │
│  │                                                                  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
         │                                        │
         │ Authenticates                          │ Authenticates
         │ via Managed Identity                   │ via Managed Identity
         ▼                                        ▼
┌─────────────────────┐              ┌──────────────────────┐
│   Microsoft Entra   │              │   Azure DevOps       │
│   (Azure AD)        │              │                      │
│                     │              │  ┌────────────────┐  │
│  ┌───────────────┐  │              │  │ Service        │  │
│  │ App           │  │              │  │ Connection     │  │
│  │ Registration  │  │              │  │                │  │
│  │               │  │              │  │ Updates SPN    │  │
│  │ - Rotate      │  │              │  │ password       │  │
│  │   password    │  │              │  └────────────────┘  │
│  └───────────────┘  │              │                      │
└─────────────────────┘              └──────────────────────┘
```

## Component Interaction Flow

### 1. Event Generation Phase

```
Key Vault Secret Approaching Expiry
    │
    │ (Automatic event emission)
    ▼
Event Grid System Topic
    │
    │ (Filter: SecretNearExpiry, SecretExpired)
    ▼
Event Subscription
    │
    │ (Delivery to queue)
    ▼
Storage Queue
```

### 2. Processing Phase

```
Storage Queue Message
    │
    │ (Queue Trigger)
    ▼
Azure Function (KeyVaultSecretNearExpiry)
    │
    ├──▶ Read Secret from Key Vault
    │        │
    │        └──▶ Parse tags for metadata
    │
    ├──▶ Query Microsoft Graph API
    │        │
    │        └──▶ Find Azure AD Application
    │
    ├──▶ Rotate Password in Azure AD
    │        │
    │        ├──▶ Remove old password credential
    │        └──▶ Add new password credential
    │
    ├──▶ Update Secret in Key Vault
    │        │
    │        └──▶ Store new password with tags
    │
    └──▶ Update Azure DevOps Service Connection
         │
         └──▶ Update serviceprincipalkey parameter
```

## Data Models

### Event Grid Event Schema

```json
{
  "id": "unique-event-id",
  "topic": "/subscriptions/{subscription}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{vault}",
  "subject": "secrets/{secretName}",
  "eventType": "Microsoft.KeyVault.SecretNearExpiry",
  "data": {
    "Id": "https://{vault}.vault.azure.net/secrets/{secretName}/{version}",
    "VaultName": "{vault-name}",
    "ObjectType": "Secret",
    "ObjectName": "{secret-name}",
    "Version": "{version}",
    "NBF": null,
    "EXP": 1234567890
  },
  "dataVersion": "1",
  "metadataVersion": "1",
  "eventTime": "2025-10-29T18:00:00Z"
}
```

### Secret Tags Schema

```json
{
  "azureADAppId": "12345678-1234-1234-1234-123456789abc",
  "azureDevOpsAccountUrl": "https://dev.azure.com/myorg",
  "azureDevOpsProjectName": "MyProject",
  "azureDevOpsConnectionName": "MyServiceConnection",
  "SecretDurationInMonths": "6"
}
```

## Security Architecture

### Identity and Access Management

```
┌─────────────────────────────────────┐
│  User-Assigned Managed Identity    │
│                                     │
│  Used by Function App for:         │
│  - Storage Account access          │
│  - Key Vault access                │
│  - Microsoft Graph API calls       │
│  - Azure DevOps API calls          │
└─────────────────────────────────────┘
         │
         ├──▶ Storage Account
         │    └─ Roles:
         │       - Storage Account Contributor
         │       - Storage Blob Data Owner
         │       - Storage Queue Data Contributor
         │
         ├──▶ Key Vault
         │    └─ Role:
         │       - Key Vault Secrets Officer
         │
         ├──▶ Microsoft Graph API
         │    └─ Permission:
         │       - Application.ReadWrite.All (Application)
         │
         └──▶ Azure DevOps
              └─ Access Level:
                 - Stakeholder or higher
                 - Endpoint Administrator (for project)
```

### Authentication Flows

#### 1. Storage Account Authentication
```
Function App
    │ (Using Managed Identity)
    ▼
Azure Storage
    │ (RBAC Check)
    ├─ Storage Account Contributor
    ├─ Storage Blob Data Owner
    └─ Storage Queue Data Contributor
    │
    ▼
Access Granted
```

#### 2. Key Vault Authentication
```
Function App
    │ (Using Managed Identity)
    ▼
Key Vault
    │ (RBAC Check)
    └─ Key Vault Secrets Officer
    │
    ▼
Access Granted
```

#### 3. Microsoft Graph Authentication
```
Function App
    │ (Using Managed Identity)
    ▼
Microsoft Entra ID
    │ (Get Access Token)
    ▼
Microsoft Graph API
    │ (Permission Check)
    └─ Application.ReadWrite.All
    │
    ▼
Access Granted
```

#### 4. Azure DevOps Authentication
```
Function App
    │ (Using Managed Identity)
    ▼
Microsoft Entra ID
    │ (Get Access Token with VssAadSettings.DefaultScopes)
    ▼
Azure DevOps
    │ (User/Permission Check)
    ├─ User exists in organization
    └─ Has Endpoint Administrator role
    │
    ▼
Access Granted
```

## Scalability Considerations

### Queue-Based Processing
- **Decoupling**: Event Grid and Function processing are decoupled via Storage Queue
- **Retry Logic**: Failed messages automatically retry based on queue configuration
- **Poison Messages**: Messages that fail repeatedly move to poison queue

### Function Scaling
- **Consumption Plan**: Automatically scales based on queue length
- **Concurrent Executions**: Multiple instances can process different secrets simultaneously
- **Throttling**: Built-in Azure Function throttling prevents overwhelming downstream services

### Rate Limiting
- **Microsoft Graph API**: Respects API rate limits
- **Azure DevOps API**: Handles rate limiting gracefully
- **Key Vault**: Optimized to minimize API calls

## High Availability

### Component Availability

| Component | Availability | Redundancy |
|-----------|--------------|------------|
| Key Vault | 99.9% SLA | Multi-region replication available |
| Event Grid | 99.99% SLA | Built-in redundancy |
| Storage Queue | 99.9% SLA | LRS/GRS/ZRS options |
| Function App | 99.95% SLA | Multiple instances |

### Failure Scenarios

#### Event Grid Unavailable
- **Impact**: Events not delivered to queue
- **Recovery**: Event Grid retries delivery
- **Mitigation**: Dead-letter queue configured

#### Storage Queue Unavailable
- **Impact**: Function cannot be triggered
- **Recovery**: Automatic retry when service recovers
- **Mitigation**: Use geo-redundant storage

#### Function Execution Failure
- **Impact**: Secret not rotated in that attempt
- **Recovery**: Queue message visibility timeout triggers retry
- **Mitigation**: Comprehensive error handling and logging

#### Key Vault Unavailable
- **Impact**: Cannot read or update secrets
- **Recovery**: Function retries automatically
- **Mitigation**: Consider Key Vault soft-delete and purge protection

## Monitoring and Observability

### Application Insights Integration

```
Function Execution
    │
    ├──▶ Traces
    │    └─ Custom log messages
    │
    ├──▶ Dependencies
    │    ├─ Key Vault calls
    │    ├─ Microsoft Graph calls
    │    └─ Azure DevOps calls
    │
    ├──▶ Exceptions
    │    └─ Full stack traces
    │
    └──▶ Metrics
         ├─ Execution count
         ├─ Duration
         └─ Success/Failure rate
```

### Key Metrics to Monitor

1. **Function Execution Metrics**
   - Execution count per day
   - Average execution duration
   - Success rate percentage

2. **Queue Metrics**
   - Queue length
   - Messages processed per hour
   - Poison message count

3. **API Call Metrics**
   - Graph API call duration
   - Azure DevOps API call duration
   - Error rates by API

## Performance Characteristics

### Expected Execution Time
- **Total Duration**: 5-15 seconds per secret rotation
  - Key Vault operations: 1-2 seconds
  - Microsoft Graph operations: 2-5 seconds
  - Azure DevOps operations: 2-5 seconds
  - Processing overhead: 1-3 seconds

### Throughput
- **Sequential Processing**: ~4-12 secrets per minute
- **Parallel Processing**: Scales with Function instances

## Cost Optimization

### Resource Costs

1. **Function App (Consumption Plan)**
   - Pay per execution
   - First 1 million executions free
   - Typical cost: < $5/month for moderate usage

2. **Storage Account**
   - Queue storage: Minimal cost
   - Blob storage for function: < $1/month

3. **Event Grid**
   - System topics: Free
   - Operations: $0.60 per million operations

4. **Key Vault**
   - Operations: $0.03 per 10,000 operations
   - Secret storage: Minimal

### Cost-Saving Tips
- Use Consumption plan for Function App
- Configure appropriate secret expiry periods
- Use LRS storage for non-critical environments

## Compliance and Audit

### Audit Logging

All operations are logged:
- **Key Vault**: Diagnostic logs to Log Analytics
- **Function App**: Application Insights
- **Azure DevOps**: Audit logs
- **Microsoft Entra ID**: Sign-in and audit logs

### Compliance Considerations

- **No Credential Storage**: No passwords stored in code or configuration
- **Least Privilege**: Minimal permissions assigned
- **Encryption**: All data encrypted in transit and at rest
- **Audit Trail**: Complete audit trail of all operations

## Disaster Recovery

### Backup Strategy

1. **Infrastructure as Code**: All resources defined in Bicep
2. **Secret Backup**: Key Vault soft-delete enabled
3. **Code Repository**: Source code in Git

### Recovery Procedures

1. **Complete Disaster**:
   - Redeploy infrastructure using Bicep
   - Restore secrets from backup
   - Redeploy function code

2. **Secret Deletion**:
   - Recover from Key Vault soft-delete
   - Restore from backup if purged

3. **Function Code Issue**:
   - Rollback to previous version
   - Deploy from repository

## Future Enhancements

Potential improvements to consider:

1. **Multi-Secret Type Support**: Extend to rotate other secret types
2. **Notification System**: Send alerts on successful/failed rotations
3. **Dashboard**: Real-time monitoring dashboard
4. **Testing Framework**: Automated integration tests
5. **Multi-Cloud Support**: Extend to AWS, GCP service accounts
6. **Approval Workflow**: Optional approval before rotation
7. **Custom Rotation Policies**: Flexible rotation schedules
8. **Secret Validation**: Verify new secrets work before completing rotation
