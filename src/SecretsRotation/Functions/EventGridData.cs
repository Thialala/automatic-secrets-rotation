using System.Text.Json.Serialization;

namespace SecretsRotation.Functions;

public class EventGridData
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("topic")]
    public string Topic { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    [JsonPropertyName("data")]
    public Data Data { get; set; }

    [JsonPropertyName("dataVersion")]
    public string dataVersion { get; set; }

    [JsonPropertyName("metadataVersion")]
    public string metadataVersion { get; set; }

    [JsonPropertyName("eventTime")]
    public DateTime eventTime { get; set; }
}

public class Data
{
    public string Id { get; set; }
    public string VaultName { get; set; }
    public string ObjectType { get; set; }
    public string ObjectName { get; set; }
    public string Version { get; set; }
    public object NBF { get; set; }
    public int EXP { get; set; }
}