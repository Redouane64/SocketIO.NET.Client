using System;
using System.Text.Json.Serialization;

namespace EngineIO.Client.Transports.Exceptions;

internal class BadRequestError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}

public class BadRequestException : Exception
{
    public int Code { get; }
    public string Message { get; }
    
    public BadRequestException(int code, string message)
        : base(message)
    {
        Code = code;
        Message = message;
    }
}
