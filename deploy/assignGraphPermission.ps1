# Input Parameters:
# $appRoleName: The name of the app role to be assigned (e.g., "Application.ReadWrite.OwnedBy")
# $spnObjectId: The object ID of the service principal to which the app role should be assigned
 
param (
    [Parameter(Mandatory=$true)]
    [string]$appRoleName,
    [Parameter(Mandatory=$true)]
    [string]$spnObjectId
)
 
# Define the Microsoft Graph Application ID
$graphAppId = "00000003-0000-0000-c000-000000000000"
 
# Retrieve the resource ID of the Microsoft Graph service principal using the Microsoft Graph App ID
$graphResourceId=$(az ad sp show --id $graphAppId --query 'id' --output tsv)
 
# Retrieve the app role ID for the given appRoleName from the Microsoft Graph service principal
$appRoleId=$(az ad sp show --id $graphAppId --query "appRoles[?value=='$appRoleName' && contains(allowedMemberTypes, 'Application')].id" --output tsv)
 
# Define the URI for assigning the app role to the service principal
$uri="https://graph.microsoft.com/v1.0/servicePrincipals/$spnObjectId/appRoleAssignments"
Write-Output $uri
# Create the JSON request body containing the required information for the app role assignment
$body="{'principalId':'$spnObjectId','resourceId':'$graphResourceId','appRoleId':'$appRoleId'}"
 
# Send a POST request to the Microsoft Graph API to create the app role assignment
az rest --method post --uri $uri --body $body --headers "Content-Type=application/json"