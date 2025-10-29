using System.Text.Json.Serialization;

namespace SecretsRotation.Functions;

/// <summary>
/// Represents an Event Grid event for Key Vault secret events.
/// This data model maps to the Event Grid schema for Azure Key Vault events.
/// </summary>
public class EventGridData
{
    /// <summary>
    /// Unique identifier for the event.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// Full resource path to the event source (Key Vault).
    /// Example: "/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.KeyVault/vaults/{vault-name}"
    /// </summary>
    [JsonPropertyName("topic")]
    public string Topic { get; set; }

    /// <summary>
    /// Resource path to the specific object that triggered the event.
    /// Example: "secrets/{secret-name}"
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    /// <summary>
    /// Type of the event.
    /// Common values: "Microsoft.KeyVault.SecretNearExpiry", "Microsoft.KeyVault.SecretExpired"
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    /// <summary>
    /// Event-specific data containing details about the Key Vault object.
    /// </summary>
    [JsonPropertyName("data")]
    public Data Data { get; set; }

    /// <summary>
    /// Schema version of the data property.
    /// </summary>
    [JsonPropertyName("dataVersion")]
    public string dataVersion { get; set; }

    /// <summary>
    /// Schema version of the event metadata.
    /// </summary>
    [JsonPropertyName("metadataVersion")]
    public string metadataVersion { get; set; }

    /// <summary>
    /// Time when the event occurred in UTC.
    /// </summary>
    [JsonPropertyName("eventTime")]
    public DateTime eventTime { get; set; }
}

/// <summary>
/// Contains detailed information about the Key Vault object that triggered the event.
/// </summary>
public class Data
{
    /// <summary>
    /// Full URI to the Key Vault object.
    /// Example: "https://{vault-name}.vault.azure.net/secrets/{secret-name}/{version}"
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Name of the Key Vault that contains the object.
    /// </summary>
    public string VaultName { get; set; }

    /// <summary>
    /// Type of the Key Vault object.
    /// Common values: "Secret", "Key", "Certificate"
    /// </summary>
    public string ObjectType { get; set; }

    /// <summary>
    /// Name of the Key Vault object (e.g., secret name).
    /// </summary>
    public string ObjectName { get; set; }

    /// <summary>
    /// Version identifier of the Key Vault object.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Not Before timestamp - when the object becomes valid (Unix timestamp).
    /// May be null if not set.
    /// </summary>
    public object NBF { get; set; }

    /// <summary>
    /// Expiration timestamp - when the object expires (Unix timestamp).
    /// </summary>
    public int EXP { get; set; }
}