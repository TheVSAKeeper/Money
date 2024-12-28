using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.FluentUI.AspNetCore.Components;
using Money.Web2.Common;
using Money.Web2.Models;
using Money.Web2.Services.Authentication;
using System.ComponentModel.DataAnnotations;

namespace Money.Web2.Pages.Account;

public partial class Login
{
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = new();

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; } = null;

    [Inject]
    private AuthenticationService AuthenticationService { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    [Inject]
    private IToastService Snackbar { get; set; } = default!;

    public Task LoginUserAsync(EditContext context)
    {
        UserDto user = new(Input.Email, Input.Password);

        return AuthenticationService.LoginAsync(user)
            .TapError(message => Snackbar.ShowError($"Ошибка во время входа {message}"))
            .Tap(() => NavigationManager.ReturnTo(ReturnUrl));
    }

    private sealed class InputModel
    {
        [Required(ErrorMessage = "Email обязателен.")]
        [EmailAddress(ErrorMessage = "Некорректный email.")]
        [Display(Name = "Электронная почта")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Пароль обязателен.")]
        [StringLength(100, ErrorMessage = "Пароль должен быть длиной от {2} до {1} символов.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = "";
    }
}
