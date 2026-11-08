namespace SaasEcommerce.Core.Common;

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public abstract class TenantScopedEntity : BaseEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
}