using Wolverine.Runtime.Serialization;

namespace Wolverine.EntityFrameworkCore.Internals;

public class IncomingMessage
{
    public IncomingMessage()
    {
    }

    public IncomingMessage(Envelope envelope)
    {
        Id = envelope.Id;
        Status = envelope.Status.ToString();
        OwnerId = envelope.OwnerId;
        ExecutionTime = envelope.ScheduledTime?.ToUniversalTime();
        Attempts = envelope.Attempts;
        Body = EnvelopeSerializer.Serialize(envelope);
        MessageType = envelope.MessageType!;
        ReceivedAt = envelope.Destination?.ToString();
    }

    public Guid Id { get; set; }
    public string Status { get; set; } = EnvelopeStatus.Incoming.ToString();
    public int OwnerId { get; set; }
    public DateTimeOffset? ExecutionTime { get; set; }
    public int Attempts { get; set; }
    public byte[] Body { get; set; } = [];
    public string MessageType { get; set; } = string.Empty;
    public string? ReceivedAt { get; set; }
    public DateTimeOffset? KeepUntil { get; set; }

    public Envelope ToEnvelope()
    {
        
        var envelope = EnvelopeSerializer.Deserialize(Body);
        envelope.Id = Id;
        envelope.Status = Enum.Parse<EnvelopeStatus>(Status);
        envelope.OwnerId = OwnerId;
        envelope.ScheduledTime = ExecutionTime;
        envelope.Attempts = Attempts;
        envelope.MessageType = MessageType;
        envelope.ScheduledTime = ExecutionTime;
        if (ReceivedAt != null)
        {
            envelope.Destination = new Uri(ReceivedAt);
        }

        return envelope;
    }
}