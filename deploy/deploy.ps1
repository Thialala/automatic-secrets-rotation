param (
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$ApplicationClientId
)

# Get the principalId of the Managed Identity from Bicep deployment outputs
$deploymentOutputs = az deployment group create --resource-group $ResourceGroupName --template-file .\main.bicep --query properties.outputs --output json | ConvertFrom-Json
$managedIdentityPrincipalId = $deploymentOutputs.managedIdentityPrincipalId.value

# Add the Managed Identity as an owner of the Azure AD application
az ad app owner add --id $ApplicationClientId --owner-object-id $managedIdentityPrincipalId