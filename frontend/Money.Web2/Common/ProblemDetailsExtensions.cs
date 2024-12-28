using Microsoft.FluentUI.AspNetCore.Components;
using Money.ApiClient;

namespace Money.Web2.Common;

public static class ProblemDetailsExtensions
{
    public static ProblemDetails? ShowMessage(this ProblemDetails? problemDetails, IToastService toast)
    {
        if (problemDetails == null)
        {
            return null;
        }

        toast.ShowError(problemDetails.Title);
        return problemDetails;
    }

    public static bool HasError(this ProblemDetails? problemDetails)
    {
        return problemDetails != null;
    }
}
