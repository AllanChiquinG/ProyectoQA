using MediCore.Contracts;
using MediCore.Domain.Identity;

namespace MediCore.UnitTests.Identity;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static User NewUser() => new() { Email = "a@example.com", NormalizedEmail = "a@example.com", DisplayName = "A", PasswordHash = "x" };

    [Fact]
    public void Cuatro_intentos_fallidos_no_bloquean()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedAttempts - 1; i++) user.RegisterFailedLogin(Now);

        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void Cinco_intentos_fallidos_bloquean_por_quince_minutos()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedAttempts; i++) user.RegisterFailedLogin(Now);

        Assert.True(user.IsLockedOut(Now));
        Assert.True(user.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(15)));
    }

    [Fact]
    public void Un_inicio_de_sesion_correcto_reinicia_el_contador()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedAttempts - 1; i++) user.RegisterFailedLogin(Now);
        user.RegisterSuccessfulLogin();
        user.RegisterFailedLogin(Now);

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(1, user.FailedLoginCount);
    }

    [Fact]
    public void Un_bloqueo_vencido_no_arrastra_intentos_anteriores()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedAttempts; i++) user.RegisterFailedLogin(Now);
        var later = Now.AddMinutes(16);

        user.RegisterFailedLogin(later);

        Assert.False(user.IsLockedOut(later));
        Assert.Equal(1, user.FailedLoginCount);
    }

    [Theory]
    [InlineData("  Ana.Garcia@Example.COM ", "ana.garcia@example.com")]
    public void El_correo_se_normaliza(string input, string expected) => Assert.Equal(expected, User.NormalizeEmail(input));

    [Fact]
    public void Los_roles_del_contrato_coinciden_con_los_del_dominio()
    {
        var domainRoles = Enum.GetNames<UserRole>().Order().ToArray();

        Assert.Equal(domainRoles, Roles.Todos.Order().ToArray());
    }
}
