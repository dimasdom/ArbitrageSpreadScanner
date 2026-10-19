using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace ArbitrageScanner.Infrastructure.Messaging.Kafka;

/// <summary>Metadata every message carries in its Kafka headers.</summary>
public sealed record EnvelopeMetadata(
    string? MessageId,
    string? MessageType,
    string? SchemaVersion,
    string? TraceParent,
    string? TraceState);

public static class MessageHeaders
{
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";
    public const string MessageType = "message-type";
    public const string SchemaVersion = "schema-version";
    public const string MessageId = "message-id";

    public const string DlqError = "dlq-error";
    public const string DlqAttempts = "dlq-attempts";
    public const string DlqSourceTopic = "dlq-source-topic";
    public const string DlqSourcePartition = "dlq-source-partition";
    public const string DlqSourceOffset = "dlq-source-offset";
    public const string DlqFailedAt = "dlq-failed-at";

    /// <summary>Current wire-contract version. Schema evolution: add optional fields only; bump only on a
    /// deliberately incompatible change.</summary>
    public const string CurrentSchemaVersion = "1";

    public static Headers Create(string messageType, string messageId, Activity? activity, string schemaVersion = CurrentSchemaVersion)
    {
        var headers = new Headers
        {
            { MessageId, Encoding.UTF8.GetBytes(messageId) },
            { MessageType, Encoding.UTF8.GetBytes(messageType) },
            { SchemaVersion, Encoding.UTF8.GetBytes(schemaVersion) },
        };

        if (activity is { IdFormat: ActivityIdFormat.W3C, Id: { } traceParent })
        {
            headers.Add(TraceParent, Encoding.UTF8.GetBytes(traceParent));
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                headers.Add(TraceState, Encoding.UTF8.GetBytes(activity.TraceStateString));
            }
        }

        return headers;
    }

    public static EnvelopeMetadata Read(Headers? headers) => new(
        GetString(headers, MessageId),
        GetString(headers, MessageType),
        GetString(headers, SchemaVersion),
        GetString(headers, TraceParent),
        GetString(headers, TraceState));

    public static string? GetString(Headers? headers, string name)
    {
        if (headers is null)
        {
            return null;
        }

        return headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}
