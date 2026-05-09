using System.ComponentModel;
using QaaS.Framework.Serialization;
using Qaas.Mocker.CommunicationObjects.ConfigurationObjects.Command;

namespace QaaS.Runner.Sessions.ConfigurationObjects;

public record ConsumeCommandConfig : Consume
{
    [Description(
        "The deserializer to use to deserialize the consumed input data received by the mocker"
    )]
    [DefaultValue(null)]
    public DeserializeConfig? InputDeserialize { get; internal set; }

    [Description(
        "The deserializer to use to deserialize the consumed output data published by the mocker"
    )]
    [DefaultValue(null)]
    public DeserializeConfig? OutputDeserialize { get; internal set; }

    public DeserializeConfig? ReadInputDeserialize() => InputDeserialize;

    public DeserializeConfig? ReadOutputDeserialize() => OutputDeserialize;
}
