namespace Adrover.Domain.Entities;

/// <summary>
/// User notification
/// </summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // CampaignApproved, PaymentReceived, etc.
    public Guid? RelatedResourceId { get; set; } // Advertisement ID, Payment ID, etc.
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
}

/// <summary>
/// Enquiry from WhatsApp or contact form
/// </summary>
public class Enquiry
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? SourceChannel { get; set; } // WhatsApp, Email, WebForm
    public string Status { get; set; } = "New"; // New, In Progress, Resolved
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}

/// <summary>
/// Audit log for compliance and security tracking
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
}
