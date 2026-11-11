namespace Handrail;

/// <summary>The arguments do not fit the tool's schema. The endpoint turns this into a 400 with the message, never a stack trace.</summary>
public sealed class WebMcpInputException(string message) : Exception(message);
