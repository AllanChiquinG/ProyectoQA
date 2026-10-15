using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Users;

public record ChangeRoleRequest([Required, MaxLength(30)] string Role);
