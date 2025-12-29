using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Assistant.Net.Data.Entities;

[Table("Users")]
public class UserEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public decimal Id { get; set; }

    public string? About { get; set; }

    public DateTime? LastSeen { get; set; }
}