using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Common.Persistence;

public abstract class GeneralEntityBase
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string LastUpdateUser { get; set; } = string.Empty;

    public DateTime LastUpdateTime { get; set; }
}
