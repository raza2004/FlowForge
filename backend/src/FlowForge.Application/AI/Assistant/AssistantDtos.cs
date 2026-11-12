namespace FlowForge.Application.AI.Assistant;

/// <summary>A passage of an uploaded document that supports an answer, so a user can verify it.</summary>
public record CitationDto(string DocumentId, string DocumentName, int Page, string Content, double Score);

public record AiUsageDto(string Model, int PromptTokens, int CompletionTokens, double CostUsd, int LatencyMs);

public record AssistantAnswerDto(string Answer, IReadOnlyList<CitationDto> Sources, AiUsageDto Usage, string RetrievalMode);

/// <summary>One earlier message of the conversation (role is "user" or "assistant"), sent so a short follow up like "why" can be understood.</summary>
public record ChatTurnDto(string Role, string Content);

public record AssistantDocumentDto(string DocumentId, string FileName, int Pages, int Chunks);
