namespace Salryn.Core.Models;

public sealed class PrintJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string JobType { get; init; }

    public required string ReferenceType { get; init; }

    public Guid ReferenceId { get; init; }

    public string? PrinterName { get; init; }

    public required string PayloadText { get; init; }

    public PrintJobStatus Status { get; init; } = PrintJobStatus.Pending;

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PrintedAt { get; init; }
}

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid? UserId { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? OldValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? Reason { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ErrorLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid? UserId { get; init; }

    public required string Area { get; init; }

    public required string Message { get; init; }

    public string? TechnicalDetail { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ImportExportJob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public ImportExportJobType JobType { get; init; }

    public required string FilePath { get; init; }

    public JobStatus Status { get; init; } = JobStatus.Pending;

    public int TotalRows { get; init; }

    public int SuccessRows { get; init; }

    public int FailedRows { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ErrorSummary { get; init; }
}

public sealed class BackupLog
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string FilePath { get; init; }

    public JobStatus Status { get; init; } = JobStatus.Pending;

    public long? FileSizeBytes { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ErrorMessage { get; init; }
}
