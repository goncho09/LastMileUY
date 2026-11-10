using Microsoft.AspNetCore.Identity;

namespace LastMileUY.API.Modules.Identidad.Infrastructure;

public sealed class IdentityErrorDescriberEs : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new()
        {
            Code = nameof(PasswordTooShort),
            Description = $"La contraseña debe tener al menos {length} caracteres."
        };

    public override IdentityError PasswordRequiresLower() =>
        new()
        {
            Code = nameof(PasswordRequiresLower),
            Description = "La contraseña debe contener al menos una letra minúscula."
        };

    public override IdentityError PasswordRequiresUpper() =>
        new()
        {
            Code = nameof(PasswordRequiresUpper),
            Description = "La contraseña debe contener al menos una letra mayúscula."
        };

    public override IdentityError PasswordRequiresDigit() =>
        new()
        {
            Code = nameof(PasswordRequiresDigit),
            Description = "La contraseña debe contener al menos un número."
        };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new()
        {
            Code = nameof(PasswordRequiresNonAlphanumeric),
            Description = "La contraseña debe contener al menos un carácter especial."
        };
}