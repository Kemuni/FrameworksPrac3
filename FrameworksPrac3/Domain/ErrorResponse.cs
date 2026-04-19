namespace FrameworksPrac3.Domain;

public sealed record ErrorResponse(string Code, string Message, string RequestId);
