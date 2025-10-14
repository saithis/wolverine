using JasperFx.Core.Reflection;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime.Serialization;

namespace Wolverine.EntityFrameworkCore.Internals;

public class DeadLetterMessage
{
    public DeadLetterMessage()
    {
    }

    public DeadLetterMessage(Envelope envelope, Exception? exception)
    {
        Id = envelope.Id;
        ExecutionTime = envelope.ScheduledTime?.ToUniversalTime();
        Body = EnvelopeSerializer.Serialize(envelope);
        MessageType = envelope.MessageType!;
        ReceivedAt = envelope.Destination?.ToString();
        Source = envelope.Source;
        ExceptionType = exception?.GetType().FullNameInCode();
        ExceptionMessage = exception?.Message;
        SentAt = envelope.SentAt.ToUniversalTime();
        Replayable = false; // Default to false, can be set later
    }

    public Guid Id { get; set; }
    public DateTimeOffset? ExecutionTime { get; set; }
    public byte[] Body { get; set; } = [];
    public string MessageType { get; set; } = string.Empty;
    public string? ReceivedAt { get; set; }
    public string? Source { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public bool Replayable { get; set; }
    public DateTimeOffset? Expires { get; set; }

    public DeadLetterEnvelope ToEnvelope()
    {
        var envelope = EnvelopeSerializer.Deserialize(Body);
        return new DeadLetterEnvelope(Id, ExecutionTime, envelope, MessageType, ReceivedAt ?? string.Empty, Source ?? string.Empty, ExceptionType ?? string.Empty, ExceptionMessage ?? string.Empty, SentAt, Replayable);
    }
}
